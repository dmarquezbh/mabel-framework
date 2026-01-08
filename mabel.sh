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

show_banner() {
    echo -e "${MAGENTA}"
    echo "  __  __   _   ___  _____ _      "
    echo " |  \/  | /_\ | _ )| ____| |     "
    echo " | |\/| |/ _ \| _ \|  _| | |     "
    echo " |_|  |_/_/ \_\___/|___|_|____   "
    echo "                                 "
    echo " 🦋 Cross-Platform Web-Native App CLI"
    echo -e "${NC}"
}

show_menu() {
    echo -e "${YELLOW}--- GESTÃO DE DISPOSITIVOS ---${NC}"
    echo "  1) 🔐 Login Apple ID (iOS)"
    echo "  2) 📱 Listar Dispositivos"
    echo "  3) 📋 Status da Conta"
    echo ""
    echo -e "${YELLOW}--- MABEL SCAFFOLD ---${NC}"
    echo "  4) 🌐 New Project (Blazor WASM + iOS + HMR)"
    echo "  5) 🖥️  Add Desktop Support (Photino) - In Progress"
    echo ""
    echo -e "${YELLOW}--- DESENVOLVIMENTO ---${NC}"
    echo "  6) 🚀 Build & Deploy (xtool dev)"
    echo "  7) 📜 Ver Logs (Debug & Bridge)"
    echo ""
    echo -e "${YELLOW}--- SISTEMA ---${NC}"
    echo "  8) 🏗️  Export Mabel Boilerplate for GitHub"
    echo "  9) 💤 Suspender Computador"
    echo "  10) 🚪 Sair"
    echo ""
    read -r -p "🦋 Opção: " choice < /dev/tty
}

# --- FUNÇÕES CORE ---

scaffold_mabel() {
    read -p "Nome do App: " APP_NAME
    read -p "Bundle ID (ex: com.mabel.app): " BUNDLE_ID
    
    if [ -z "$APP_NAME" ] || [ -z "$BUNDLE_ID" ]; then
        echo -e "${RED}❌ Erro: Nome e Bundle ID são obrigatórios.${NC}"
        return 1
    fi

    BASE_DIR="$(pwd)/$APP_NAME"
    mkdir -p "$BASE_DIR"
    
    echo -e "${BLUE}📦 Criando Blazor UI...${NC}"
    dotnet new blazorwasm -o "$BASE_DIR/blazor_app" --no-restore

    echo -e "${BLUE}📱 Criando Wrapper iOS (via xtool)...${NC}"
    cd "$BASE_DIR" && xtool new ios_app --skip-setup
    
    # Configuração do Wrapper
    echo "version: 1" > "$BASE_DIR/ios_app/xtool.yml"
    echo "bundleID: $BUNDLE_ID" >> "$BASE_DIR/ios_app/xtool.yml"

    # Injeção do Package.swift (Formato Mabel 0.1)
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
    IP_LOCAL=\$(ip addr show | grep -w 'inet' | grep -v 127.0.0.1 | head -n 1 | awk '{print \$2}' | cut -d/ -f1)
    
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
echo "✅ Mabel Sync OK!"
EOF
    chmod +x "$BASE_DIR/mabel-sync.sh"

    echo -e "${GREEN}✅ Mabel Project Created!${NC}"
    echo "Para rodar: cd $APP_NAME && ./mabel-sync.sh && cd ios_app && xtool dev"
}

export_mabel_boilerplate() {
    read -p "Nome da pasta do repositório: " FOLDER
    if [ -z "$FOLDER" ]; then FOLDER="mabel-framework"; fi
    mkdir -p "$FOLDER"
    cp $0 "$FOLDER/mabel.sh"
    echo "# 🦋 Mabel Framework" > "$FOLDER/README.md"
    echo "Cross-platform development for iOS, Android and Desktop via Linux." >> "$FOLDER/README.md"
    echo -e "${GREEN}✅ Boilerplate MABEL pronto em $FOLDER${NC}"
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
        6) read -p "Path do projeto: " p; cd "\$p" && xtool dev ;;
        7) idevicesyslog | grep -E "MABEL-BRIDGE|JS:|BlazorOS" ;;
        8) export_mabel_boilerplate ;;
        9) systemctl suspend ;;
        10) exit 0 ;;
        *) echo "Opção Inválida" ;;
    esac
done
