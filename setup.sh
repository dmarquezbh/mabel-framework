#!/bin/bash
# ==============================================================================
# MABEL FRAMEWORK - SETUP
# Instala todas as dependencias necessarias para o Mabel Framework
# Uso: ./setup.sh            (instalacao)
#      ./setup.sh --uninstall (remover tudo)
# Testado em: Ubuntu 22.04+ / WSL2
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

TOTAL_STEPS=6
BASHRC="$HOME/.bashrc"
MARKER="# >>> mabel-framework >>>"
MARKER_END="# <<< mabel-framework <<<"

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

    TOTAL_STEPS=5

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
    if [ -f "/usr/local/bin/xtool" ]; then
        sudo rm -f /usr/local/bin/xtool
        log_ok "xtool removido de /usr/local/bin/"
    else
        log_skip "xtool nao encontrado"
    fi

    # --- Step 5: Limpar PATH do .bashrc ---
    log_step 5 "Removendo configuracao do PATH do .bashrc..."
    if grep -q "$MARKER" "$BASHRC" 2>/dev/null; then
        # Remove o bloco entre os markers (inclusive)
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
    # libimobiledevice (comunicacao com iOS via USB)
    usbmuxd
    libimobiledevice-utils
    ideviceinstaller
    # Dependencias do .NET
    apt-transport-https
    # Dependencias do Swift (runtime)
    libcurl4-openssl-dev
    libxml2-dev
    # Utilitarios de rede
    iproute2
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
    echo -e "  Instalando ${#MISSING_PACKAGES[@]} pacote(s) faltando..."
    sudo apt-get update -qq
    sudo apt-get install -y -qq "${MISSING_PACKAGES[@]}"
    log_ok "Pacotes do sistema instalados."
fi

# ------------------------------------------------------------------------------
# Step 2: .NET SDK 10.0
# ------------------------------------------------------------------------------

log_step 2 "Instalando .NET SDK 10.0..."

if check_command dotnet; then
    DOTNET_VER=$(dotnet --version 2>/dev/null || echo "unknown")
    log_skip "dotnet $DOTNET_VER"
else
    # Instala via script oficial da Microsoft (nao precisa de repo extra)
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

if [ -x "$SWIFT_DIR/usr/bin/swift" ]; then
    SWIFT_VER=$("$SWIFT_DIR/usr/bin/swift" --version 2>/dev/null | head -1 || echo "unknown")
    log_skip "swift ($SWIFT_VER)"
elif check_command swift; then
    SWIFT_VER=$(swift --version 2>/dev/null | head -1 || echo "unknown")
    log_skip "swift no PATH ($SWIFT_VER)"
else
    echo -e "  Baixando Swift 6.0.3 para Ubuntu..."

    # Detecta a arquitetura
    ARCH=$(uname -m)
    if [ "$ARCH" = "aarch64" ]; then
        SWIFT_ARCH="aarch64"
    else
        SWIFT_ARCH="x86_64"
    fi

    # Detecta a versao do Ubuntu (ex: "22.04" -> "2204" e "22.04")
    UBUNTU_VER_DOT=$(. /etc/os-release && echo "$VERSION_ID")
    UBUNTU_VER=$(echo "$UBUNTU_VER_DOT" | tr -d '.')
    SWIFT_URL="https://download.swift.org/swift-6.0.3-release/ubuntu${UBUNTU_VER}/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-ubuntu${UBUNTU_VER_DOT}-${SWIFT_ARCH}.tar.gz"

    SWIFT_TAR="/tmp/swift-6.0.3.tar.gz"

    echo -e "  URL: $SWIFT_URL"
    if curl -fSL "$SWIFT_URL" -o "$SWIFT_TAR" 2>/dev/null; then
        mkdir -p "$SWIFT_DIR"
        tar xzf "$SWIFT_TAR" -C "$SWIFT_DIR" --strip-components=1
        rm -f "$SWIFT_TAR"
        log_ok "Swift 6.0.3 instalado em $SWIFT_DIR"
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
        sudo mv /tmp/xtool /usr/local/bin/xtool
        log_ok "xtool instalado em /usr/local/bin/xtool"
    else
        log_err "Falha ao baixar xtool."
        echo -e "  Baixe manualmente de: ${BLUE}https://github.com/xtool-org/xtool/releases/latest${NC}"
    fi
fi

# ------------------------------------------------------------------------------
# Step 5: Criar ~/bin e instalar Mabel CLI
# ------------------------------------------------------------------------------

log_step 5 "Configurando Mabel CLI..."

mkdir -p "$HOME/bin"

MABEL_LINK="$HOME/bin/mabel"
MABEL_TARGET="$SCRIPT_DIR/mabel.sh"

if [ -L "$MABEL_LINK" ] && [ "$(readlink -f "$MABEL_LINK")" = "$(readlink -f "$MABEL_TARGET")" ]; then
    log_skip "Mabel CLI (~/bin/mabel)"
else
    ln -sf "$MABEL_TARGET" "$MABEL_LINK"
    chmod +x "$MABEL_TARGET"
    log_ok "Mabel CLI linkado em ~/bin/mabel"
fi

# ------------------------------------------------------------------------------
# Step 6: Configurar PATH no .bashrc
# ------------------------------------------------------------------------------

log_step 6 "Configurando PATH..."

PATH_BLOCK="export PATH=\"\$HOME/bin:\$HOME/.dotnet:\$HOME/.dotnet/tools:\$HOME/swift/usr/bin:\$PATH\"
export DOTNET_ROOT=\"\$HOME/.dotnet\""

if grep -q "$MARKER" "$BASHRC" 2>/dev/null; then
    # Remove o bloco antigo e reinsere atualizado
    sed -i "/$MARKER/,/$MARKER_END/d" "$BASHRC"
    cat >> "$BASHRC" << EOF

$MARKER
$PATH_BLOCK
$MARKER_END
EOF
    log_ok "PATH atualizado no .bashrc"
else
    cat >> "$BASHRC" << EOF

$MARKER
$PATH_BLOCK
$MARKER_END
EOF
    log_ok "PATH adicionado ao .bashrc"
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

# Verifica cada ferramenta
for tool in dotnet swift xtool mabel usbmuxd ideviceinfo; do
    if check_command "$tool" || [ -x "$HOME/bin/$tool" ] || [ -x "$HOME/.dotnet/$tool" ] || [ -x "$HOME/swift/usr/bin/$tool" ]; then
        echo -e "   ${GREEN}+${NC} $tool"
    else
        echo -e "   ${RED}-${NC} $tool (requer instalacao manual)"
    fi
done

echo ""
echo -e " ${YELLOW}Proximos passos:${NC}"
echo -e "   1. Recarregue o shell:  ${BLUE}source ~/.bashrc${NC}"
echo -e "   2. Configure o xtool:   ${BLUE}xtool setup${NC}"
echo -e "      (voce vai precisar do Xcode.xip - baixe em https://developer.apple.com)"
echo -e "   3. Crie um projeto:     ${BLUE}mabel${NC}  (opcao 4)"
echo ""
echo -e " ${YELLOW}Nota para WSL:${NC}"
echo -e "   Para conectar dispositivos iOS via USB, configure o usbipd no Windows:"
echo -e "   ${BLUE}https://learn.microsoft.com/en-us/windows/wsl/connect-usb${NC}"
echo ""
