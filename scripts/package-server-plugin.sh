#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
game_bin="${SPACE_ENGINEERS_BIN64:-}"
package_dir="${VOIDWRIGHT_SERVER_PLUGIN_PACKAGE_DIR:-$repo_dir/dist/VoidwrightServerPlugin}"
build_dir="$repo_dir/tools/PluginCompileCheck/bin/Release/net8.0"

if [[ -z "$game_bin" || ! -f "$game_bin/Sandbox.Game.dll" ]]; then
  echo "SPACE_ENGINEERS_BIN64 must point to the installed game SDK directory." >&2
  exit 64
fi
if [[ -e "$package_dir" || -L "$package_dir" ]]; then
  echo "refusing to replace an existing plugin package: $package_dir" >&2
  exit 73
fi

dotnet build "$repo_dir/tools/PluginCompileCheck/PluginCompileCheck.csproj" \
  --configuration Release \
  -p:SpaceEngineersBin64="$game_bin" \
  -p:LangVersion=7.3

mkdir -p "$package_dir"
# Magnetar's local plugin loader compiles source files from the staged folder;
# a DLL-only package is rejected with "No files were found in the directory
# specified." Keep the binary build as an API compile check, then stage the
# exact source set used by that check.
for source in "$repo_dir"/ServerPlugin/*.cs; do
  case "$(basename "$source")" in
    DrivingLabPassiveTelemetryPlugin.cs|PassiveTelemetryCommands.cs) continue ;;
  esac
  cp "$source" "$package_dir/"
done
for source in \
  DirectManeuver.cs \
  LocalVehicleTestControl.cs \
  LocalVehicleTestObstacleStopPolicy.cs \
  DirectManeuverCommandAcceptancePolicy.cs \
  DirectManeuverExecutor.cs \
  WheelPropulsionMapping.cs \
  DirectManeuverOperationLease.cs \
  MovementPreflight.cs \
  VoidwrightDiscoveryHandshake.cs \
  PreflightRuntimeEvidence.cs \
  DepthAvoidancePolicy.cs \
  DepthGridBindingPolicy.cs \
  ModOwnedCommandRoutingPolicy.cs \
  DrivingLabConsentWire.cs \
  VoidwrightOperationCapability.cs; do
  cp "$repo_dir/Data/Scripts/Voidwright/Core/$source" "$package_dir/"
done
cp "$repo_dir/ServerPlugin/Voidwright.ServerPlugin.xml" "$package_dir/Voidwright.ServerPlugin.xml"
cp "$repo_dir/ServerPlugin/vragecage.plugin.json" "$package_dir/vragecage.plugin.json"

echo "Stageable server-plugin package created at $package_dir"
