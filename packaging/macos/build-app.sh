#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"
runtime_id="${1:-osx-arm64}"
configuration="${2:-Release}"
version="${3:-0.1.0-preview}"
output_root="${4:-$repository_root/artifacts}"

case "$runtime_id" in
  osx-arm64|osx-x64) ;;
  *) echo "Unsupported macOS runtime: $runtime_id" >&2; exit 2 ;;
esac

mkdir -p "$output_root"
stage_root="$(mktemp -d "$output_root/v-router-macos-stage.XXXXXX")"
trap 'rm -rf -- "$stage_root"' EXIT
publish_dir="$stage_root/publish"
app_dir="$stage_root/V-Router.app"
archive_path="$output_root/V-Router-$version-$runtime_id-preview.zip"

dotnet publish "$repository_root/src/CncCad.App/CncCad.App.csproj" \
  --configuration "$configuration" \
  --runtime "$runtime_id" \
  --self-contained true \
  --output "$publish_dir"

mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
ditto "$publish_dir" "$app_dir/Contents/MacOS"
cp "$repository_root/packaging/macos/Info.plist" "$app_dir/Contents/Info.plist"

# Build the native Finder/Dock icon from the same source used by the Avalonia shell.
icon_source="$repository_root/src/CncCad.App/Assets/v-router-logo.png"
iconset="$stage_root/V-Router.iconset"
mkdir -p "$iconset"
sips -z 16 16 "$icon_source" --out "$iconset/icon_16x16.png" >/dev/null
sips -z 32 32 "$icon_source" --out "$iconset/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$icon_source" --out "$iconset/icon_32x32.png" >/dev/null
sips -z 64 64 "$icon_source" --out "$iconset/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$icon_source" --out "$iconset/icon_128x128.png" >/dev/null
sips -z 256 256 "$icon_source" --out "$iconset/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$icon_source" --out "$iconset/icon_256x256.png" >/dev/null
sips -z 512 512 "$icon_source" --out "$iconset/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$icon_source" --out "$iconset/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$icon_source" --out "$iconset/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$iconset" -o "$app_dir/Contents/Resources/V-Router.icns"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app_dir/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app_dir/Contents/Info.plist"
plutil -lint "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/V-Router"

# Preview builds are ad-hoc signed so Gatekeeper can verify bundle integrity.
codesign --force --deep --sign - "$app_dir" >"$stage_root/codesign.log" 2>&1 || { cat "$stage_root/codesign.log" >&2; exit 1; }
codesign --verify --deep --strict "$app_dir"
ditto -c -k --sequesterRsrc --keepParent "$app_dir" "$archive_path"

echo "$archive_path"
