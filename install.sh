#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source_dir="$project_dir/dist"
if [[ ! -f "$source_dir/wuwa-touch-launcher" ]]; then source_dir="$project_dir"; fi
install_dir="$HOME/.local/lib/wuwa-touch-launcher"
config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/wuwa-touch-launcher"
applications_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$install_dir" "$config_dir" "$HOME/.local/bin" "$applications_dir"
install -m 755 "$source_dir/wuwa-touch-launcher" "$install_dir/wuwa-touch-launcher.new"
mv -f "$install_dir/wuwa-touch-launcher.new" "$install_dir/wuwa-touch-launcher"
# Version 0.5 embeds the helper. Preserve an old adjacent copy as a rollback file.
if [[ -f "$install_dir/touch-bridge.exe" ]]; then
  mv "$install_dir/touch-bridge.exe" "$install_dir/touch-bridge.exe.previous.$(date +%s)"
fi
if [[ ! -e "$config_dir/profiles.json" ]]; then
  "$install_dir/wuwa-touch-launcher" save
fi
ln -sfn "$install_dir/wuwa-touch-launcher" "$HOME/.local/bin/wuwa-touch-launcher"
cat > "$applications_dir/wuwa-touch-launcher.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=鸣潮启动器
Name[en]=Wuthering Waves Launcher
Comment=配置普通或触控模式，一键启动鸣潮
Icon=wuwa-touch-launcher
Exec="$install_dir/wuwa-touch-launcher" gui
Terminal=false
Categories=Game;
Actions=Normal;Touch;Stop;

[Desktop Action Normal]
Name=直接启动普通模式
Exec="$install_dir/wuwa-touch-launcher" launch --mode normal --notify

[Desktop Action Touch]
Name=直接启动触控模式
Exec="$install_dir/wuwa-touch-launcher" launch --mode touch --notify

[Desktop Action Stop]
Name=关闭鼠标过滤（游戏继续运行）
Exec="$install_dir/wuwa-touch-launcher" stop --notify
EOF
icon_dir="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/scalable/apps"
mkdir -p "$icon_dir"
install -m 644 "$project_dir/assets/wuwa-touch-launcher.svg" "$icon_dir/wuwa-touch-launcher.svg"
"$install_dir/wuwa-touch-launcher" shortcuts
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$applications_dir"
fi
echo "已安装。应用菜单搜索“鸣潮启动器”打开配置，或使用“鸣潮 · 普通模式 / 触控模式”直接启动。"
