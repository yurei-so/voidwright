#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
game_bin="${SPACE_ENGINEERS_BIN64:-}"
package_dir="$repo_dir/dist/Voidwright"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$repo_dir/.dotnet-home}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if [[ -z "$game_bin" ]]; then
  echo "SPACE_ENGINEERS_BIN64 must point to the game's Bin64 directory." >&2
  exit 64
fi
if [[ ! -f "$game_bin/Sandbox.Game.dll" ]]; then
  echo "Space Engineers assemblies were not found at: $game_bin" >&2
  exit 66
fi

dotnet build "$repo_dir/Voidwright.csproj" -p:SpaceEngineersBin64="$game_bin"
dotnet run --project "$repo_dir/tests/Voidwright.CoreTests/Voidwright.CoreTests.csproj"

mkdir -p "$package_dir/Data"
# These two files are server-side execution policy used by the Magnetar plugin
# and offline tests. The programmable-block runtime uses C# 6, so keep them in
# the source tree for plugin compilation but out of the in-game mod package.
rsync -a --delete \
  --exclude='Scripts/Voidwright/Core/DirectManeuverExecutor.cs' \
  --exclude='Scripts/Voidwright/Core/DirectManeuverOperationLease.cs' \
  "$repo_dir/Data/" "$package_dir/Data/"
cp "$repo_dir/modinfo.sbmi" "$package_dir/modinfo.sbmi"
cp "$repo_dir/vragecage.integration.json" "$package_dir/vragecage.integration.json"

# Magnetar compiles the packaged in-game scripts as C# 6. Keep the exact
# stageable source set inside that language boundary before publishing it.
dotnet build "$repo_dir/tools/ModScriptCompileCheck/ModScriptCompileCheck.csproj" \
  --configuration Release \
  -p:VoidwrightModPackage="$package_dir" \
  -p:SpaceEngineersBin64="$game_bin"

echo "Voidwright package refreshed at $package_dir"
