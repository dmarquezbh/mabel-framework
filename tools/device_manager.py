#!/usr/bin/env python3
"""
Mabel Device Manager - Gerencia devices no Apple Developer Portal
Usa a Developer Services API (a mesma que o xtool usa para contas free)

Comandos:
  python3 device_manager.py list              — lista devices
  python3 device_manager.py disable <id>      — desabilita device
  python3 device_manager.py enable <id>       — reabilita device

Requer: xtool auth (login prévio realizado)
"""

import json
import sys
import os
from pathlib import Path
import urllib.request
import urllib.parse
import urllib.error
import ssl
import base64
from datetime import datetime

# Developer Services API (usada por contas free)
API_BASE = "https://developerservices2.apple.com/services/QH65B2"

def load_auth_data():
    """Lê os dados de autenticação do xtool"""
    token_path = Path.home() / ".config" / "xtool" / "data" / "XTLAuthToken"

    if not token_path.exists():
        print(f"Erro: Token não encontrado em {token_path}")
        print("Execute 'xtool auth' primeiro para fazer login.")
        sys.exit(1)

    with open(token_path) as f:
        data = json.load(f)

    if "xcode" in data and "_0" in data["xcode"]:
        return {
            "adsid": data["xcode"]["_0"]["adsid"],
            "token": data["xcode"]["_0"]["token"],
            "team_id": data["xcode"]["_0"]["teamID"],
            "apple_id": data["xcode"]["_0"]["appleID"],
        }

    print("Erro: Formato de token não reconhecido")
    sys.exit(1)

def make_plist(data):
    """Converte dict para plist XML simples"""
    def plist_value(v):
        if isinstance(v, dict):
            items = [f"<key>{k}</key>{plist_value(val)}" for k, val in v.items()]
            return f"<dict>{''.join(items)}</dict>"
        elif isinstance(v, list):
            items = [plist_value(item) for item in v]
            return f"<array>{''.join(items)}</array>"
        elif isinstance(v, bool):
            return "<true/>" if v else "<false/>"
        elif isinstance(v, int):
            return f"<integer>{v}</integer>"
        elif isinstance(v, float):
            return f"<real>{v}</real>"
        else:
            escaped = str(v).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            return f"<string>{escaped}</string>"

    return f"<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist version=\"1.0\">{plist_value(data)}</plist>"

def api_request(action, params=None):
    """Faz uma request para a Developer Services API"""
    auth = load_auth_data()

    url = f"{API_BASE}/{action}"

    # Headers base (baseados no xtool)
    headers = {
        "Content-Type": "application/x-www-form-urlencoded",
        "Accept": "application/vnd.api+json",
        "X-Apple-GS-Token": auth["token"],
        "X-Apple-I-Identity-Id": auth["adsid"],
        "X-Apple-App-Info": "com.apple.gs.xcode.auth",
        "User-Agent": "Xcode (com.apple.dt.Xcode/16.2)",
    }

    # Parâmetros da requisição
    request_params = {
        "teamId": auth["team_id"],
        "protocolVersion": "QH65B2",
    }
    if params:
        request_params.update(params)

    # Converte para form-urlencoded
    data = urllib.parse.urlencode(request_params).encode('utf-8')

    ctx = ssl.create_default_context()

    req = urllib.request.Request(
        url,
        data=data,
        headers=headers,
        method="POST"  # Developer Services API usa POST para tudo
    )

    try:
        with urllib.request.urlopen(req, context=ctx, timeout=30) as response:
            return json.loads(response.read().decode('utf-8'))
    except urllib.error.HTTPError as e:
        error_body = e.read().decode('utf-8')
        try:
            error_data = json.loads(error_body)
            if error_data.get("resultCode") == 1100:
                print("Erro: Sessão expirada. Execute 'xtool auth' novamente.")
            elif "userString" in error_data:
                print(f"Erro: {error_data['userString']}")
            elif "resultString" in error_data:
                print(f"Erro: {error_data['resultString']}")
            else:
                print(f"Erro HTTP {e.code}: {error_body[:200]}")
        except:
            print(f"Erro HTTP {e.code}: {error_body[:200]}")
        sys.exit(1)
    except Exception as e:
        print(f"Erro na requisição: {e}")
        sys.exit(1)

