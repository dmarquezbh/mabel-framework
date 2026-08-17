#!/usr/bin/env bash
# scripts/pack-xcframework.sh
#
# Gera o MabelHost.xcframework (device iOS + iOS Simulator) a partir do pacote
# SwiftPM puro em src/Mabel.Host.Ios, e empacota o resultado como um pacote
# NuGet de distribuição binária (feed privado RuiBarbot).
#
# Contexto: src/Mabel.Host.Ios é um SwiftPM package com um único target de
# biblioteca "automatic" (sem `type:` explícito no Package.swift). Quando esse
# tipo de alvo é arquivado via `xcodebuild archive` (SKIP_INSTALL=NO +
# BUILD_LIBRARY_FOR_DISTRIBUTION=YES), o Xcode NÃO produz um `.framework`
# pronto — produz um objeto relocável único (`MabelHost.o`, resultado de um
# link `-r`/merged-object) mais o `.swiftmodule` (um diretório com um slice
# por arquitetura-alvo) e o header gerado de compatibilidade ObjC
# (`MabelHost-Swift.h`) em locais separados do DerivedData. Isso é o
# comportamento documentado/conhecido de arquivar pacotes SwiftPM sem um
# target de Framework dedicado — não é falha do build. Este script monta o
# `.framework` manualmente a partir dessas três peças (binário + swiftmodule +
# header) antes de chamar `xcodebuild -create-xcframework`.
#
# Uso:
#   scripts/pack-xcframework.sh [VERSION]
#
# VERSION (opcional, default 1.0.0) é usada só no .nuspec/.nupkg final — o
# xcframework em si não é versionado internamente.
set -euo pipefail

VERSION="${1:-1.0.0}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="$REPO_ROOT/src/Mabel.Host.Ios"
BUILD_DIR="$REPO_ROOT/build"
SCHEME="MabelHost"
CONFIGURATION="Release"
PACKAGE_ID="Pjus.RuiBarbot.MabelHost.Ios"

echo "==> Limpando diretório de build anterior ($BUILD_DIR)"
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/archives" "$BUILD_DIR/frameworks" "$BUILD_DIR/xcframework" "$BUILD_DIR/nuget"

archive_and_assemble() {
    local platform_dest="$1"      # ex.: "generic/platform=iOS"
    local platform_dir_name="$2"  # ex.: "iOS" | "iOSSimulator"
    local sdk_dir_name="$3"       # ex.: "iphoneos" | "iphonesimulator" — usado
                                   # para achar o BuildProductsPath certo
    local supported_platform="$4" # CFBundleSupportedPlatforms — "iPhoneOS" | "iPhoneSimulator"
    local min_os="$5"

    local archive_path="$BUILD_DIR/archives/MabelHost-${platform_dir_name}.xcarchive"
    local derived_data="$BUILD_DIR/derived-${platform_dir_name}"

    echo "==> xcodebuild archive — $platform_dest" >&2
    (
        cd "$PKG_DIR"
        xcodebuild archive \
            -scheme "$SCHEME" \
            -configuration "$CONFIGURATION" \
            -destination "$platform_dest" \
            -archivePath "$archive_path" \
            -derivedDataPath "$derived_data" \
            SKIP_INSTALL=NO \
            BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
            CODE_SIGNING_ALLOWED=NO \
            CODE_SIGNING_REQUIRED=NO >&2
    )

    local build_products="$derived_data/Build/Intermediates.noindex/ArchiveIntermediates/$SCHEME/BuildProductsPath/${CONFIGURATION}-${sdk_dir_name}"
    local intermediates="$derived_data/Build/Intermediates.noindex/ArchiveIntermediates/$SCHEME/IntermediateBuildFilesPath/${SCHEME}.build/${CONFIGURATION}-${sdk_dir_name}/${SCHEME}.build/Objects-normal/arm64"

    # O `.o` em BuildProductsPath é um SYMLINK RELATIVO para
    # InstallationBuildProductsLocation/Users/<usuário>/Objects/${SCHEME}.o —
    # e, pelo menos neste ambiente/versão de Xcode, esse symlink sai quebrado
    # (aponta certo em teoria, mas `[ -f ]` nele falha). O artefato real e
    # íntegro é a cópia que `xcodebuild archive` deposita dentro do próprio
    # .xcarchive em Products/Users/<usuário>/Objects/${SCHEME}.o — usamos essa
    # via `find` (evita hardcodar o usuário).
    local binary_o
    binary_o=$(find "$archive_path/Products" -type f -name "${SCHEME}.o" | head -1)
    local swiftmodule_dir="$build_products/${SCHEME}.swiftmodule"
    local generated_header="$intermediates/${SCHEME}-Swift.h"

    [ -f "$binary_o" ] || { echo "ERRO: binário não encontrado em $binary_o" >&2; exit 1; }
    [ -d "$swiftmodule_dir" ] || { echo "ERRO: swiftmodule não encontrado em $swiftmodule_dir" >&2; exit 1; }
    [ -f "$generated_header" ] || { echo "ERRO: header gerado não encontrado em $generated_header" >&2; exit 1; }

    echo "==> Montando ${SCHEME}.framework ($platform_dir_name)" >&2
    local fw_dir="$BUILD_DIR/frameworks/$platform_dir_name/${SCHEME}.framework"
    mkdir -p "$fw_dir/Headers" "$fw_dir/Modules/${SCHEME}.swiftmodule"

    cp -L "$binary_o" "$fw_dir/${SCHEME}"
    cp -L "$generated_header" "$fw_dir/Headers/${SCHEME}-Swift.h"
    cp -R "$swiftmodule_dir/." "$fw_dir/Modules/${SCHEME}.swiftmodule/"

    cat > "$fw_dir/Modules/module.modulemap" <<EOF
framework module ${SCHEME} {
    header "${SCHEME}-Swift.h"
    export *
    requires objc
}
EOF

    cat > "$fw_dir/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>${SCHEME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.mabel.host.ios.${SCHEME}</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${SCHEME}</string>
    <key>CFBundlePackageType</key>
    <string>FMWK</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleSupportedPlatforms</key>
    <array>
        <string>${supported_platform}</string>
    </array>
    <key>MinimumOSVersion</key>
    <string>${min_os}</string>
</dict>
</plist>
EOF

    echo "$fw_dir"
}

