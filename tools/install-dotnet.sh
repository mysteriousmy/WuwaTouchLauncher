#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
installer=$(mktemp)
trap 'rm -f "$installer"' EXIT
curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o "$installer"
bash "$installer" --version 10.0.402 --install-dir .tools/dotnet --no-path
