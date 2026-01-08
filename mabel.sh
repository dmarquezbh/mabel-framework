#!/bin/bash
# ==============================================================================
# 🦋 MABEL FRAMEWORK - CLI
# The Cross-Platform .NET Web Wrapper for iOS, Android & Desktop
# ==============================================================================

export PATH="/home/dmarques/bin:/home/dmarques/swift/usr/bin:$PATH"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
NC='\033[0m'

# --- INTERNACIONALIZAÇÃO (PT/EN) ---

set_language() {
    if [[ "$1" == "en" ]] || ([[ -z "$1" ]] && [[ "$LANG" != pt_* ]]); then
        LANG_CODE="en"
        L_BANNER="🦋 Cross-Platform Web-Native App CLI"
        L_TITLE_DEV="--- DEVICE MANAGEMENT ---"
        L_OPT_1="1) 🔐 Login Apple ID (iOS)"
        L_OPT_2="2) 📱 List Devices"
        L_OPT_3="3) 📋 Account Status"
        L_TITLE_SCAFFOLD="--- MABEL SCAFFOLD ---"
        L_OPT_4="4) 🌐 New Project (Blazor WASM + iOS + HMR)"
        L_OPT_5="5) 🖥️  Add Desktop Support (Photino) - In Progress"
        L_TITLE_DEV_OPS="--- DEVELOPMENT ---"
        L_OPT_6="6) 🚀 Build & Deploy (xtool dev)"
        L_OPT_7="7) 📜 View Logs (Debug & Bridge)"
        L_TITLE_SYS="--- SYSTEM ---"
        L_OPT_8="8) 🏗️  Export Mabel Boilerplate for GitHub"
        L_OPT_9="9) 🛠️  Install Mabel CLI (~/bin)"
        L_OPT_10="10) 🌐 Change Language (Para Português)"
        L_OPT_EXIT="11) 🚪 Exit"
        L_PROMPT="🦋 Option: "
        L_ERR_REQUIRED="❌ Error: Name and Bundle ID are required."
        L_APP_NAME_PROMPT="App Name: "
        L_BUNDLE_PROMPT="Bundle ID (ex: com.mabel.app): "
        L_CREATING_BLAZOR="📦 Creating Blazor UI..."
        L_CREATING_WRAPPER="📱 Creating iOS Wrapper (via xtool)..."
        L_PROJECT_CREATED="✅ Mabel Project Created!"
        L_RUN_INSTR="To run: cd \$APP_NAME && ./mabel-sync.sh && cd ios_app && xtool dev"
        L_SYNC_OK="✅ Mabel Sync OK!"
        L_FOLDER_PROMPT="Repository folder name: "
        L_BOILERPLATE_OK="✅ MABEL Boilerplate ready in "
        L_CHECKING_CONFLICTS="🔍 Checking for installation conflicts..."
        L_DETECTED_GLOBAL="⚠️  Detected global hook in /usr/local/bin/mabel."
        L_REMOVING_OLD="🗑️  Removing old hook (may ask for sudo)..."
        L_INSTALLING_LOCAL="🛠️  Installing Mabel CLI in ~/bin..."
        L_INSTALL_SUCCESS="✅ Mabel CLI successfully installed in ~/bin!"
        L_INSTALL_INSTR="You can now use the 'mabel' command from anywhere."
        L_INSTALL_FAIL="❌ Installation failed. Check if ~/bin exists or permissions."
        L_PATH_PROMPT="Project path: "
        L_INVALID_OPT="Invalid Option"
    else
        LANG_CODE="pt"
        L_BANNER="🦋 CLI para Apps Nativos com Tecnologia Web"
        L_TITLE_DEV="--- GESTÃO DE DISPOSITIVOS ---"
        L_OPT_1="1) 🔐 Login Apple ID (iOS)"
        L_OPT_2="2) 📱 Listar Dispositivos"
        L_OPT_3="3) 📋 Status da Conta"
        L_TITLE_SCAFFOLD="--- MABEL SCAFFOLD ---"
        L_OPT_4="4) 🌐 New Project (Blazor WASM + iOS + HMR)"
        L_OPT_5="5) 🖥️  Add Desktop Support (Photino) - Em Progresso"
        L_TITLE_DEV_OPS="--- DESENVOLVIMENTO ---"
        L_OPT_6="6) 🚀 Build & Deploy (xtool dev)"
        L_OPT_7="7) 📜 Ver Logs (Debug & Bridge)"
        L_TITLE_SYS="--- SISTEMA ---"
        L_OPT_8="8) 🏗️  Export Mabel Boilerplate for GitHub"
        L_OPT_9="9) 🛠️  Instalar Mabel CLI (~/bin)"
        L_OPT_10="10) 🌐 Mudar Idioma (To English)"
        L_OPT_EXIT="11) 🚪 Sair"
        L_PROMPT="🦋 Opção: "
        L_ERR_REQUIRED="❌ Erro: Nome e Bundle ID são obrigatórios."
        L_APP_NAME_PROMPT="Nome do App: "
        L_BUNDLE_PROMPT="Bundle ID (ex: com.mabel.app): "
        L_CREATING_BLAZOR="📦 Criando Blazor UI..."
        L_CREATING_WRAPPER="📱 Criando Wrapper iOS (via xtool)..."
        L_PROJECT_CREATED="✅ Projeto Mabel Criado!"
        L_RUN_INSTR="Para rodar: cd \$APP_NAME && ./mabel-sync.sh && cd ios_app && xtool dev"
        L_SYNC_OK="✅ Mabel Sync OK!"
        L_FOLDER_PROMPT="Nome da pasta do repositório: "
        L_BOILERPLATE_OK="✅ Boilerplate MABEL pronto em "
        L_CHECKING_CONFLICTS="🔍 Verificando conflitos de instalação..."
        L_DETECTED_GLOBAL="⚠️  Detectado apontamento global em /usr/local/bin/mabel."
        L_REMOVING_OLD="🗑️  Removendo apontamento antigo (pode solicitar sudo)..."
        L_INSTALLING_LOCAL="🛠️  Instalando Mabel CLI em ~/bin..."
        L_INSTALL_SUCCESS="✅ Mabel CLI instalado com sucesso em ~/bin!"
        L_INSTALL_INSTR="Você agora pode usar o comando 'mabel' de qualquer lugar."
        L_INSTALL_FAIL="❌ Falha na instalação. Verifique se ~/bin existe ou permissões."
        L_PATH_PROMPT="Path do projeto: "
        L_INVALID_OPT="Opção Inválida"
    fi
}

