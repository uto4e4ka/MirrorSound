package com.mirrorsound;

import android.annotation.SuppressLint;
import android.app.PendingIntent;
import android.content.Intent;
import android.graphics.drawable.Icon;
import android.os.Build;
import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;

/** "MirrorSound" button in the quick settings shade: starts or stops streaming. */
public class SoundTileService extends TileService {

    @Override
    public void onStartListening() {
        super.onStartListening();
        update();
    }

    @Override
    public void onClick() {
        super.onClick();
        if (StreamService.running) {
            stopService(new Intent(this, StreamService.class));
            update();
            return;
        }
        if (isLocked()) {
            unlockAndRun(this::launch);
        } else {
            launch();
        }
    }

    /** Android asks for permission to capture audio every time, so the app opens briefly to show that dialog. */
    @SuppressLint("StartActivityAndCollapseDeprecated")
    @SuppressWarnings("deprecation")
    private void launch() {
        Intent intent = new Intent(this, MainActivity.class)
                .setAction(MainActivity.ACTION_START)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (Build.VERSION.SDK_INT >= 34) {
            startActivityAndCollapse(PendingIntent.getActivity(this, 0, intent,
                    PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT));
        } else {
            startActivityAndCollapse(intent);
        }
    }

    private void update() {
        Tile tile = getQsTile();
        if (tile == null) return;
        boolean on = StreamService.running;
        String pc = getSharedPreferences("mirrorsound", MODE_PRIVATE).getString("name", "");
        boolean hasPc = !getSharedPreferences("mirrorsound", MODE_PRIVATE).getString("host", "").isEmpty();
        tile.setState(on ? Tile.STATE_ACTIVE : Tile.STATE_INACTIVE);
        tile.setLabel("MirrorSound");
        tile.setIcon(Icon.createWithResource(this, R.drawable.ic_waves));
        if (Build.VERSION.SDK_INT >= 29) {
            tile.setSubtitle(on ? "Звук передаётся"
                    : hasPc ? (pc.isEmpty() ? "На компьютер" : pc) : "В браузер");
        }
        tile.updateTile();
    }
}
