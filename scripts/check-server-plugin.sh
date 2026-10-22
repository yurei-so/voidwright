#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
game_bin="${SPACE_ENGINEERS_BIN64:-}"
if [[ -z "$game_bin" || ! -f "$game_bin/Sandbox.Game.dll" ]]; then
  echo "SPACE_ENGINEERS_BIN64 must name an existing game Bin64/SDK directory." >&2
  exit 64
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
dotnet build "$repo_dir/tools/PluginCompileCheck/PluginCompileCheck.csproj" \
  --configuration Release -p:SpaceEngineersBin64="$game_bin"
