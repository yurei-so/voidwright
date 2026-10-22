#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
game_bin="${SPACE_ENGINEERS_BIN64:-}"
magnetar_plugin_sdk="${MAGNETAR_PLUGIN_SDK:-}"
package_dir="$repo_dir/dist/VoidwrightPassiveTelemetryPlugin"
build_dir="$repo_dir/tools/PassiveTelemetryPluginCompileCheck/bin/Release/net8.0"

if [[ -z "$game_bin" || ! -f "$game_bin/Sandbox.Game.dll" ]]; then
  echo "SPACE_ENGINEERS_BIN64 must point to the installed game SDK directory." >&2
  exit 64
fi
if [[ -z "$magnetar_plugin_sdk" || ! -f "$magnetar_plugin_sdk" ]]; then
  echo "MAGNETAR_PLUGIN_SDK must point to the installed Magnetar PluginSdk.dll." >&2
  exit 64
fi
if [[ -e "$package_dir" || -L "$package_dir" ]]; then
  echo "refusing to replace an existing plugin package: $package_dir" >&2
  exit 73
fi

dotnet build "$repo_dir/tools/PassiveTelemetryPluginCompileCheck/PassiveTelemetryPluginCompileCheck.csproj" \
  --configuration Release \
  -p:SpaceEngineersBin64="$game_bin" \
  -p:MagnetarPluginSdk="$magnetar_plugin_sdk" \
  -p:LangVersion=7.3

mkdir -p "$package_dir"
cp "$build_dir/Voidwright.PassiveTelemetryPlugin.dll" "$package_dir/Voidwright.PassiveTelemetryPlugin.dll"
cp "$repo_dir/ServerPlugin/Voidwright.PassiveTelemetryPlugin.xml" "$package_dir/Voidwright.PassiveTelemetryPlugin.xml"
cp "$repo_dir/ServerPlugin/vragecage.passive-telemetry-plugin.json" "$package_dir/vragecage.passive-telemetry-plugin.json"

echo "Read-only passive telemetry plugin package created at $package_dir"
