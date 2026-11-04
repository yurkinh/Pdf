#!/usr/bin/env bash
# Installs the SDK pinned in global.json into ./.dotnet (no system-wide changes).
# Usage: ./install-dotnet.sh [workload ...]
set -euo pipefail
cd "$(dirname "$0")"
INSTALL_DIR=".dotnet"
VERSION=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' global.json | head -1)
INSTALL_SCRIPT="$(mktemp "${TMPDIR:-/tmp}/dotnet-install.XXXXXX")"
trap 'rm -f "$INSTALL_SCRIPT"' EXIT
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALL_SCRIPT"
bash "$INSTALL_SCRIPT" --version "$VERSION" --install-dir "$INSTALL_DIR"
# Libraries and tests also target net10.0, so the local root needs that runtime to run them.
bash "$INSTALL_SCRIPT" --runtime dotnet --channel 10.0 --install-dir "$INSTALL_DIR"
if [ $# -gt 0 ]; then "$INSTALL_DIR/dotnet" workload install "$@"; fi
echo "Done. SDK: $("$INSTALL_DIR/dotnet" --version)"