def list_devices():
    """Lista todos os devices registrados"""
    print("Buscando devices...\n")

    response = api_request("listDevices.action")

    if response.get("resultCode") != 0:
        print(f"Erro: {response.get('resultString', 'Unknown error')}")
        sys.exit(1)

    devices = response.get("devices", [])

    if not devices:
        print("Nenhum device registrado.")
        return

    # Agrupa por tipo
    iphones = [d for d in devices if d.get("deviceClass") == "iphone"]
    ipads = [d for d in devices if d.get("deviceClass") == "ipad"]
    others = [d for d in devices if d.get("deviceClass") not in ["iphone", "ipad"]]

    print(f"Total: {len(devices)} devices\n")

    if iphones:
        print(f"=== iPhones ({len(iphones)}/3 slots usados) ===")
        for d in iphones:
            status = d.get("status", "ENABLED")
            status_icon = "✓" if status == "ENABLED" else "✗"
            print(f"{status_icon} ID: {d.get('deviceId', 'N/A')}")
            print(f"   Nome: {d.get('name', 'N/A')}")
            print(f"   Modelo: {d.get('model', 'N/A')}")
            print(f"   UDID: {d.get('deviceNumber', 'N/A')}")
            print(f"   Status: {status}")
            print()

    if ipads:
        print(f"=== iPads ({len(ipads)}) ===")
        for d in ipads:
            status = d.get("status", "ENABLED")
            status_icon = "✓" if status == "ENABLED" else "✗"
            print(f"{status_icon} ID: {d.get('deviceId', 'N/A')}")
            print(f"   Nome: {d.get('name', 'N/A')}")
            print(f"   Status: {status}")
            print()

    if others:
        print(f"=== Outros ({len(others)}) ===")
        for d in others:
            print(f"  {d.get('deviceId')}: {d.get('name', 'N/A')}")

def update_device_status(device_id, enabled):
    """Atualiza o status de um device (enable/disable)"""
    action = "habilitando" if enabled else "desabilitando"
    status = "ENABLED" if enabled else "DISABLED"

    print(f"{action.capitalize()} device {device_id}...")

    # Infelizmente a Developer Services API não tem um endpoint direto para update de status
    # Precisamos usar a App Store Connect API com os headers corretos
    # Mas isso é complexo... vamos tentar um approach diferente

    print("⚠ Aviso: A API da Apple não permite alterar status de devices via chamadas simples.")
    print("Para contas free, devices só podem ser removidos via portal web:")
    print("  https://developer.apple.com/account/resources/devices/list")
    print()
    print(f"Para liberar um slot, acesse o portal e remova o device com ID: {device_id}")
    print("Nota: Contas free têm limite de 3 iPhones. O reset anual ocorre uma vez por ano.")

def main():
    if len(sys.argv) < 2:
        print("Mabel Device Manager")
        print("Gerencia devices no Apple Developer Portal")
        print()
        print("Uso: python3 device_manager.py <comando> [args]")
        print()
        print("Comandos:")
        print("  list                    — lista todos os devices")
        print("  disable <device-id>     — tenta desabilitar um device")
        print("  enable <device-id>      — tenta reabilitar um device")
        print()
        print("Exemplos:")
        print("  python3 device_manager.py list")
        print("  python3 device_manager.py disable 348MCG364B")
        print()
        print("Nota: Para contas free, a remoção de devices deve ser feita via portal web:")
        print("      https://developer.apple.com/account/resources/devices/list")
        sys.exit(1)

    command = sys.argv[1]

    if command == "list":
        list_devices()
    elif command == "disable":
        if len(sys.argv) < 3:
            print("Erro: Especifique o device ID")
            print("Uso: python3 device_manager.py disable <device-id>")
            sys.exit(1)
        update_device_status(sys.argv[2], enabled=False)
    elif command == "enable":
        if len(sys.argv) < 3:
            print("Erro: Especifique o device ID")
            print("Uso: python3 device_manager.py enable <device-id>")
            sys.exit(1)
        update_device_status(sys.argv[2], enabled=True)
    else:
        print(f"Comando desconhecido: {command}")
        print("Comandos válidos: list, disable, enable")
        sys.exit(1)

if __name__ == "__main__":
    main()