set_language # Inicializa com base no sistema

show_banner() {
    echo -e "${MAGENTA}"
    echo "  __  __   _   ___  _____ _      "
    echo " |  \/  | /_\ | _ )| ____| |     "
    echo " | |\/| |/ _ \| _ \|  _| | |     "
    echo " |_|  |_/_/ \_\___/|___|_|____   "
    echo "                                 "
    echo " $L_BANNER"
    echo -e "${NC}"
}

show_menu() {
    echo -e "${YELLOW}$L_TITLE_DEV${NC}"
    echo "  $L_OPT_1"
    echo "  $L_OPT_2"
    echo "  $L_OPT_3"
    echo ""
    echo -e "${YELLOW}$L_TITLE_SCAFFOLD${NC}"
    echo "  $L_OPT_4"
    echo "  $L_OPT_5"
    echo ""
    echo -e "${YELLOW}$L_TITLE_DEV_OPS${NC}"
    echo "  $L_OPT_6"
    echo "  $L_OPT_7"
    echo ""
    echo -e "${YELLOW}$L_TITLE_SYS${NC}"
    echo "  $L_OPT_8"
    echo "  $L_OPT_9"
    echo "  $L_OPT_10"
    echo "  $L_OPT_EXIT"
    echo ""
    read -r -p "$L_PROMPT" choice < /dev/tty
}

# --- FUNÇÕES CORE ---

