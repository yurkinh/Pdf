#!/usr/bin/env bash
# Builds Melville.StreamInterfaces (+ Melville.IntersectionTypes) from the external/Melville.Streams
# submodule into ./packages, the "local" source in nuget.config. Upstream does not publish these to nuget.org.
# Usage: ./build-local-packages.sh            (run from anywhere; needs `git submodule update --init`)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
STREAMS="$ROOT/external/Melville.Streams"
DOTNET="$ROOT/Src/.dotnet/dotnet"
[ -x "$DOTNET" ] || DOTNET=dotnet
IDS=(melville.streaminterfaces melville.intersectiontypes)

# Local fixes kept as patches so the submodule stays pinned to pristine upstream commits.
for patch in "$ROOT"/patches/melville-streams-*.patch; do
  [ -e "$patch" ] || continue
  if git -C "$STREAMS" apply --check "$patch" 2>/dev/null; then
    git -C "$STREAMS" apply "$patch" && echo "applied $(basename "$patch")"
  elif git -C "$STREAMS" apply --reverse --check "$patch" 2>/dev/null; then
    echo "already applied $(basename "$patch")"
  else
    echo "patch does not apply: $(basename "$patch")" >&2; exit 1
  fi
done

# Upstream's CopyPackage target (copy to the author's TempNuget folder) is skipped; patch 0002 adds the switch.
PROJ="$STREAMS/Src/Melville.StreamInterfaces/Melville.StreamInterfaces.csproj"
PROPS=(-c Release -p:MelvilleSkipCopyPackage=true)
# CS8785 = a source generator crashed; its members would silently be missing, so fail instead.
"$DOTNET" build "$PROJ" "${PROPS[@]}" -warnaserror:CS8785
"$DOTNET" pack "$PROJ" "${PROPS[@]}" --no-build

mkdir -p "$ROOT/packages"
rm -f "$ROOT"/packages/*.nupkg
find "$STREAMS/Src" -path '*/bin/Release/*.nupkg' -exec cp {} "$ROOT/packages/" \;

# Same version number on every rebuild, so drop cached copies or restore keeps the old bits.
CACHE="$("$DOTNET" nuget locals global-packages --list | sed 's/^global-packages: //')"
for id in "${IDS[@]}"; do rm -rf "${CACHE:?}/$id"; done
ls -1 "$ROOT/packages"
