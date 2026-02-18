#!/bin/bash
# ==============================================================================
# MABEL FRAMEWORK - SETUP
# Instala todas as dependencias necessarias para o Mabel Framework
# Uso: ./setup.sh            (instalacao)
#      ./setup.sh --uninstall (remover tudo)
# Testado em: Ubuntu 22.04+ / 24.04+ / WSL2
# ==============================================================================

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# ------------------------------------------------------------------------------
# Helpers
# ------------------------------------------------------------------------------

log_step() { echo -e "\n${BLUE}[$1/$TOTAL_STEPS]${NC} $2"; }
log_ok()   { echo -e "  ${GREEN}OK${NC} $1"; }
log_skip() { echo -e "  ${YELLOW}SKIP${NC} $1 (ja instalado)"; }
log_warn() { echo -e "  ${YELLOW}WARN${NC} $1"; }
log_err()  { echo -e "  ${RED}ERRO${NC} $1"; }

check_command() { command -v "$1" &>/dev/null; }

# Check if we can use sudo (may not be available in containers/CI)
can_sudo() {
    if ! check_command sudo; then
        return 1
    fi
    # Test if sudo works without password (or has cached credentials)
    sudo -n true 2>/dev/null
}

TOTAL_STEPS=8
BASHRC="$HOME/.bashrc"
MARKER="# >>> mabel-framework >>>"
MARKER_END="# <<< mabel-framework <<<"

# Ensure ~/bin exists early (used by xtool and mabel CLI)
mkdir -p "$HOME/bin"

# ------------------------------------------------------------------------------
# Uninstall
# ------------------------------------------------------------------------------

if [ "${1:-}" = "--uninstall" ]; then

    echo -e "${MAGENTA}"
    echo "  __  __   _   ___  _____ _      "
    echo " |  \/  | /_\ | _ )| ____| |     "
    echo " | |\/| |/ _ \| _ \|  _| | |     "
    echo " |_|  |_/_/ \_\___/|___|_|____   "
    echo ""
    echo " Setup - Desinstalador"
    echo -e "${NC}"

    TOTAL_STEPS=6

    # --- Step 1: Remover symlink do Mabel CLI ---
    log_step 1 "Removendo Mabel CLI..."
    if [ -L "$HOME/bin/mabel" ]; then
        rm -f "$HOME/bin/mabel"
        log_ok "Symlink ~/bin/mabel removido."
    else
        log_skip "~/bin/mabel nao encontrado"
    fi

    # --- Step 2: Remover .NET SDK ---
    log_step 2 "Removendo .NET SDK..."
    if [ -d "$HOME/.dotnet" ]; then
        read -r -p "  Remover ~/.dotnet? (s/N) " confirm
        if [[ "$confirm" =~ ^[sS]$ ]]; then
            rm -rf "$HOME/.dotnet"
            log_ok "~/.dotnet removido."
        else
            log_skip "Mantido por escolha do usuario"
        fi
    else
        log_skip "~/.dotnet nao encontrado"
    fi

    # --- Step 3: Remover Swift ---
    log_step 3 "Removendo Swift toolchain..."
    if [ -d "$HOME/swift" ]; then
        read -r -p "  Remover ~/swift? (s/N) " confirm
        if [[ "$confirm" =~ ^[sS]$ ]]; then
            rm -rf "$HOME/swift"
            log_ok "~/swift removido."
        else
            log_skip "Mantido por escolha do usuario"
        fi
    else
        log_skip "~/swift nao encontrado"
    fi

    # --- Step 4: Remover xtool ---
    log_step 4 "Removendo xtool..."
    XTOOL_REMOVED=false
    if [ -f "$HOME/bin/xtool" ]; then
        rm -f "$HOME/bin/xtool"
        log_ok "xtool removido de ~/bin/"
        XTOOL_REMOVED=true
    fi
    if [ -f "/usr/local/bin/xtool" ]; then
        if can_sudo; then
            sudo rm -f /usr/local/bin/xtool
            log_ok "xtool removido de /usr/local/bin/"
        else
            log_warn "xtool em /usr/local/bin/ — requer sudo para remover"
        fi
        XTOOL_REMOVED=true
    fi
    if [ "$XTOOL_REMOVED" = false ]; then
        log_skip "xtool nao encontrado"
    fi

    # --- Step 5: Remover libs auxiliares ---
    log_step 5 "Removendo libs auxiliares..."
    if [ -d "$HOME/lib" ]; then
        rm -rf "$HOME/lib"
        log_ok "~/lib removido."
    else
        log_skip "~/lib nao encontrado"
    fi

    # --- Step 6: Limpar PATH do .bashrc ---
    log_step 6 "Removendo configuracao do PATH do .bashrc..."
    if grep -q "$MARKER" "$BASHRC" 2>/dev/null; then
        sed -i "/$MARKER/,/$MARKER_END/d" "$BASHRC"
        log_ok "Bloco mabel-framework removido do .bashrc."
    else
        log_skip "Nenhuma configuracao encontrada no .bashrc"
    fi

    echo ""
    echo -e "${MAGENTA}============================================${NC}"
    echo -e "${GREEN} Desinstalacao concluida!${NC}"
    echo -e "${MAGENTA}============================================${NC}"
    echo ""
    echo -e "  ${YELLOW}Nota:${NC} Pacotes do sistema (build-essential, usbmuxd, etc.)"
    echo -e "  nao foram removidos para evitar quebrar outros programas."
    echo -e "  Recarregue o shell: ${BLUE}source ~/.bashrc${NC}"
    echo ""
    exit 0
