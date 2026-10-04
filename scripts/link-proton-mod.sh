#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_dir="$repo_dir/dist/Voidwright"
mods_dir="${SPACE_ENGINEERS_MODS_DIR:-}"
link_path="$mods_dir/Voidwright"

if [[ -z "$mods_dir" ]]; then
  echo "SPACE_ENGINEERS_MODS_DIR must point to the Proton local-mod directory." >&2
  exit 64
fi

if [[ ! -d "$package_dir/Data/Scripts/Voidwright" ]]; then
  echo "Build the mod before linking it: ./scripts/build-mod.sh" >&2
  exit 1
fi

mkdir -p "$mods_dir"
if [[ -L "$link_path" ]]; then
  current_target="$(readlink -f "$link_path")"
  expected_target="$(readlink -f "$package_dir")"
  if [[ "$current_target" == "$expected_target" ]]; then
    echo "Voidwright development link already installed: $link_path"
    exit 0
  fi
  echo "Refusing to replace a symlink with a different target: $link_path -> $current_target" >&2
  exit 1
fi
if [[ -e "$link_path" ]]; then
  echo "Refusing to replace existing mod content: $link_path" >&2
  exit 1
fi

ln -s "$package_dir" "$link_path"
echo "Installed Voidwright development link: $link_path -> $package_dir"
