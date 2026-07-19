#!/usr/bin/env bash
# Build the .NET guest to a WasmKit-hostable CORE wasm module.
# Requires: WASI SDK 29 at ~/.wasi-sdk/wasi-sdk-29.0, emsdk 3.1.56 at ~/emsdk,
# .NET SDK 10. See ../dotnet-guest-toolchain.md.
set -e
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export WASI_SDK_PATH="$HOME/.wasi-sdk/wasi-sdk-29.0"
export EMSDK="$HOME/emsdk"
source "$HOME/emsdk/emsdk_env.sh" >/dev/null 2>&1 || true
echo "WASI_SDK_PATH=$WASI_SDK_PATH"
echo "EMSDK=$EMSDK"
cd "$here/dotaot"
dotnet publish -r wasi-wasm -c Release /p:DebugType=none 2>&1 | tail -30
w="bin/Release/net10.0/wasi-wasm/publish/dotaot.wasm"
echo "=== output ==="; ls -l "$w"
echo "=== inspect ==="; python3 "$here/inspect.py" "$here/dotaot/$w"