scaffold_mabel() {
    read -p "$L_APP_NAME_PROMPT" APP_NAME
    read -p "$L_BUNDLE_PROMPT" BUNDLE_ID
    
    if [ -z "$APP_NAME" ] || [ -z "$BUNDLE_ID" ]; then
        echo -e "${RED}$L_ERR_REQUIRED${NC}"
        return 1
    fi

    BASE_DIR="$(pwd)/$APP_NAME"
    mkdir -p "$BASE_DIR"
    
    echo -e "${BLUE}$L_CREATING_BLAZOR${NC}"
    dotnet new blazorwasm -o "$BASE_DIR/blazor_app" --no-restore

    echo -e "${BLUE}$L_CREATING_WRAPPER${NC}"
    cd "$BASE_DIR" && xtool new ios_app --skip-setup
    
    # Configuração do Wrapper
    echo "version: 1" > "$BASE_DIR/ios_app/xtool.yml"
    echo "bundleID: $BUNDLE_ID" >> "$BASE_DIR/ios_app/xtool.yml"

    # Injeção do Package.swift
    cat <<EOF > "$BASE_DIR/ios_app/Package.swift"
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ios_app",
    platforms: [.iOS(.v17)],
    products: [.library(name: "ios_app", targets: ["ios_app"])],
    targets: [
        .target(
            name: "ios_app",
            dependencies: [],
            resources: [.copy("Resources")]
        )
    ]
)
EOF

    # Injeção da Bridge Nativa Mabel
    mkdir -p "$BASE_DIR/ios_app/Sources/ios_app"
    IP_LOCAL=$(ip addr show | grep -w 'inet' | grep -v 127.0.0.1 | head -n 1 | awk '{print $2}' | cut -d/ -f1)
    
    cat <<EOF > "$BASE_DIR/ios_app/Sources/ios_app/ContentView.swift"
import SwiftUI
import WebKit

class MabelBridge: NSObject, WKScriptMessageHandler {
    func userContentController(_ uc: WKUserContentController, didReceive msg: WKScriptMessage) {
        if msg.name == "iosNative", let body = msg.body as? String {
            NSLog("🦋 [MABEL-BRIDGE] \(body)")
            let generator = UINotificationFeedbackGenerator()
            generator.notificationOccurred(.success)
            let alert = UIAlertController(title: "Mabel Engine", message: body, preferredStyle: .alert)
            alert.addAction(UIAlertAction(title: "OK", style: .default))
            UIApplication.shared.connectedScenes.compactMap { \$0 as? UIWindowScene }.first?.windows.first?.rootViewController?.present(alert, animated: true)
        }
    }
}

struct WebView: UIViewRepresentable {
    let devMode = false // Mudar para true para HMR
    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        let cc = WKUserContentController()
        cc.add(MabelBridge(), name: "iosNative")
        let js = "window.callNative = function(m){ window.webkit.messageHandlers.iosNative.postMessage(m); };"
        cc.addUserScript(WKUserScript(source: js, injectionTime: .atDocumentStart, forMainFrameOnly: false))
        config.userContentController = cc
        
        // Scheme Handler Mabel para arquivos locais
        config.setURLSchemeHandler(AppSchemeHandler(), forURLScheme: "app")
        
        let wv = WKWebView(frame: .zero, configuration: config)
        if #available(iOS 16.4, *) { wv.isInspectable = true }
        return wv
    }
    func updateUIView(_ wv: WKWebView, context: Context) {
        let url = devMode ? URL(string: "http://$IP_LOCAL:5000")! : URL(string: "app://localhost/index.html")!
        wv.load(URLRequest(url: url))
    }
}

class AppSchemeHandler: NSObject, WKURLSchemeHandler {
    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url else { return }
        var path = url.path.hasPrefix("/") ? String(url.path.dropFirst()) : url.path
        if path.isEmpty { path = "index.html" }
        
        let bundleName = "ios_app_ios_app.bundle"
        let potentialDirs = [
            Bundle.main.resourceURL?.appendingPathComponent(bundleName).appendingPathComponent("Resources"),
            Bundle.main.resourceURL?.appendingPathComponent("Resources"),
            Bundle.main.bundleURL
        ]
        
        for dir in potentialDirs {
            if let target = dir?.appendingPathComponent(path), let data = try? Data(contentsOf: target) {
                let ext = target.pathExtension.lowercased()
                let mime = ext == "wasm" ? "application/wasm" : (ext == "js" ? "application/javascript" : "text/html")
                let res = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": mime, "Access-Control-Allow-Origin": "*"])!
                task.didReceive(res); task.didReceive(data); task.didFinish()
                return
            }
        }
        task.didFailWithError(NSError(domain: "mabel", code: 404))
    }
    func webView(_ wv: WKWebView, stop task: WKURLSchemeTask) {}
}

