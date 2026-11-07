package com.mirrorsound;

import android.annotation.SuppressLint;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
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

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Captures what other apps play and streams it as 48 kHz stereo PCM to the PC. */
public class StreamService extends Service {
    static final String EXTRA_CODE = "code";
    static final String EXTRA_DATA = "data";
    static final String EXTRA_HOST = "host";
    static final int TCP_PORT = 47800;
    static final int DISCOVERY_PORT = 47801;

    private static final int SAMPLE_RATE = 48000;
    private static final int CHANNELS = 2;
    private static final String CHANNEL_ID = "stream";

    static volatile String status = "Готово к трансляции";
    static volatile boolean running;

    private MediaProjection projection;
    private WifiManager.WifiLock wifiLock;
    private Thread worker;
    private volatile boolean stopping;
    private volatile Socket socket;

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override
    @SuppressWarnings("deprecation")
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || running) {
            if (!running) stopSelf();
            return START_NOT_STICKY;
        }

        startForeground(1, buildNotification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);

        int code = intent.getIntExtra(EXTRA_CODE, 0);
        Intent data = intent.getParcelableExtra(EXTRA_DATA);
        String host = intent.getStringExtra(EXTRA_HOST);

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
        worker = new Thread(() -> stream(host), "mirrorsound-stream");
        worker.start();
        return START_NOT_STICKY;
    }

    @SuppressLint("MissingPermission") // RECORD_AUDIO is checked by MainActivity before starting
    private void stream(String hostList) {
        AudioPlaybackCaptureConfiguration capture = new AudioPlaybackCaptureConfiguration.Builder(projection)
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
                    .setAudioPlaybackCaptureConfig(capture)
                    .build();
            recorder.startRecording();
        } catch (Exception e) {
            status = "Не удалось начать запись звука: " + e.getMessage();
            stopSelf();
            return;
        }

        byte[] header = ByteBuffer.allocate(10).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 'M').put((byte) 'S').put((byte) 'N').put((byte) 'D')
                .putInt(SAMPLE_RATE).putShort((short) CHANNELS).array();
        byte[] chunk = new byte[bytesPerSecond / 100]; // 10 ms

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
                out.write(header);
                status = "Звук идёт на компьютер";
                while (!stopping) {
                    int n = recorder.read(chunk, 0, chunk.length);
                    if (n > 0) out.write(chunk, 0, n);
                }
            } catch (IOException e) {
                if (stopping) break;
                index++;
                if (index % hosts.length != 0) continue; // try the next address right away
                status = "Нет связи с компьютером, пробую снова…";
                // Keep draining the recorder so we don't replay stale audio after reconnecting.
                long until = System.currentTimeMillis() + 1000;
                while (!stopping && System.currentTimeMillis() < until) {
                    recorder.read(chunk, 0, chunk.length);
                }
            }
        }

        recorder.stop();
        recorder.release();
    }

    private Notification buildNotification() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel(CHANNEL_ID, "Трансляция звука",
                NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 0,
                new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("MirrorSound")
                .setContentText("Звук передаётся на компьютер")
                .setSmallIcon(R.drawable.ic_waves)
                .setColor(0xFF8B5CF6)
                .setContentIntent(open)
                .setOngoing(true)
                .build();
    }

    @Override
    public void onDestroy() {
        stopping = true;
        Socket s = socket;
        if (s != null) {
            try { s.close(); } catch (IOException ignored) { }
        }
        if (worker != null) {
            try { worker.join(500); } catch (InterruptedException ignored) { }
        }
        if (projection != null) projection.stop();
        if (wifiLock != null && wifiLock.isHeld()) wifiLock.release();
        running = false;
        status = "Готово к трансляции";
        super.onDestroy();
    }
}

