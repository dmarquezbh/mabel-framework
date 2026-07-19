# .NET guest toolchain: NativeAOT-LLVM → WasmKit-hostable core module

How to compile a .NET assembly to a **core WebAssembly module** (WASM binary version
`01 00 00 00`) that the pure-Swift [WasmKit](https://github.com/swiftwasm/WasmKit)
interpreter can parse, instantiate and run on-device (iOS via xtool, no JIT).

## Why this exists

The default `dotnet publish -r wasi-wasm` path (Mono + `wasi-experimental` workload)
emits a **WASIp2 component** — binary version `0d 00 01 00`, ~3.34 MB (per spike #17).
WasmKit is a core-wasm + WASI-preview1 host; it rejects components outright:

```
unknown binary version 0d 00 01 00
```

**NativeAOT-LLVM** (dotnet/runtimelab `feature/NativeAOT-LLVM`) compiles IL straight to
LLVM and links a real native module — no embedded Mono runtime. With the flags below it
produces a **core module of ~810 KB** that WasmKit parses, instantiates and executes.

## Prerequisites (one-time, all under `$HOME` — never `/tmp` nor `C:`)

Two SDKs, both pinned to what the rc compiler is tested against
(`eng/pipelines/runtimelab/install-{wasi-sdk,emscripten}.ps1`):

### WASI SDK 29 (required for the `wasi-wasm` target)

```bash
curl -fL -o ~/dl/wasi-sdk-29.tar.gz \
  https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-29/wasi-sdk-29.0-x86_64-linux.tar.gz
mkdir -p ~/.wasi-sdk/wasi-sdk-29.0
tar -xf ~/dl/wasi-sdk-29.tar.gz -C ~/.wasi-sdk/wasi-sdk-29.0 --strip-components=1
export WASI_SDK_PATH="$HOME/.wasi-sdk/wasi-sdk-29.0"   # must contain share/wasi-sysroot
```

### Emscripten 3.1.56 (a declared prerequisite; the link uses `$EMSDK`)

The `git clone` of emsdk was unreliable over this network (`fetch-pack: invalid
index-pack output`). The release **tarball** is a single reliable HTTP file:

```bash
curl -fL -o ~/dl/emsdk-3.1.56.tar.gz \
  https://github.com/emscripten-core/emsdk/archive/refs/tags/3.1.56.tar.gz
mkdir -p ~/emsdk && tar -xf ~/dl/emsdk-3.1.56.tar.gz -C ~/emsdk --strip-components=1
cd ~/emsdk && ./emsdk install 3.1.56 && ./emsdk activate 3.1.56   # ~1 GB toolchain, from googleapis
source ~/emsdk/emsdk_env.sh && export EMSDK="$HOME/emsdk"
```

## The NuGet feed and package

NativeAOT-LLVM ships from the **dotnet-experimental** Azure feed. `nuget.config`:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="dotnet-experimental" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json" />
    <add key="dotnet10"            value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json" />
    <add key="nuget"               value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Two packages, **same version** (latest .NET 10 line at time of writing:
`10.0.0-rc.1.26357.1`). The second one is RID-specific via
`$(NETCoreSdkPortableRuntimeIdentifier)` (here `linux-x64`):

```xml
<PackageReference Include="Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
<PackageReference Include="runtime.$(NETCoreSdkPortableRuntimeIdentifier).Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
```

## The project — the flags that matter

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PublishTrimmed>true</PublishTrimmed>
    <SelfContained>true</SelfContained>
    <InvariantGlobalization>true</InvariantGlobalization>

    <!-- Stops the wasi-wasm RID from pulling the Mono wasm workload (which errors NETSDK1203). -->
    <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>

    <!-- CRITICAL: default for wasi is wasm32-unknown-wasip2, which yields a COMPONENT.
         Override to wasip1 to get a plain CORE module. -->
    <IlcLlvmTarget>wasm32-unknown-wasip1</IlcLlvmTarget>

    <!-- lld => links with plain wasm-ld (core module) AND skips the framework .wit
         "--component-type" linker args (Native.targets gates them on LinkerFlavor != lld). -->
    <LinkerFlavor>lld</LinkerFlavor>

    <!-- Reactor exec-model: exports UnmanagedCallersOnly functions, no _start required. -->
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
    <PackageReference Include="runtime.$(NETCoreSdkPortableRuntimeIdentifier).Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
  </ItemGroup>
</Project>
```

Exported function:

```csharp
using System.Runtime.InteropServices;
public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "add")]
    public static int Add(int a, int b) => a + b;
}
```

### Where each flag came from (source of truth)

`Microsoft.DotNet.ILCompiler.LLVM/<ver>/build/`:

- `Microsoft.NETCore.Native.targets:131` — `IlcLlvmTarget` defaults to
  `wasm32-unknown-wasip2` for wasi. It feeds ILC codegen (`--codegenopt:Target=`, line 345),
  the compile (`-target`, line 422) and the link (`-target`, line 563). Overriding it to
  `wasm32-unknown-wasip1` is what flips component → core.
- `Microsoft.NETCore.Native.Wasm.targets:71-73` — unconditionally adds the framework's
  `*.wit` as `WasmComponentTypeWit`.
- `Microsoft.NETCore.Native.targets:594` — turns those into `-Wl,--component-type,…`
  **only when `LinkerFlavor != 'lld'`**. So `LinkerFlavor=lld` both selects `wasm-ld`
  and drops the component-type args.
- `Microsoft.NETCore.Native.targets:603` — `-mexec-model=reactor` when `NativeLib=Shared`.

## Build

```bash
export WASI_SDK_PATH="$HOME/.wasi-sdk/wasi-sdk-29.0"
source "$HOME/emsdk/emsdk_env.sh"; export EMSDK="$HOME/emsdk"
dotnet publish -r wasi-wasm -c Release /p:DebugType=none
# => bin/Release/net10.0/wasi-wasm/publish/<name>.wasm
```

## Verification (proof)

Binary header + shape (`inspect.py`):

```
size 827810
magic 00 61 73 6d   version 01 00 00 00      <-- CORE (was 0d 00 01 00 = component)
KIND=CORE
EXPORTS=['memory(mem)', '_initialize(func)', 'add(func)', 'cabi_realloc(func)']
```

WasmKit (0.2.2) parse → instantiate → call, on Linux (`runner/`):

```
KIND=CORE-MODULE
PARSE=OK exports=["memory", "_initialize", "add", "cabi_realloc"]
STUBBED_IMPORTS=32
INSTANTIATE=OK
INIT=_initialize ran
add(2,3) raw=[WasmTypes.Value.i32(5)]
RESULT=OK add(2,3)=5
WASMKIT-RUNS-DOTNET-CORE-WASM=TRUE
```

| Metric | Mono component (spike #17) | NativeAOT-LLVM core (this) |
|---|---|---|
| Binary version | `0d 00 01 00` (component) | `01 00 00 00` (core) |
| WasmKit parse | rejected | **OK** |
| WasmKit run export | n/a | **add(2,3)=5** |
| Size | ~3.34 MB | **~810 KB** |

## Known limitation — WASIp2 imports (read before using for real I/O)

The core module still **imports WASIp2 component interfaces**, not
`wasi_snapshot_preview1`:

```
wasi:cli/environment@0.2.0, wasi:cli/exit@0.2.0, wasi:cli/std{in,out,err}@0.2.0,
wasi:clocks/*@0.2.0, wasi:filesystem/*@0.2.0, wasi:io/{error,poll,streams}@0.2.0,
wasi:random/random@0.2.0   (32 function imports)
```

`IlcLlvmTarget=wasip1` changes **our** module's format and linkage, but the **prebuilt
.NET framework native libs** (`libSystem.Native`, etc.) are compiled against WASIp2, so
their syscalls import the `wasi:*@0.2.0` interfaces. WasmKit's WASI-preview1 host
(`WASIBridgeToHost`) does not provide these, so instantiation against real WASI fails:

```
INSTANTIATE=FAIL unknown import wasi:io/error@0.2.0.[resource-drop]error
```

A preview1 core module would need a **preview1 build of the .NET framework libs**, which
the rc toolchain does not ship. That is the exact obstacle; it is not worked around by a
gambiarra.

**Why this is still the right path for Mabel.** A pure exported function never calls those
imports, so the host satisfies them with matching-typed no-op stubs (see `runner/`) and the
export runs. Mabel's guest is a **capability-import** guest — it renders a semantic
descriptor by calling *host-provided* functions, not by doing WASI file/stdio I/O. That is
precisely this model: the host supplies the imports (Mabel capabilities instead of stubs),
and pure/compute + capability-call code executes under WasmKit on-device. The remaining
work is to give the .NET guest a capability ABI that imports *Mabel* functions rather than
`wasi:*`, e.g. via `[DllImport("mabel")]` + `[WasmImportLinkage]` (see runtimelab
`compiling.md` → "WebAssembly module imports").

## Reproduce

Working sample committed under [`docs/dotnet-aot-wasm/`](./dotnet-aot-wasm/):
`dotaot/` (the .NET project), `runner/` (WasmKit host + stubs), `inspect.py` (header
parser). Spike scratch copy lived at
`~/apps/rui-native/wasm-spike/dotnet-aot/` on the Ubuntu-26.04 WSL box.

Toolchain versions proven: .NET SDK 10.0.300, ILCompiler.LLVM 10.0.0-rc.1.26357.1,
WASI SDK 29.0, emsdk 3.1.56, Swift 6.1, WasmKit 0.2.2.
