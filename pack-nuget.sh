#!/usr/bin/env bash
# Pack Plugin.Maui.Pdf and the libraries it depends on into artifacts/nuget. Nothing is published.
#
#   ./pack-nuget.sh                      # Release, versions from Src/Directory.Build.Props
#   MELVILLE_VERSION=0.6.9-maui.2 ./pack-nuget.sh
#
# The Melville.Pdf libraries are this fork's builds (net10.0 and net11.0; upstream's packages are net11.0 only),
# stamped with MELVILLE_VERSION so they never collide with upstream packages. Melville.StreamInterfaces comes from
# the external/Melville.Streams submodule via build-local-packages.sh.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/Src"
OUT="$ROOT/artifacts/nuget"
MELVILLE_VERSION="${MELVILLE_VERSION:-0.6.9-maui.1}"
DOTNET="$SRC/.dotnet/dotnet"; [ -x "$DOTNET" ] || DOTNET=dotnet
PYTHON="$(command -v python3 || command -v python)"   # Windows runners have "python"

"$ROOT/build-local-packages.sh"
rm -rf "$OUT"; mkdir -p "$OUT"
cp "$ROOT"/packages/*.nupkg "$OUT"/

# Plugin.Maui.Pdf and every project it references (not analyzers or source generators), dependencies first.
PROJECTS=()
while IFS= read -r line; do PROJECTS+=("$line"); done < <("$PYTHON" "$ROOT/tools/project-closure.py" "$SRC/Plugin.Maui.Pdf/Plugin.Maui.Pdf.csproj")

for project in "${PROJECTS[@]}"; do
  echo "== $(basename "$project")"
  "$DOTNET" pack "$project" -c Release -o "$OUT" \
    -p:MelvilleVersion="$MELVILLE_VERSION" -p:MelvilleSkipCopyPackage=true -p:ContinuousIntegrationBuild=true \
    -p:GeneratePackageOnBuild=false -nologo -v:quiet
done
echo
ls -1 "$OUT"