fi

# ------------------------------------------------------------------------------
# Banner
# ------------------------------------------------------------------------------

echo -e "${MAGENTA}"
echo "  __  __   _   ___  _____ _      "
echo " |  \/  | /_\ | _ )| ____| |     "
echo " | |\/| |/ _ \| _ \|  _| | |     "
echo " |_|  |_/_/ \_\___/|___|_|____   "
echo ""
echo " Setup - Instalador de Dependencias"
echo -e "${NC}"

# ------------------------------------------------------------------------------
# Step 1: Pacotes do sistema (apt)
# ------------------------------------------------------------------------------

log_step 1 "Instalando pacotes do sistema via apt..."

PACKAGES=(
    # Build essentials
    build-essential
    curl
    wget
    git
    unzip
    zip
    cmake
    pkg-config
    # libimobiledevice (comunicacao com iOS via USB)
    usbmuxd
    libimobiledevice-dev
    libimobiledevice-utils
    libusbmuxd-dev
    libplist-dev
    ideviceinstaller
    # Dependencias do .NET
    apt-transport-https
    # Dependencias do Swift (runtime)
    # libncurses6 e libtinfo6 sao necessarios para Swift 6.x no Ubuntu 24.04+
    # (Ubuntu 24.04 instala apenas libncursesw6 por padrao, que eh incompativel)
    libncurses6
    libtinfo6
    libc6-dev
    gcc
    libcurl4-openssl-dev
    libxml2-dev
    libssl-dev
    # Dependencias para compilar zsign
    libminizip-dev
    liblzma-dev
    # Utilitarios de rede
    iproute2
    # Outros utilitarios
    strace
    ltrace
)

# Verifica quais pacotes ainda nao estao instalados
MISSING_PACKAGES=()
for pkg in "${PACKAGES[@]}"; do
    if ! dpkg -s "$pkg" &>/dev/null; then
        MISSING_PACKAGES+=("$pkg")
    fi
done

