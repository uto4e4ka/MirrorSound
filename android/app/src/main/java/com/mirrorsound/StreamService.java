package com.mirrorsound;

import android.annotation.SuppressLint;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioPlaybackCaptureConfiguration;
import android.media.AudioRecord;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.net.wifi.WifiManager;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.service.quicksettings.TileService;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;
import java.util.Collections;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Captures what other apps play (48 kHz stereo PCM) and sends it to the PC app, if one is chosen,
 * and to any browser that opens the phone's web player.
 */
public class StreamService extends Service {
    static final String EXTRA_CODE = "code";
    static final String EXTRA_DATA = "data";
    static final String EXTRA_HOST = "host";
    static final String ACTION_STOP = "com.mirrorsound.STOP";
    static final int TCP_PORT = 47800;
    static final int DISCOVERY_PORT = 47801;

    private static final int SAMPLE_RATE = 48000;
    private static final int CHANNELS = 2;
    private static final String CHANNEL_ID = "stream";
    static final String IDLE_STATUS = "Готово к трансляции";

    static volatile String status = IDLE_STATUS;
    static volatile boolean running;
    /** Address of the web player while streaming, e.g. http://192.168.0.149:47803 */
    static volatile String webUrl;
    static volatile WebPlayer web;

    private MediaProjection projection;
    private WifiManager.WifiLock wifiLock;
    private Thread capture, pcLink;
    private volatile boolean stopping;
    private volatile Socket socket;
    private volatile boolean pcConnected;
    private final ArrayBlockingQueue<byte[]> pcQueue = new ArrayBlockingQueue<>(50);

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override
    @SuppressWarnings("deprecation")
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (intent == null || running) {
            if (!running) stopSelf();
            return START_NOT_STICKY;
        }

        String host = intent.getStringExtra(EXTRA_HOST);
        boolean toPc = host != null && !host.trim().isEmpty();
        startForeground(1, buildNotification(toPc), ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);

        int code = intent.getIntExtra(EXTRA_CODE, 0);
        Intent data = intent.getParcelableExtra(EXTRA_DATA);
        MediaProjectionManager mpm = getSystemService(MediaProjectionManager.class);
        projection = mpm.getMediaProjection(code, data);
        if (projection == null) {
            status = "Не удалось получить доступ к звуку";
            stopSelf();
            return START_NOT_STICKY;
        }
        projection.registerCallback(new MediaProjection.Callback() {
            @Override public void onStop() { stopSelf(); }
        }, new Handler(Looper.getMainLooper()));

