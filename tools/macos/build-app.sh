#!/bin/bash
# TakeTaiko の macOS 版（TakeTaiko.app）を作る
#   使い方（リポジトリのいちばん上のフォルダで）：
#     ./tools/macos/build-app.sh            … この Mac に合わせる（Intel なら x64、Apple シリコンなら arm64）
#     ./tools/macos/build-app.sh arm64      … Apple シリコン用
#     ./tools/macos/build-app.sh x64        … Intel 用
#     AOT=1 ./tools/macos/build-app.sh      … Native AOT（逆コンパイル対策）でビルドする（Xcode のコマンドラインツールが必要）
#   できあがり：work/macos/TakeTaiko.app
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$ROOT/TakeTaikoSimulator/TakeTaikoSimulator.csproj"
OUT="$ROOT/work/macos"
APP="$OUT/TakeTaiko.app"

ARCH="${1:-}"
if [ -z "$ARCH" ]; then
  case "$(uname -m)" in
    arm64) ARCH=arm64 ;;
    *) ARCH=x64 ;;
  esac
fi
case "$ARCH" in
  arm64|x64) ;;
  *) echo "arm64 か x64 を指定してください（今：$ARCH）"; exit 1 ;;
esac
RID="osx-$ARCH"

# ----- BASS の Mac 版があるか -----
for lib in libbass.dylib libbassmix.dylib; do
  if [ ! -f "$ROOT/TakeTaikoSimulator/$lib" ]; then
    echo "TakeTaikoSimulator/$lib がありません。"
    echo "un4seen.com から bass24-osx.zip と bassmix24-osx.zip をダウンロードして、中の $lib を TakeTaikoSimulator フォルダに置いてください。"
    exit 1
  fi
done

# ----- ビルド -----
PUBLISH="$OUT/publish-$RID"
rm -rf "$PUBLISH"
AOT_FLAG="-p:PublishAot=false"
if [ "${AOT:-0}" = "1" ]; then AOT_FLAG="-p:PublishAot=true"; fi
echo "▶ ビルド中（$RID、${AOT_FLAG#-p:}）…"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true $AOT_FLAG -o "$PUBLISH"

# ----- TakeTaiko.app の形にまとめる -----
echo "▶ TakeTaiko.app を作っています…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/TakeTaiko"

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROJECT" | head -n 1)"
VERSION="${VERSION:-1.0.0}"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>TakeTaiko</string>
  <key>CFBundleDisplayName</key><string>TakeTaiko</string>
  <key>CFBundleIdentifier</key><string>com.takerai.taketaiko</string>
  <key>CFBundleExecutable</key><string>TakeTaiko</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.music-games</string>
</dict>
</plist>
PLIST

# アイコン（AppIcon.png → AppIcon.icns。sips と iconutil は macOS に最初から入っている）
ICON_PNG="$ROOT/TakeTaikoSimulator/Assets/Branding/AppIcon.png"
if [ -f "$ICON_PNG" ]; then
  ICONSET="$OUT/AppIcon.iconset"
  rm -rf "$ICONSET"; mkdir -p "$ICONSET"
  for size in 16 32 128 256; do
    sips -z $size $size "$ICON_PNG" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    if [ $double -le 256 ]; then
      sips -z $double $double "$ICON_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
    fi
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns" || echo "（アイコンを作れませんでした。アイコンなしで続けます）"
  rm -rf "$ICONSET"
fi

# ----- 自分用の署名（アドホック署名）。これが無いと Apple シリコンの Mac では起動できない -----
echo "▶ 署名しています（アドホック）…"
codesign --force --deep --sign - "$APP"

echo ""
echo "✅ できました：$APP"
echo "   データ（曲・設定・スキン）は 書類/TakeTaiko に作られます。曲は 書類/TakeTaiko/Songs に入れてください。"
