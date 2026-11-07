#!/bin/sh
# Removes MirrorSound installed by install.sh, with its autostart entry and settings.
pkill -x MirrorSound 2>/dev/null || true
CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}"
rm -f "$HOME/.local/bin/mirrorsound" \
      "$HOME/.local/share/applications/mirrorsound.desktop" \
      "$HOME/.local/share/icons/hicolor/256x256/apps/mirrorsound.png" \
      "$CONFIG/autostart/mirrorsound.desktop"
rm -rf "$HOME/.local/share/mirrorsound" "$CONFIG/MirrorSound"
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
echo "MirrorSound удалён."