        WifiManager wifi = getSystemService(WifiManager.class);
        wifiLock = wifi.createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "mirrorsound");
        wifiLock.acquire();

        running = true;
        stopping = false;
        status = toPc ? "Подключение…" : "Звук доступен в браузере";
        startWebPlayer();
        capture = new Thread(this::capture, "mirrorsound-capture");
        capture.start();
        if (toPc) {
            pcLink = new Thread(() -> sendToPc(host), "mirrorsound-pc");
            pcLink.start();
        }
        updateTile();
        return START_NOT_STICKY;
    }

    private byte[] header() {
        return ByteBuffer.allocate(10).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 'M').put((byte) 'S').put((byte) 'N').put((byte) 'D')
                .putInt(SAMPLE_RATE).putShort((short) CHANNELS).array();
    }

    private void startWebPlayer() {
        try (InputStream in = getAssets().open("player.html")) {
            ByteArrayOutputStream page = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) page.write(buf, 0, n);
            WebPlayer player = new WebPlayer(page.toByteArray(), header());
            player.start();
            web = player;
            String ip = localAddress();
            webUrl = ip == null ? null : "http://" + ip + ":" + WebPlayer.PORT;
        } catch (IOException e) {
            web = null;
            webUrl = null;
        }
    }

    /** The phone's address on Wi-Fi (or its own hotspot), which browsers on the network can reach. */
    static String localAddress() {
        String fallback = null;
        try {
            for (NetworkInterface nic : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!nic.isUp() || nic.isLoopback()) continue;
                for (InetAddress a : Collections.list(nic.getInetAddresses())) {
                    if (!(a instanceof Inet4Address) || !a.isSiteLocalAddress()) continue;
                    String name = nic.getName();
                    if (name.startsWith("wlan") || name.startsWith("swlan") || name.startsWith("ap")
                            || name.startsWith("eth")) {
                        return a.getHostAddress();
                    }
                    if (fallback == null && !name.startsWith("rmnet") && !name.startsWith("tun")) {
                        fallback = a.getHostAddress();
                    }
                }
            }
        } catch (Exception ignored) {
        }
        return fallback;
    }

    @SuppressLint("MissingPermission") // RECORD_AUDIO is checked by MainActivity before starting
    private void capture() {
        AudioPlaybackCaptureConfiguration config = new AudioPlaybackCaptureConfiguration.Builder(projection)
                .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                .addMatchingUsage(AudioAttributes.USAGE_GAME)
                .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                .build();
        AudioFormat format = new AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(SAMPLE_RATE)
                .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
                .build();
        int minBuffer = AudioRecord.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_IN_STEREO,
                AudioFormat.ENCODING_PCM_16BIT);
        int bytesPerSecond = SAMPLE_RATE * CHANNELS * 2;

        AudioRecord recorder;
        try {
            recorder = new AudioRecord.Builder()
                    .setAudioFormat(format)
                    .setBufferSizeInBytes(Math.max(minBuffer * 2, bytesPerSecond / 10))
                    .setAudioPlaybackCaptureConfig(config)
                    .build();
            recorder.startRecording();
        } catch (Exception e) {
            status = "Не удалось начать запись звука: " + e.getMessage();
            stopSelf();
            return;
        }

        byte[] chunk = new byte[bytesPerSecond / 100]; // 10 ms
        while (!stopping) {
            int n = recorder.read(chunk, 0, chunk.length);
            if (n <= 0) continue;
            byte[] piece = Arrays.copyOf(chunk, n);
            if (pcConnected && !pcQueue.offer(piece)) {
                pcQueue.clear(); // PC fell half a second behind: drop the backlog
                pcQueue.offer(piece);
            }
            WebPlayer player = web;
            if (player != null) player.broadcast(piece);
        }

        recorder.stop();
        recorder.release();
    }

    private void sendToPc(String hostList) {
        // The QR code may carry several PC addresses; try them in turn and stick with the one that works.
        String[] hosts = hostList.trim().split("[,\\s]+");
        int index = 0;
        while (!stopping) {
            String host = hosts[index % hosts.length];
            try (Socket s = new Socket()) {
                socket = s;
                status = "Подключение к " + host + "…";
                s.setTcpNoDelay(true);
                s.connect(new InetSocketAddress(host, TCP_PORT), 1500);
                OutputStream out = s.getOutputStream();
                out.write(header());
                pcQueue.clear();
                pcConnected = true;
                status = "Звук идёт на компьютер";
                while (!stopping) {
                    byte[] piece = pcQueue.poll(500, TimeUnit.MILLISECONDS);
                    if (piece != null) out.write(piece);
                }
            } catch (IOException e) {
                pcConnected = false;
                if (stopping) break;
                index++;
                if (index % hosts.length != 0) continue; // try the next address right away
                status = "Нет связи с компьютером, пробую снова…";
                try { Thread.sleep(1000); } catch (InterruptedException ie) { break; }
            } catch (InterruptedException e) {
                break;
            }
        }
        pcConnected = false;
    }

    private Notification buildNotification(boolean toPc) {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel(CHANNEL_ID, "Трансляция звука",
                NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 0,
                new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop = PendingIntent.getService(this, 1,
                new Intent(this, StreamService.class).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("MirrorSound")
                .setContentText(toPc ? "Звук передаётся на компьютер" : "Звук можно слушать в браузере")
                .setSmallIcon(R.drawable.ic_waves)
                .setColor(0xFF8B5CF6)
                .setContentIntent(open)
                .addAction(new Notification.Action.Builder(null, "Остановить", stop).build())
                .setOngoing(true)
                .build();
    }

    private void updateTile() {
        TileService.requestListeningState(this, new ComponentName(this, SoundTileService.class));
    }

    @Override
    public void onDestroy() {
        stopping = true;
        Socket s = socket;
        if (s != null) {
            try { s.close(); } catch (IOException ignored) { }
        }
        WebPlayer player = web;
        web = null;
        webUrl = null;
        if (player != null) player.close();
        if (pcLink != null) pcLink.interrupt();
        if (capture != null) {
            try { capture.join(500); } catch (InterruptedException ignored) { }
        }
        if (projection != null) projection.stop();
        if (wifiLock != null && wifiLock.isHeld()) wifiLock.release();
        running = false;
        status = IDLE_STATUS;
        updateTile();
        super.onDestroy();
    }
}