if [ ${#MISSING_PACKAGES[@]} -eq 0 ]; then
    log_skip "Todos os pacotes do sistema"
else
    echo -e "  Instalando ${#MISSING_PACKAGES[@]} pacote(s) faltando: ${MISSING_PACKAGES[*]}"
    if can_sudo; then
        sudo apt-get update -qq
        sudo apt-get install -y -qq "${MISSING_PACKAGES[@]}"
        log_ok "Pacotes do sistema instalados."
    else
        log_warn "sudo nao disponivel. Tentando instalar sem sudo..."
        # Fallback: download .deb packages and extract libs to ~/lib
        mkdir -p "$HOME/lib"
        NEED_LIB_FALLBACK=false
        for pkg in "${MISSING_PACKAGES[@]}"; do
            case "$pkg" in
                libncurses6|libtinfo6)
                    NEED_LIB_FALLBACK=true
                    ;;
            esac
        done

        if [ "$NEED_LIB_FALLBACK" = true ]; then
            echo -e "  Extraindo libs do Swift (libncurses6, libtinfo6) para ~/lib..."
            TMPDIR_EXTRACT=$(mktemp -d)
            for pkg in libncurses6 libtinfo6; do
                if ! dpkg -s "$pkg" &>/dev/null; then
                    apt download "$pkg" 2>/dev/null && \
                    dpkg-deb -x ${pkg}_*.deb "$TMPDIR_EXTRACT" 2>/dev/null && \
                    rm -f ${pkg}_*.deb
                fi
            done
            # Copy extracted .so files to ~/lib
            find "$TMPDIR_EXTRACT" -name "*.so*" -type f -exec cp {} "$HOME/lib/" \;
            # Create versioned symlinks
            for sofile in "$HOME/lib"/*.so.*.*; do
                [ -f "$sofile" ] || continue
                base=$(basename "$sofile" | sed 's/\(\.so\.[0-9]*\).*/\1/')
                ln -sf "$(basename "$sofile")" "$HOME/lib/$base" 2>/dev/null
            done
            rm -rf "$TMPDIR_EXTRACT"
            log_ok "Libs do Swift extraidas em ~/lib"
        fi

        log_warn "Outros pacotes do sistema requerem sudo: ${MISSING_PACKAGES[*]}"
        echo -e "  Execute manualmente: ${BLUE}sudo apt install ${MISSING_PACKAGES[*]}${NC}"
    fi
fi

# ------------------------------------------------------------------------------
# Step 2: .NET SDK 10.0
# ------------------------------------------------------------------------------

log_step 2 "Instalando .NET SDK 10.0..."

# Check both PATH and known install location
DOTNET_CMD=""
if check_command dotnet; then
    DOTNET_CMD="dotnet"
elif [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET_CMD="$HOME/.dotnet/dotnet"
fi

if [ -n "$DOTNET_CMD" ]; then
    DOTNET_VER=$("$DOTNET_CMD" --version 2>/dev/null || echo "unknown")
    log_skip "dotnet $DOTNET_VER"
else
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
    rm -f /tmp/dotnet-install.sh
    log_ok ".NET SDK 10.0 instalado em ~/.dotnet"
fi

# Garante que .dotnet esta no PATH para o resto do script
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

# ------------------------------------------------------------------------------
# Step 3: Swift 6.0 Toolchain
# ------------------------------------------------------------------------------

log_step 3 "Instalando Swift 6.0 toolchain..."

SWIFT_DIR="$HOME/swift"

# Set LD_LIBRARY_PATH for Swift's library dependencies
export LD_LIBRARY_PATH="$HOME/lib:${LD_LIBRARY_PATH:-}"

swift_works() {
    "$1" --version &>/dev/null
}

if [ -x "$SWIFT_DIR/usr/bin/swift" ] && swift_works "$SWIFT_DIR/usr/bin/swift"; then
    SWIFT_VER=$("$SWIFT_DIR/usr/bin/swift" --version 2>/dev/null | head -1 || echo "unknown")
    log_skip "swift ($SWIFT_VER)"
elif check_command swift && swift_works swift; then
    SWIFT_VER=$(swift --version 2>/dev/null | head -1 || echo "unknown")
    log_skip "swift no PATH ($SWIFT_VER)"
else
    echo -e "  Baixando Swift 6.0.3 para Ubuntu..."

    # Detecta a versao do Ubuntu
    UBUNTU_VER_DOT=$(. /etc/os-release && echo "$VERSION_ID")
    UBUNTU_VER=$(echo "$UBUNTU_VER_DOT" | tr -d '.')

    # Swift download URL format (without architecture suffix in filename)
    # Tested: Ubuntu 22.04, 24.04
    SWIFT_URL="https://download.swift.org/swift-6.0.3-release/ubuntu${UBUNTU_VER}/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-ubuntu${UBUNTU_VER_DOT}.tar.gz"

    SWIFT_TAR="/tmp/swift-6.0.3.tar.gz"

    echo -e "  URL: $SWIFT_URL"
    echo -e "  ${YELLOW}Download ~750MB — pode levar alguns minutos...${NC}"
    if curl -fSL --progress-bar "$SWIFT_URL" -o "$SWIFT_TAR"; then
        mkdir -p "$SWIFT_DIR"
        echo -e "  Extraindo..."
        tar xzf "$SWIFT_TAR" -C "$SWIFT_DIR" --strip-components=1
        rm -f "$SWIFT_TAR"

        # Verify Swift works
        if swift_works "$SWIFT_DIR/usr/bin/swift"; then
            log_ok "Swift 6.0.3 instalado em $SWIFT_DIR"
        else
            # Might need library path fix
            if LD_LIBRARY_PATH="$HOME/lib:${LD_LIBRARY_PATH:-}" "$SWIFT_DIR/usr/bin/swift" --version &>/dev/null; then
                log_ok "Swift 6.0.3 instalado em $SWIFT_DIR (requer LD_LIBRARY_PATH)"
            else
                log_warn "Swift instalado mas nao executa corretamente."
                echo -e "  Verifique dependencias: ${BLUE}ldd $SWIFT_DIR/usr/bin/swift${NC}"
            fi
        fi
    else
        log_warn "Download automatico falhou."
        echo -e "  Instale manualmente seguindo: ${BLUE}https://swift.org/install/linux${NC}"
        echo -e "  Extraia para: $SWIFT_DIR"
    fi
fi

export PATH="$SWIFT_DIR/usr/bin:$PATH"

# ------------------------------------------------------------------------------
# Step 4: xtool (iOS development from Linux)
# ------------------------------------------------------------------------------

log_step 4 "Instalando xtool..."

if check_command xtool; then
    XTOOL_VER=$(xtool --version 2>/dev/null || echo "installed")
    log_skip "xtool ($XTOOL_VER)"
else
    ARCH=$(uname -m)
    XTOOL_URL="https://github.com/xtool-org/xtool/releases/latest/download/xtool-${ARCH}.AppImage"

    echo -e "  Baixando xtool AppImage para $ARCH..."
    if curl -fSL "$XTOOL_URL" -o /tmp/xtool 2>/dev/null; then
        chmod +x /tmp/xtool

        # Prefer /usr/local/bin (system-wide), fallback to ~/bin (user-local)
        if can_sudo; then
            sudo mv /tmp/xtool /usr/local/bin/xtool
            log_ok "xtool instalado em /usr/local/bin/xtool"
        else
            mv /tmp/xtool "$HOME/bin/xtool"
            log_ok "xtool instalado em ~/bin/xtool (sem sudo)"
        fi
    else
        log_err "Falha ao baixar xtool."
        echo -e "  Baixe manualmente de: ${BLUE}https://github.com/xtool-org/xtool/releases/latest${NC}"
    fi
fi

# ------------------------------------------------------------------------------
# Step 5: Criar ~/bin e instalar Mabel CLI
# ------------------------------------------------------------------------------

log_step 5 "Configurando Mabel CLI..."

MABEL_LINK="$HOME/bin/mabel"
MABEL_TARGET="$SCRIPT_DIR/mabel.sh"

if [ -f "$MABEL_TARGET" ]; then
    if [ -L "$MABEL_LINK" ] && [ "$(readlink -f "$MABEL_LINK")" = "$(readlink -f "$MABEL_TARGET")" ]; then
        log_skip "Mabel CLI (~/bin/mabel)"
    else
        ln -sf "$MABEL_TARGET" "$MABEL_LINK"
        chmod +x "$MABEL_TARGET"
        log_ok "Mabel CLI linkado em ~/bin/mabel"
    fi
else
    log_warn "mabel.sh nao encontrado em $SCRIPT_DIR"
    echo -e "  O Mabel CLI sera linkado quando o projeto for compilado."
fi

# ------------------------------------------------------------------------------
# Step 6: Configurar PATH e LD_LIBRARY_PATH no .bashrc
# ------------------------------------------------------------------------------

log_step 6 "Configurando PATH..."

PATH_BLOCK="export PATH=\"\$HOME/bin:\$HOME/.dotnet:\$HOME/.dotnet/tools:\$HOME/swift/usr/bin:\$PATH\"
export DOTNET_ROOT=\"\$HOME/.dotnet\"
export LD_LIBRARY_PATH=\"\$HOME/lib:\${LD_LIBRARY_PATH:-}\""

if grep -q "$MARKER" "$BASHRC" 2>/dev/null; then
    sed -i "/$MARKER/,/$MARKER_END/d" "$BASHRC"
fi

cat >> "$BASHRC" << EOF

$MARKER
$PATH_BLOCK
$MARKER_END
EOF
log_ok "PATH e LD_LIBRARY_PATH configurados no .bashrc"

# ------------------------------------------------------------------------------
# Step 7: Iniciar usbmuxd (necessario para iOS via USB)
# ------------------------------------------------------------------------------

log_step 7 "Verificando usbmuxd..."

if check_command usbmuxd; then
    if pgrep -x usbmuxd &>/dev/null; then
        log_skip "usbmuxd (rodando)"
    else
        echo -e "  Iniciando usbmuxd..."
        if can_sudo; then
            sudo usbmuxd -f -d &>/dev/null &
            disown
            sleep 1
            if pgrep -x usbmuxd &>/dev/null; then
                log_ok "usbmuxd iniciado."
            else
                log_warn "usbmuxd nao iniciou. Execute manualmente: sudo usbmuxd"
            fi
        else
            log_warn "usbmuxd requer sudo para iniciar."
            echo -e "  Execute manualmente: ${BLUE}sudo usbmuxd${NC}"
        fi
    fi
else
    log_warn "usbmuxd nao instalado."
    echo -e "  Execute: ${BLUE}sudo apt install usbmuxd${NC}"
fi

# WSL: verificar usbipd
if uname -r 2>/dev/null | grep -qi microsoft; then
    echo ""
    echo -e "  ${YELLOW}WSL detectado:${NC} Para conectar iPhone via USB, voce precisa de usbipd no Windows."
    echo -e "  No PowerShell (admin): ${BLUE}winget install usbipd${NC}"
    echo -e "  Depois: ${BLUE}usbipd list${NC} e ${BLUE}usbipd attach --wsl --busid <BUSID>${NC}"
    echo -e "  Guia completo: ${BLUE}https://learn.microsoft.com/en-us/windows/wsl/connect-usb${NC}"
fi

# ------------------------------------------------------------------------------
# Resumo
# ------------------------------------------------------------------------------

echo ""
echo -e "${MAGENTA}============================================${NC}"
echo -e "${GREEN} Setup concluido!${NC}"
echo -e "${MAGENTA}============================================${NC}"
echo ""
echo -e " Ferramentas instaladas:"

# Reload PATH for summary
export PATH="$HOME/bin:$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/swift/usr/bin:$PATH"

for tool in dotnet swift xtool mabel usbmuxd ideviceinfo; do
    if check_command "$tool"; then
        VER=""
        case "$tool" in
            dotnet) VER=$($tool --version 2>/dev/null || true) ;;
            swift)  VER=$($tool --version 2>/dev/null | head -1 | grep -oP '\d+\.\d+(\.\d+)?' || true) ;;
            xtool)  VER=$($tool --version 2>/dev/null || true) ;;
        esac
        if [ -n "$VER" ]; then
            echo -e "   ${GREEN}+${NC} $tool ($VER)"
        else
            echo -e "   ${GREEN}+${NC} $tool"
        fi
    elif [ -x "$HOME/bin/$tool" ] || [ -x "$HOME/.dotnet/$tool" ] || [ -x "$HOME/swift/usr/bin/$tool" ]; then
        echo -e "   ${GREEN}+${NC} $tool (encontrado fora do PATH — recarregue o shell)"
    else
        echo -e "   ${RED}-${NC} $tool (requer instalacao manual)"
    fi
