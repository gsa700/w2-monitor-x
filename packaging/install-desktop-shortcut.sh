#!/bin/sh
# Adds a "W2 Monitor" entry to your applications menu (and Desktop, if you have one).
# Run once from inside the extracted folder:  ./install-desktop-shortcut.sh
set -e

here="$(cd "$(dirname "$0")" && pwd)"
bin="$here/W2Monitor"
icon="$here/icon.png"

if [ ! -f "$bin" ]; then
  echo "W2Monitor was not found next to this script — keep it in the extracted folder." >&2
  exit 1
fi
chmod +x "$bin" 2>/dev/null || true

# Use the bundled icon if it shipped; otherwise fall back to a stock system icon.
if [ -f "$icon" ]; then icon_line="$icon"; else icon_line="network-transmit-receive"; fi

apps="$HOME/.local/share/applications"
mkdir -p "$apps"
entry="$apps/w2monitor.desktop"

cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=W2 Monitor
Comment=Elecraft W2 wattmeter monitor
Exec="$bin"
Icon=$icon_line
Terminal=false
Categories=Utility;HamRadio;
EOF
chmod +x "$entry"

# Also drop a copy on the Desktop, if there is one. GNOME/Nautilus needs it marked "trusted";
# some file managers (e.g. the Pi's PCManFM) still prompt once per launch — see the note below.
if [ -d "$HOME/Desktop" ]; then
  cp "$entry" "$HOME/Desktop/w2monitor.desktop"
  chmod +x "$HOME/Desktop/w2monitor.desktop" 2>/dev/null || true
  gio set "$HOME/Desktop/w2monitor.desktop" metadata::trusted true 2>/dev/null || true
fi

update-desktop-database "$apps" 2>/dev/null || true

echo "Installed 'W2 Monitor' to your applications menu."
echo
echo "Best: launch it from the Applications menu — that never prompts."
echo "If the DESKTOP icon asks 'Execute / Execute in Terminal / Open …', that's your file"
echo "manager's safety prompt for launcher files. Choose 'Execute', or silence it once:"
echo "  * PCManFM (Raspberry Pi):  Edit -> Preferences -> General ->"
echo "        tick 'Don't ask options on launch executable file'."
echo "  * GNOME Files:  right-click the icon -> 'Allow Launching'."
echo
echo "Serial access needs the 'dialout' group:"
echo "  sudo usermod -aG dialout \$USER   (then log out and back in)"