struct ContentView: View { var body: some View { WebView().edgesIgnoringSafeArea(.all) } }
EOF

    # Scripts de automação Mabel
    cat <<EOF > "$BASE_DIR/mabel-sync.sh"
#!/bin/bash
# Sincronizador Automático Mabel
cd blazor_app && dotnet publish -c Release -p:BlazorEnableCompression=false -o publish && cd ..
mkdir -p ios_app/Sources/ios_app/Resources
cp -r blazor_app/publish/wwwroot/* ios_app/Sources/ios_app/Resources/
cd ios_app/Sources/ios_app/Resources/_framework
for f in blazor.webassembly.*.js; do [ -f "\$f" ] && cp "\$f" blazor.webassembly.js; done
for f in dotnet.*.js; do [ -f "\$f" ] && cp "\$f" dotnet.js; done
for f in dotnet.native.*.js; do [ -f "\$f" ] && cp "\$f" dotnet.native.js; done
for f in dotnet.runtime.*.js; do [ -f "\$f" ] && cp "\$f" dotnet.runtime.js; done
for f in dotnet.native.*.wasm; do [ -f "\$f" ] && cp "\$f" dotnet.native.wasm; done
cd ../../../../../
find ios_app/Sources/ios_app/Resources -name "blazor.boot.json" -exec sed -i 's/"integrity": {[^}]*}//g' {} +
echo "$L_SYNC_OK"
EOF
    chmod +x "$BASE_DIR/mabel-sync.sh"

    echo -e "${GREEN}$L_PROJECT_CREATED${NC}"
    echo -e "$L_RUN_INSTR"
}

export_mabel_boilerplate() {
    read -p "$L_FOLDER_PROMPT" FOLDER
    if [ -z "$FOLDER" ]; then FOLDER="mabel-framework"; fi
    mkdir -p "$FOLDER"
    cp $0 "$FOLDER/mabel.sh"
    echo "# 🦋 Mabel Framework" > "$FOLDER/README.md"
    echo "Cross-platform development for iOS, Android and Desktop via Linux." >> "$FOLDER/README.md"
    echo -e "${GREEN}$L_BOILERPLATE_OK$FOLDER${NC}"
}

install_mabel_cli() {
    echo -e "${BLUE}$L_CHECKING_CONFLICTS${NC}"
    if [ -f "/usr/local/bin/mabel" ]; then
        echo -e "${YELLOW}$L_DETECTED_GLOBAL${NC}"
        echo -e "${BLUE}$L_REMOVING_OLD${NC}"
        sudo rm "/usr/local/bin/mabel"
    fi

    echo -e "${BLUE}$L_INSTALLING_LOCAL${NC}"
    mkdir -p "$HOME/bin"
    if ln -sf "$(realpath "$0")" "$HOME/bin/mabel"; then
        echo -e "${GREEN}$L_INSTALL_SUCCESS${NC}"
        echo "$L_INSTALL_INSTR"
    else
        echo -e "${RED}$L_INSTALL_FAIL${NC}"
    fi
}

toggle_language() {
    if [ "$LANG_CODE" == "pt" ]; then
        set_language "en"
    else
        set_language "pt"
    fi
}

# --- LOOP PRINCIPAL ---

show_banner

while true; do
    show_menu
    case $choice in
        1) xtool auth login --mode password ;;
        2) xtool devices ;;
        3) xtool auth status ;;
        4) scaffold_mabel ;;
        5) echo -e "${BLUE}Desktop (Photino) coming soon...${NC}" ;;
        6) read -p "$L_PATH_PROMPT" p; cd "$p" && xtool dev ;;
        7) idevicesyslog | grep -E "MABEL-BRIDGE|JS:|BlazorOS" ;;
        8) export_mabel_boilerplate ;;
        9) install_mabel_cli ;;
        10) toggle_language ;;
        11) exit 0 ;;
        *) echo "$L_INVALID_OPT" ;;
    esac
done
