#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"
runtime_id="${1:-linux-x64}"
configuration="${2:-Release}"
output_dir="${3:-$repository_root/artifacts/linux-x64-preview}"

case "$runtime_id" in
  linux-x64) ;;
  *) echo "Unsupported Linux runtime: $runtime_id" >&2; exit 2 ;;
esac

mkdir -p "$output_dir/app" "$output_dir/share/applications" "$output_dir/share/icons/hicolor/512x512/apps"
dotnet publish "$repository_root/src/CncCad.App/CncCad.App.csproj" \
  --configuration "$configuration" \
  --runtime "$runtime_id" \
  --self-contained true \
  --output "$output_dir/app"
cp "$repository_root/packaging/linux/v-router.desktop" "$output_dir/share/applications/v-router.desktop"
cp "$repository_root/src/CncCad.App/Assets/v-router-logo.png" "$output_dir/share/icons/hicolor/512x512/apps/v-router.png"
cp "$repository_root/LICENSE" "$output_dir/LICENSE.txt"
cp "$repository_root/docs/THIRD_PARTY_NOTICES.md" "$output_dir/THIRD_PARTY_NOTICES.md"

echo "$output_dir"
