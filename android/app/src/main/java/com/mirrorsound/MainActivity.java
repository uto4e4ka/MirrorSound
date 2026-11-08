package com.mirrorsound;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.StatusBarManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Icon;
import android.graphics.drawable.RippleDrawable;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.google.mlkit.vision.barcode.common.Barcode;
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions;
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;

public class MainActivity extends Activity {
    /** Sent by the quick settings tile: ask for capture permission, start, and get out of the way. */
    static final String ACTION_START = "com.mirrorsound.START";
    private static final int REQ_PROJECTION = 1;
    private static final int REQ_PERMISSIONS = 2;

    private static final int BG_TOP = 0xFF1A1530;
    private static final int BG_MID = 0xFF13121B;
    private static final int BG_BOTTOM = 0xFF0B0C10;
    private static final int CARD = 0xFF181A22;
    private static final int CARD_BORDER = 0xFF262935;
    private static final int TEXT = 0xFFECEEF2;
    private static final int MUTED = 0xFF8A909C;
    private static final int LAVENDER = 0xFFC4B5FD;
    private static final int VIOLET = 0xFF8B5CF6;
    private static final int BLUE = 0xFF3B82F6;

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Runnable poll = new Runnable() {
        @Override public void run() {
            refresh();
            ui.postDelayed(this, 400);
        }
    };

    private SharedPreferences prefs;
    private String hosts = "";
    private String pcName = "";
    private boolean fromTile;

    private PulseButton power;
    private TextView statusView;
    private TextView hintView;
    private TextView webView;
    private TextView pcTitle;
    private TextView pcAddress;
    private TextView infoView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("mirrorsound", MODE_PRIVATE);
        hosts = prefs.getString("host", "");
        pcName = prefs.getString("name", "");

        getWindow().setStatusBarColor(BG_TOP);
        getWindow().setNavigationBarColor(BG_BOTTOM);
        setContentView(buildUi());
        showPc();
        refresh();