DEVICE_FRAMEWORK=$(archive_and_assemble "generic/platform=iOS" "iOS" "iphoneos" "iPhoneOS" "15.0")
SIMULATOR_FRAMEWORK=$(archive_and_assemble "generic/platform=iOS Simulator" "iOSSimulator" "iphonesimulator" "iPhoneSimulator" "15.0")

echo "==> xcodebuild -create-xcframework"
rm -rf "$BUILD_DIR/xcframework/MabelHost.xcframework"
xcodebuild -create-xcframework \
    -framework "$DEVICE_FRAMEWORK" \
    -framework "$SIMULATOR_FRAMEWORK" \
    -output "$BUILD_DIR/xcframework/MabelHost.xcframework"

echo "==> Validando slices do xcframework"
ls "$BUILD_DIR/xcframework/MabelHost.xcframework"

echo "==> Empacotando NuGet ($PACKAGE_ID $VERSION)"
# Sem `nuget.exe`/mono nuget disponível neste ambiente — empacota via
# `dotnet pack` sobre um .csproj "vazio" (scripts/nuget/) que só existe pra
# processar o .nuspec já resolvido (NuspecFile). Zip + nuspec concreto ficam
# juntos no stage; NuspecBasePath="." resolve o <file src="..."> relativo.
NUPKG_STAGE="$BUILD_DIR/nuget/stage"
rm -rf "$NUPKG_STAGE"
mkdir -p "$NUPKG_STAGE"
XCFRAMEWORK_ZIP="$NUPKG_STAGE/MabelHost.xcframework.zip"
( cd "$BUILD_DIR/xcframework" && zip -r -q "$XCFRAMEWORK_ZIP" "MabelHost.xcframework" )

sed \
    -e "s/{{VERSION}}/$VERSION/g" \
    "$REPO_ROOT/scripts/MabelHost.Ios.nuspec.template" > "$NUPKG_STAGE/MabelHost.Ios.nuspec"

dotnet pack "$REPO_ROOT/scripts/nuget/Pjus.RuiBarbot.MabelHost.Ios.csproj" \
    -p:NuspecFile="$NUPKG_STAGE/MabelHost.Ios.nuspec" \
    -p:NuspecBasePath="$NUPKG_STAGE" \
    -o "$BUILD_DIR/nuget"

echo ""
echo "==> Concluído."
echo "xcframework: $BUILD_DIR/xcframework/MabelHost.xcframework"
echo "nupkg:       $BUILD_DIR/nuget/${PACKAGE_ID}.${VERSION}.nupkg"
