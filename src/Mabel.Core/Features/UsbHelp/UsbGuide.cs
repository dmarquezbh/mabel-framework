namespace Mabel.Core.Features.UsbHelp;

/// <summary>
/// Texto de ajuda para configurar USB de dispositivos fisicos.
/// Retorna instrucoes contextuais baseadas no ambiente detectado (WSL, Linux, macOS)
/// e na plataforma alvo (iOS, Android).
/// </summary>
public static class UsbGuide
{
    // ── iOS ────────────────────────────────────────────────────────────

    public static string IosOnWsl() =>
"""
  Conectando iPhone via USB no WSL
  ────────────────────────────────

  O WSL nao tem acesso direto ao USB. Voce precisa usar o usbipd-win
  para encaminhar o dispositivo do Windows para o Linux.

  1. No Windows (PowerShell como Admin), instale o usbipd:

     winget install usbipd

  2. Conecte o iPhone e confie no computador (toque "Confiar" no iPhone).

  3. No PowerShell (Admin), liste os dispositivos USB:

     usbipd list

     Procure por "Apple Mobile Device" ou "iPhone". Anote o BUSID (ex: 2-3).

  4. Vincule o dispositivo (so precisa fazer uma vez):

     usbipd bind --busid 2-3

  5. Encaminhe para o WSL:

     usbipd attach --wsl --busid 2-3

  6. No WSL, verifique se apareceu:

     lsusb | grep Apple
     idevice_id -l

  7. Se idevice_id nao retornar nada, reinicie o usbmuxd:

     sudo systemctl restart usbmuxd
     # ou: sudo usbmuxd -f -v  (modo debug)

  Dica: toda vez que desconectar/reconectar o cabo, repita o passo 5.
  O bind (passo 4) persiste entre reboots.

  Pacotes necessarios no WSL:
    sudo apt install usbmuxd libimobiledevice-utils libimobiledevice6
    (o 'mabel setup' ja instala esses pacotes)
""";

    public static string IosOnLinux() =>
"""
  Conectando iPhone via USB no Linux
  ──────────────────────────────────

  1. Instale os pacotes necessarios:

     sudo apt install usbmuxd libimobiledevice-utils libimobiledevice6

     (o 'mabel setup' ja instala esses pacotes)

  2. Conecte o iPhone via cabo USB.

  3. Desbloqueie o iPhone e toque "Confiar" quando perguntado.

  4. Verifique a conexao:

     idevice_id -l          # lista UDIDs dos dispositivos
     ideviceinfo             # mostra info detalhada

  5. Se nao aparecer nada:

     # Reinicie o daemon:
     sudo systemctl restart usbmuxd

     # Ou rode em modo debug para ver erros:
     sudo usbmuxd -f -v

     # Verifique se o USB esta visivel:
     lsusb | grep Apple

  6. Permissoes: se der erro de permissao, adicione seu usuario ao grupo plugdev:

     sudo usermod -aG plugdev $USER
     # Faca logout/login para aplicar

  Nota: o iPhone precisa estar desbloqueado durante o deploy.
""";

    public static string IosOnMac() =>
"""
  Conectando iPhone via USB no macOS
  ──────────────────────────────────

  No macOS o suporte a iPhone via USB ja vem integrado.

  1. Conecte o iPhone via cabo USB (ou USB-C).

  2. Desbloqueie o iPhone e toque "Confiar" quando perguntado.

  3. Se for a primeira vez, o Finder (ou iTunes no macOS antigo) vai
     pedir para confiar no computador — aceite.

  4. Verifique no terminal:

     xcrun xctrace list devices    # lista dispositivos disponiveis
     # ou, se tiver libimobiledevice instalado via Homebrew:
     idevice_id -l

  5. O xtool (usado pelo mabel deploy) detecta o iPhone automaticamente:

     xtool devices

  Nota: voce precisa do Xcode instalado para assinar e deployar apps iOS.
  Instale via App Store ou: xcode-select --install (apenas command line tools).
""";

    // ── Android ────────────────────────────────────────────────────────