done

echo ""
echo -e " ${YELLOW}Proximos passos:${NC}"
echo -e "   1. Recarregue o shell:  ${BLUE}source ~/.bashrc${NC}"
echo -e "   2. Configure o xtool:   ${BLUE}xtool setup${NC}"
echo -e "      (voce vai precisar do Xcode.xip - baixe em https://developer.apple.com)"
echo -e "   3. Crie um projeto:     ${BLUE}mabel create meu-app${NC}"
echo ""
echo -e " ${YELLOW}Nota para WSL:${NC}"
echo -e "   Para conectar dispositivos iOS via USB, configure o usbipd no Windows:"
echo -e "   ${BLUE}https://learn.microsoft.com/en-us/windows/wsl/connect-usb${NC}"
echo ""

# ------------------------------------------------------------------------------
# Troubleshooting & Notas
# ------------------------------------------------------------------------------

cat << 'TROUBLESHOOTING'

TROUBLESHOOTING — iOS Device Limit (Free Developer Account)
============================================================

Problema:
  Contas gratuitas do Apple Developer tem limite de 3 iPhones registrados.
  Se voce atingir o limite, o deploy falha com:
  "Your development team has reached the maximum number of registered iPhone devices"

Solucoes:

1. NOVA CONTA APPLE ID (Recomendado - Gratuito)
   - Crie uma nova conta em: https://appleid.apple.com
   - Execute: xtool auth
   - Faca login com a nova conta
   - Deploy funcionara (0 devices no limite)

2. CONTA PAGA ($99/ano)
   - Aumenta limite para 100 devices
   - Permite remover devices pelo portal
   - Necessario para publicar na App Store
   - https://developer.apple.com/programs/

3. ESPERAR RESET ANUAL
   - Contas free resetam devices 1x por ano
   - Data imprevisivel (aniversario da conta)

Comandos Uteis:
   xtool devices              - Lista devices conectados via USB
   xtool ds devices list      - Lista devices registrados no portal
   idevice_id -l              - Lista UDIDs dos devices USB
   ideviceinstaller -l        - Lista apps instalados

Para mais ajuda:
   https://github.com/dmarquezbh/mabel-framework/issues

TROUBLESHOOTING

