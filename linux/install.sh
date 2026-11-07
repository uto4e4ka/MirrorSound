#!/bin/sh
# Installs MirrorSound for the current user (no root needed): ~/.local/share/mirrorsound,
# an app-menu entry, the icon and a `mirrorsound` command in ~/.local/bin.
set -e

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/mirrorsound"
APPS="$HOME/.local/share/applications"
ICONS="$HOME/.local/share/icons/hicolor/256x256/apps"
AUTOSTART="${XDG_CONFIG_HOME:-$HOME/.config}/autostart/mirrorsound.desktop"

# Stop a running copy (an upgrade) and give it a moment to exit.
if pkill -x MirrorSound 2>/dev/null; then
    for i in 1 2 3 4 5 6 7 8 9 10; do pgrep -x MirrorSound >/dev/null || break; sleep 0.3; done
fi

mkdir -p "$DEST" "$APPS" "$ICONS" "$HOME/.local/bin"
# Copy beside and rename, so replacing a binary that is still running works too.
cp "$SRC/MirrorSound" "$DEST/MirrorSound.new"
chmod 755 "$DEST/MirrorSound.new"
mv -f "$DEST/MirrorSound.new" "$DEST/MirrorSound"
cp "$SRC/uninstall.sh" "$DEST/uninstall.sh"
chmod 755 "$DEST/uninstall.sh"
cp "$SRC/mirrorsound.png" "$ICONS/mirrorsound.png"
ln -sf "$DEST/MirrorSound" "$HOME/.local/bin/mirrorsound"

cat > "$APPS/mirrorsound.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=MirrorSound
Comment=Звук телефона на компьютере
Exec="$DEST/MirrorSound"
Icon=mirrorsound
Terminal=false
Categories=AudioVideo;Audio;
StartupWMClass=MirrorSound
EOF

# An autostart entry from an older copy should start this one.
if [ -f "$AUTOSTART" ]; then
    sed -i "s|^Exec=.*|Exec=\"$DEST/MirrorSound\" --minimized|" "$AUTOSTART"
fi

update-desktop-database "$APPS" >/dev/null 2>&1 || true
gtk-update-icon-cache -q "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true

echo "MirrorSound установлен: ищите его в меню приложений."
echo
echo "Если включён брандмауэр, откройте порты для телефона:"
echo "  ufw:       sudo ufw allow 47800,47802/tcp && sudo ufw allow 47801/udp"
echo "  firewalld: sudo firewall-cmd --permanent --add-port=47800/tcp --add-port=47802/tcp --add-port=47801/udp && sudo firewall-cmd --reload"
echo
echo "Удалить: $DEST/uninstall.sh"

if [ -n "$DISPLAY$WAYLAND_DISPLAY" ]; then
    nohup "$DEST/MirrorSound" >/dev/null 2>&1 &
fi