    public static string AndroidOnWsl() =>
"""
  Conectando Android via USB no WSL
  ─────────────────────────────────

  Assim como o iPhone, o Android precisa do usbipd-win no WSL.

  1. No celular Android, habilite "Opcoes do Desenvolvedor":
     Configuracoes > Sobre o telefone > Numero da versao (toque 7 vezes)

  2. Habilite "Depuracao USB":
     Configuracoes > Opcoes do desenvolvedor > Depuracao USB

  3. No Windows (PowerShell como Admin):

     winget install usbipd            # se ainda nao instalou
     usbipd list                       # encontre o dispositivo Android
     usbipd bind --busid <BUSID>       # vincule (ex: 1-4)
     usbipd attach --wsl --busid <BUSID>

  4. No WSL, instale o adb:

     sudo apt install adb
     # ou: sudo apt install android-tools-adb

  5. Verifique:

     adb devices

     O celular deve aparecer com status "device".
     Se aparecer "unauthorized", aceite a conexao na tela do celular.

  6. Se o adb nao detectar, mate e reinicie o server:

     adb kill-server
     adb start-server
     adb devices

  Dica: repita 'usbipd attach' toda vez que reconectar o cabo.
""";

    public static string AndroidOnLinux() =>
"""
  Conectando Android via USB no Linux
  ───────────────────────────────────

  1. No celular Android, habilite "Opcoes do Desenvolvedor":
     Configuracoes > Sobre o telefone > Numero da versao (toque 7 vezes)

  2. Habilite "Depuracao USB":
     Configuracoes > Opcoes do desenvolvedor > Depuracao USB

  3. Instale o adb:

     sudo apt install adb
     # ou: sudo apt install android-tools-adb

  4. Conecte o celular via USB.

  5. Verifique:

     adb devices

     O celular deve aparecer com status "device".
     Se aparecer "unauthorized", aceite a conexao na tela do celular.

  6. Se nao aparecer:

     # Verifique se o USB esta visivel:
     lsusb

     # Reinicie o server:
     adb kill-server && adb start-server && adb devices

     # Regras udev (para o Linux reconhecer o dispositivo sem root):
     # Crie /etc/udev/rules.d/51-android.rules com:
     #   SUBSYSTEM=="usb", ATTR{idVendor}=="XXXX", MODE="0666", GROUP="plugdev"
     # (substitua XXXX pelo vendor ID do seu celular, visivel com lsusb)

     sudo udevadm control --reload-rules
     sudo udevadm trigger

  7. Permissoes:

     sudo usermod -aG plugdev $USER
     # Faca logout/login para aplicar
""";

    public static string AndroidOnMac() =>
"""
  Conectando Android via USB no macOS
  ───────────────────────────────────

  1. No celular Android, habilite "Opcoes do Desenvolvedor":
     Configuracoes > Sobre o telefone > Numero da versao (toque 7 vezes)

  2. Habilite "Depuracao USB":
     Configuracoes > Opcoes do desenvolvedor > Depuracao USB

  3. Instale o adb via Homebrew:

     brew install android-platform-tools

  4. Conecte o celular via USB e aceite a conexao no celular.

  5. Verifique:

     adb devices

  Se nao aparecer, experimente outro cabo USB (alguns cabos so carregam,
  nao transmitem dados).
""";

    // ── Seletor contextual ─────────────────────────────────────────────

    public enum Environment { Wsl, Linux, Mac }

    public static Environment DetectEnvironment(bool isWsl)
    {
        if (isWsl) return Environment.Wsl;
        if (OperatingSystem.IsMacOS()) return Environment.Mac;
        return Environment.Linux;
    }

    /// <summary>
    /// Retorna as instrucoes relevantes para o ambiente atual.
    /// Se showAll=true, mostra iOS + Android. Senao, so a plataforma pedida.
    /// </summary>
    public static string GetHelp(Environment env, bool showIos = true, bool showAndroid = true)
    {
        var parts = new List<string>();

        if (showIos)
        {
            parts.Add(env switch
            {
                Environment.Wsl   => IosOnWsl(),
                Environment.Mac   => IosOnMac(),
                _                 => IosOnLinux(),
            });
        }

        if (showAndroid)
        {
            parts.Add(env switch
            {
                Environment.Wsl   => AndroidOnWsl(),
                Environment.Mac   => AndroidOnMac(),
                _                 => AndroidOnLinux(),
            });
        }

        return string.Join("\n", parts);
    }
}