        if (handleStart(getIntent())) return;
        if (!handleLink(getIntent()) && hosts.isEmpty()) discover();
    }

    // ---------------------------------------------------------------- UI

    private View buildUi() {
        FrameLayout frame = new FrameLayout(this);
        frame.setBackground(new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[]{BG_TOP, BG_MID, BG_BOTTOM}));

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(22), dp(18), dp(22), dp(22));
        frame.addView(root, new FrameLayout.LayoutParams(-1, -1));

        // Header: logo + name
        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.ic_waves);
        logo.setPadding(dp(9), dp(9), dp(9), dp(9));
        logo.setBackground(gradient(dp(13)));
        header.addView(logo, new LinearLayout.LayoutParams(dp(42), dp(42)));
        LinearLayout titles = new LinearLayout(this);
        titles.setOrientation(LinearLayout.VERTICAL);
        titles.setPadding(dp(12), 0, 0, 0);
        titles.addView(text("MirrorSound", 20, TEXT, true));
        titles.addView(text("Звук телефона на компьютере", 13, MUTED, false));
        header.addView(titles);
        root.addView(header);

        root.addView(new View(this), new LinearLayout.LayoutParams(-1, 0, 1f));

        // Power button + status
        power = new PulseButton(this);
        power.setOnClickListener(v -> toggle());
        LinearLayout.LayoutParams powerLp = new LinearLayout.LayoutParams(dp(290), dp(290));
        powerLp.gravity = Gravity.CENTER_HORIZONTAL;
        root.addView(power, powerLp);

        statusView = text("", 20, TEXT, true);
        statusView.setGravity(Gravity.CENTER);
        root.addView(statusView, marginTop(dp(4)));
        hintView = text("", 14, MUTED, false);
        hintView.setGravity(Gravity.CENTER);
        root.addView(hintView, marginTop(dp(4)));

        // While streaming: the address to open in a browser (tap to copy)
        webView = text("", 14, LAVENDER, true);
        webView.setGravity(Gravity.CENTER);
        webView.setPadding(dp(14), dp(10), dp(14), dp(10));
        webView.setBackground(ripple(rounded(0x1A8B5CF6, 0x338B5CF6, dp(14)), dp(14)));
        webView.setOnClickListener(v -> copyWebUrl());
        LinearLayout.LayoutParams webLp = new LinearLayout.LayoutParams(-2, -2);
        webLp.gravity = Gravity.CENTER_HORIZONTAL;
        webLp.topMargin = dp(14);
        root.addView(webView, webLp);

        root.addView(new View(this), new LinearLayout.LayoutParams(-1, 0, 1f));

        // Computer card
        LinearLayout card = new LinearLayout(this);
        card.setGravity(Gravity.CENTER_VERTICAL);
        card.setPadding(dp(16), dp(14), dp(12), dp(14));
        card.setBackground(rounded(CARD, CARD_BORDER, dp(20)));
        ImageView pcIcon = new ImageView(this);
        pcIcon.setImageResource(R.drawable.ic_computer);
        pcIcon.setPadding(dp(9), dp(9), dp(9), dp(9));
        pcIcon.setBackground(rounded(0x338B5CF6, 0, dp(12)));
        card.addView(pcIcon, new LinearLayout.LayoutParams(dp(42), dp(42)));
        LinearLayout pcText = new LinearLayout(this);
        pcText.setOrientation(LinearLayout.VERTICAL);
        pcText.setPadding(dp(12), 0, dp(8), 0);
        pcTitle = text("", 16, TEXT, true);
        pcAddress = text("", 13, MUTED, false);
        pcText.addView(pcTitle);
        pcText.addView(pcAddress);
        card.addView(pcText, new LinearLayout.LayoutParams(0, -2, 1f));
        TextView change = text("Изменить", 14, LAVENDER, true);
        change.setPadding(dp(10), dp(8), dp(10), dp(8));
        change.setBackground(ripple(null, dp(10)));
        change.setOnClickListener(v -> editHost());
        card.addView(change);
        root.addView(card);

        // Actions
        LinearLayout actions = new LinearLayout(this);
        actions.setPadding(0, dp(12), 0, 0);
        TextView scan = pill("Сканировать QR", true);
        scan.setOnClickListener(v -> scan());
        TextView find = pill("Найти в сети", false);
        find.setOnClickListener(v -> discover());
        LinearLayout.LayoutParams left = new LinearLayout.LayoutParams(0, dp(54), 1f);
        left.rightMargin = dp(6);
        LinearLayout.LayoutParams right = new LinearLayout.LayoutParams(0, dp(54), 1f);
        right.leftMargin = dp(6);
        actions.addView(scan, left);
        actions.addView(find, right);
        root.addView(actions);

        infoView = text("", 13, MUTED, false);
        infoView.setGravity(Gravity.CENTER);
        infoView.setMinHeight(dp(36));
        root.addView(infoView, marginTop(dp(10)));

        if (Build.VERSION.SDK_INT >= 33) {
            TextView addTile = text("Добавить кнопку в шторку", 13, LAVENDER, true);
            addTile.setGravity(Gravity.CENTER);
            addTile.setPadding(dp(10), dp(6), dp(10), dp(6));
            addTile.setBackground(ripple(null, dp(10)));
            addTile.setOnClickListener(v -> addTile());
            LinearLayout.LayoutParams tileLp = new LinearLayout.LayoutParams(-2, -2);
            tileLp.gravity = Gravity.CENTER_HORIZONTAL;
            root.addView(addTile, tileLp);
        }

        return frame;
    }

    private void refresh() {
        boolean running = StreamService.running;
        power.setActive(running);
        statusView.setText(StreamService.status);
        hintView.setText(running ? "Нажмите, чтобы остановить" : "Нажмите, чтобы начать");
        String url = StreamService.webUrl;
        WebPlayer web = StreamService.web;
        if (running && url != null) {
            int listeners = web == null ? 0 : web.listenerCount();
            webView.setText("Слушать в браузере: " + url.replace("http://", "") +
                    (listeners > 0 ? "\nСлушают: " + listeners : ""));
            webView.setVisibility(View.VISIBLE);
        } else {
            webView.setVisibility(View.GONE);
        }
    }

    private void copyWebUrl() {
        String url = StreamService.webUrl;
        if (url == null) return;
        getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText("MirrorSound", url));
        infoView.setText("Адрес скопирован. Откройте его в браузере на компьютере в той же сети.");
    }

    private void addTile() {
        if (Build.VERSION.SDK_INT < 33) return;
        getSystemService(StatusBarManager.class).requestAddTileService(
                new ComponentName(this, SoundTileService.class), "MirrorSound",
                Icon.createWithResource(this, R.drawable.ic_waves), getMainExecutor(),
                result -> infoView.setText(
                        result == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ALREADY_ADDED
                                ? "Кнопка уже есть в шторке."
                                : result == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ADDED
                                ? "Готово: кнопка MirrorSound в шторке." : ""));
    }

    private void showPc() {
        if (hosts.isEmpty()) {
            pcTitle.setText("Компьютер не выбран");
            pcAddress.setText("Звук можно слушать в браузере");
        } else {
            pcTitle.setText(pcName.isEmpty() ? "Компьютер" : pcName);
            pcAddress.setText(hosts.split(",")[0]);
        }
    }

    private void setPc(String newHosts, String name) {
        hosts = newHosts.trim();
        if (name != null) pcName = name;
        prefs.edit().putString("host", hosts).putString("name", pcName).apply();
        showPc();
    }

    private void editHost() {
        EditText field = new EditText(this);
        field.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        field.setHint("Например, 192.168.0.10");
        field.setText(hosts.split(",")[0]);
        FrameLayout box = new FrameLayout(this);
        box.setPadding(dp(22), dp(8), dp(22), 0);
        box.addView(field);
        new AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                .setTitle("IP компьютера")
                .setMessage("Показан в окне MirrorSound на ПК, в строке «Сеть».")
                .setView(box)
                .setPositiveButton("Сохранить", (d, w) -> {
                    String value = field.getText().toString().trim();
                    if (!value.isEmpty()) setPc(value, "");
                })
                .setNegativeButton("Отмена", null)
                .show();
    }

    // ---------------------------------------------------------------- connecting

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        if (!handleStart(intent)) handleLink(intent);
    }

    private boolean handleStart(Intent intent) {
        if (intent == null || !ACTION_START.equals(intent.getAction())) return false;
        intent.setAction(null);
        fromTile = true;
        if (StreamService.running) {
            finish();
        } else {
            begin();
        }
        return true;
    }

    /** Opened from the PC's page via mirrorsound://connect?h=ip1,ip2&n=NAME */
    private boolean handleLink(Intent intent) {
        if (intent == null || intent.getData() == null) return false;
        Uri uri = intent.getData();
        intent.setData(null); // don't re-handle on rotation
        return connectTo(uri);
    }

    /** Accepts mirrorsound://connect?h=... or the PC page URL http://ip:47802/?h=... */
    private boolean connectTo(Uri uri) {
        String list = uri.getQueryParameter("h");
        if (list == null || list.trim().isEmpty()) list = uri.getHost();
        if (list == null || list.isEmpty() || list.equals("connect")) return false;
        setPc(list, uri.getQueryParameter("n"));
        infoView.setText("");
        if (!StreamService.running) begin();
        return true;
    }

    private void scan() {
        GmsBarcodeScannerOptions options = new GmsBarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .build();
        GmsBarcodeScanning.getClient(this, options).startScan()
                .addOnSuccessListener(barcode -> {
                    String raw = barcode.getRawValue();
                    if (raw == null || !connectTo(Uri.parse(raw))) {
                        infoView.setText("Это не QR-код MirrorSound.");
                    }
                })
                .addOnFailureListener(e -> infoView.setText("Не удалось открыть сканер: " + e.getMessage()));
    }

    private void toggle() {
        if (StreamService.running) {
            stopService(new Intent(this, StreamService.class));
        } else {
            begin();
        }
    }

    private void begin() {
        // Without a chosen PC the phone still streams to browsers (web player).
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            String[] perms = Build.VERSION.SDK_INT >= 33
                    ? new String[]{Manifest.permission.RECORD_AUDIO, Manifest.permission.POST_NOTIFICATIONS}
                    : new String[]{Manifest.permission.RECORD_AUDIO};
            requestPermissions(perms, REQ_PERMISSIONS);
            return;
        }
        MediaProjectionManager mpm = getSystemService(MediaProjectionManager.class);
        startActivityForResult(mpm.createScreenCaptureIntent(), REQ_PROJECTION);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        if (requestCode != REQ_PERMISSIONS) return;
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
            begin();
        } else {
            infoView.setText("Без разрешения на запись звука передавать звук нельзя.");
            fromTile = false;
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_PROJECTION) return;
        if (resultCode != RESULT_OK || data == null) {
            infoView.setText("Трансляция отменена.");
            if (fromTile) finish();
            return;
        }
        Intent service = new Intent(this, StreamService.class)
                .putExtra(StreamService.EXTRA_CODE, resultCode)
                .putExtra(StreamService.EXTRA_DATA, data)
                .putExtra(StreamService.EXTRA_HOST, hosts);
        startForegroundService(service);
        infoView.setText("");
        if (fromTile) {
            fromTile = false;
            finish(); // started from the shade: back to whatever the user was doing
        }
    }

    /** Broadcasts a UDP query; the PC app answers with its name. */
    private void discover() {
        infoView.setText("Ищу компьютер в сети…");
        new Thread(() -> {
            String address = null;
            String name = null;
            try (DatagramSocket socket = new DatagramSocket()) {
                socket.setBroadcast(true);
                socket.setSoTimeout(2000);
                byte[] query = "MIRRORSOUND?".getBytes(StandardCharsets.UTF_8);
                InetAddress broadcast = InetAddress.getByName("255.255.255.255");
                for (int attempt = 0; attempt < 2 && address == null; attempt++) {
                    socket.send(new DatagramPacket(query, query.length, broadcast, StreamService.DISCOVERY_PORT));
                    byte[] buf = new byte[256];
                    DatagramPacket reply = new DatagramPacket(buf, buf.length);
                    try {
                        socket.receive(reply);
                    } catch (java.net.SocketTimeoutException e) {
                        continue;
                    }
                    String text = new String(buf, 0, reply.getLength(), StandardCharsets.UTF_8);
                    if (text.startsWith("MIRRORSOUND!")) {
                        address = reply.getAddress().getHostAddress();
                        name = text.substring("MIRRORSOUND!".length());
                    }
                }
            } catch (Exception ignored) {
            }
            final String foundAddress = address;
            final String foundName = name;
            ui.post(() -> {
                if (foundAddress != null) {
                    setPc(foundAddress, foundName);
                    infoView.setText("Компьютер найден");
                } else {
                    infoView.setText("Компьютер не найден. Запустите MirrorSound на ПК или отсканируйте QR-код.");
                }
            });
        }).start();
    }

    // ---------------------------------------------------------------- lifecycle

    @Override protected void onResume() {
        super.onResume();
        ui.post(poll);
    }

    @Override protected void onPause() {
        super.onPause();
        ui.removeCallbacks(poll);
    }

    // ---------------------------------------------------------------- helpers

    private TextView text(String value, float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(value);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        t.setTypeface(Typeface.create("sans-serif" + (bold ? "-medium" : ""), Typeface.NORMAL));
        return t;
    }

    private TextView pill(String label, boolean primary) {
        TextView t = text(label, 15, primary ? 0xFFFFFFFF : TEXT, true);
        t.setGravity(Gravity.CENTER);
        Drawable content = primary ? gradient(dp(27)) : rounded(0x0FFFFFFF, 0x24FFFFFF, dp(27));
        t.setBackground(ripple(content, dp(27)));
        t.setClickable(true);
        return t;
    }

    private GradientDrawable gradient(int radius) {
        GradientDrawable d = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[]{VIOLET, BLUE});
        d.setCornerRadius(radius);
        return d;
    }

    private GradientDrawable rounded(int color, int border, int radius) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(radius);
        if (border != 0) d.setStroke(dp(1), border);
        return d;
    }

    private RippleDrawable ripple(Drawable content, int radius) {
        return new RippleDrawable(ColorStateList.valueOf(0x33FFFFFF), content, rounded(0xFFFFFFFF, 0, radius));
    }

    private LinearLayout.LayoutParams marginTop(int top) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.topMargin = top;
        return lp;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
