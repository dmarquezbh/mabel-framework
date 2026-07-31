using System.Text.Json;
using Mabel.Core.Features.Devices;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Deploy;

/// <summary>
/// Build + deploy nativo via Xcode, pra quando o mabel roda num Mac de
/// verdade com Xcode.app instalado (ver <see cref="XcodeEnvironment.IsNativeXcodeMac"/>).
/// Alternativa ao <see cref="DeployToDevice"/> (que usa `xtool dev`) — o xtool
/// continua sendo o caminho certo pra Linux/WSL, onde nao ha Xcode. Aqui:
///
///   - build:   xcodebuild (CODE_SIGN_STYLE=Automatic, -allowProvisioningUpdates)
///   - bundle:  Package.swift puro nunca vira .app via `xcodebuild build` sozinho
///              (sem .xcodeproj nao ha target Application) — <see cref="AssembleAppBundle"/>
///              monta o bundle manualmente a partir do executavel Mach-O gerado
///   - sign:    <see cref="SignAppBundle"/> (codesign com a identidade Apple
///              Development disponivel no keychain)
///   - listar:  xcrun devicectl list devices (substitui `xtool devices`)
///   - deploy:  xcrun devicectl device install app + device process launch
///              (substitui `xtool dev`)
///
/// Requer login com Apple ID em Xcode > Settings > Accounts (com certificado
/// "Apple Development" gerado em Manage Certificates) pra o signing funcionar —
/// isso nao da pra automatizar sem UI, entao so verificamos e reportamos o erro
/// (nao inventamos workaround). Mesmo com identidade valida, instalar num device
/// fisico (nao-simulador) exige tambem um provisioning profile embutido listando
/// o UDID do device — a montagem automatica desse profile via API do Apple
/// Developer Portal e um passo hoje so disponivel dentro do Xcode/IDE (nao existe
/// via `codesign`/`security` puros); <see cref="SignAppBundle"/> assina o bundle
/// mas nao resolve profile, entao a instalacao pode falhar por profile ausente
/// mesmo com a assinatura correta — o erro do `devicectl` aparece integralmente
/// (passthrough) nesse caso.
/// </summary>
public sealed class XcodeNativeDeploy
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public XcodeNativeDeploy(IShellExecutor shell, IFileSystem fs) { _shell = shell; _fs = fs; }

    public record Result(bool Success, string? Error);

    /// <summary>Lista devices iOS via <see cref="DevicectlDeviceLister"/>.</summary>
    public IReadOnlyList<DeviceInfo> ListDevices() => DevicectlDeviceLister.List(_shell);

    /// <summary>
    /// Build (xcodebuild) + install (devicectl device install app) + launch
    /// (devicectl device process launch) no device indicado por
    /// <paramref name="deviceUdid"/>, ou no primeiro device iOS encontrado
    /// (<see cref="ListDevices"/>) se for null.
    /// </summary>
    public Result Execute(string projectPath, string? deviceUdid = null, string configuration = "Debug")
    {
        projectPath = Path.GetFullPath(projectPath);
        var iosDir = Path.Combine(projectPath, "ios_app");
        if (!_fs.DirectoryExists(iosDir))
            return new(false, $"'ios_app/' nao encontrado em {projectPath}");

        var prep = WasmResourceBundler.Prepare(_shell, projectPath, iosDir);
        if (prep is not null) return new(false, prep);

        if (deviceUdid is null)
        {
            var devices = ListDevices();
            if (devices.Count == 0)
                return new(false, "Nenhum device iOS encontrado (xcrun devicectl list devices). Conecte um iPhone via USB e confie neste Mac.");
            deviceUdid = devices[0].Id;
        }

        var scheme = ResolveScheme(iosDir);
        if (scheme is null)
            return new(false, "Nao foi possivel resolver o scheme do Xcode (xcodebuild -list -json). Verifique o Package.swift em ios_app/.");

        var derivedData = Path.Combine(iosDir, ".xcode-build");
        var buildCmd = $"xcodebuild build -scheme \"{scheme}\" -destination \"id={deviceUdid}\" " +
                       $"-configuration {configuration} -derivedDataPath \"{derivedData}\" " +
                       "-allowProvisioningUpdates CODE_SIGN_STYLE=Automatic";

        var buildRc = _shell.RunPassthrough(buildCmd, workingDir: iosDir);
        if (buildRc != 0)
            return new(false,
                $"xcodebuild saiu com codigo {buildRc}. Causas comuns: nenhuma conta Apple ID logada " +
                "(Xcode > Settings > Accounts — nao da pra automatizar sem UI), plataforma iOS nao " +
                "instalada (xcodebuild -downloadPlatform iOS / Xcode > Settings > Components), ou " +
                "provisioning profile. Rode o comando acima direto em ios_app/ pra ver o erro completo.");

        var appPath = FindBuiltApp(derivedData, configuration);
        if (appPath is null)
        {
            // Package.swift puro (sem .xcodeproj) nunca produz um bundle .app via
            // `xcodebuild build` sozinho — so gera o executavel Mach-O nu (ver
            // docs/mabel-xcode-native-deploy.md). Montamos o .app manualmente aqui.
            var assembled = AssembleAppBundle(derivedData, configuration, iosDir, scheme);
            if (assembled.Error is not null) return new(false, assembled.Error);
            appPath = assembled.AppPath;
        }

        var signRc = SignAppBundle(appPath!);
        if (signRc is not null) return new(false, signRc);

        var installRc = _shell.RunPassthrough($"xcrun devicectl device install app --device {deviceUdid} \"{appPath}\"");
        if (installRc != 0)
            return new(false, $"'xcrun devicectl device install app' saiu com codigo {installRc}.");

        var bundleId = ReadBundleId(iosDir) ?? Path.GetFileNameWithoutExtension(appPath);
        var launchRc = _shell.RunPassthrough($"xcrun devicectl device process launch --device {deviceUdid} {bundleId}");
        if (launchRc != 0)
            return new(false,
                $"App instalado no device, mas 'xcrun devicectl device process launch' saiu com codigo {launchRc}. " +
                "Abra o app manualmente na tela do device.");

        return new(true, null);
    }

    private string? ResolveScheme(string iosDir)
    {
        var r = _shell.Run("xcodebuild -list -json", workingDir: iosDir);
        return r.Success ? ParseScheme(r.Output) : null;
    }

    /// <summary>
    /// Extrai o primeiro scheme do JSON de `xcodebuild -list -json`, isolado
    /// do disparo real do processo pra dar pra testar com um fixture de texto.
    /// </summary>
    public static string? ParseScheme(string listJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("workspace", out var ws) &&
                ws.TryGetProperty("schemes", out var schemes) &&
                schemes.GetArrayLength() > 0)
                return schemes[0].GetString();

            if (doc.RootElement.TryGetProperty("project", out var proj) &&
                proj.TryGetProperty("schemes", out var pschemes) &&
                pschemes.GetArrayLength() > 0)
                return pschemes[0].GetString();
        }
        catch (JsonException)
        {
            // xcodebuild -list -json as vezes escreve avisos no stdout em builds
            // antigos; sem JSON valido, deixa o chamador reportar a falha.
        }

        return null;
    }

    private static string? FindBuiltApp(string derivedData, string configuration)
    {
        var productsDir = Path.Combine(derivedData, "Build", "Products");
        if (!Directory.Exists(productsDir)) return null;

        foreach (var candidate in Directory.GetDirectories(productsDir, $"{configuration}-iphoneos*"))
        {
            var app = Directory.GetDirectories(candidate, "*.app").FirstOrDefault();
            if (app is not null) return app;
        }

        return null;
    }

    private string? ReadBundleId(string iosDir)
    {
        var xtoolYml = Path.Combine(iosDir, "xtool.yml");
        if (!_fs.FileExists(xtoolYml)) return null;

        foreach (var line in _fs.ReadAllText(xtoolYml).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("bundleID:", StringComparison.OrdinalIgnoreCase))
                return trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
        }

        return null;
    }

    public record AssembleResult(string? AppPath, string? Error);

    /// <summary>
    /// Monta manualmente um bundle `&lt;scheme&gt;.app/` a partir do executavel
    /// Mach-O nu que `xcodebuild build` produz pra um `Package.swift` puro (sem
    /// .xcodeproj — nao ha target Application pra gerar o wrapper sozinho).
    /// Copia o executavel + o resource bundle do SwiftPM (se existir) pra dentro
    /// do `.app` e escreve um `Info.plist` minimo. Nao assina — ver <see cref="SignAppBundle"/>.
    /// </summary>
    private static AssembleResult AssembleAppBundle(string derivedData, string configuration, string iosDir, string scheme)
    {
        var productsDir = Path.Combine(derivedData, "Build", "Products", $"{configuration}-iphoneos");
        var executablePath = Path.Combine(productsDir, scheme);
        if (!File.Exists(executablePath))
            return new(null, $"Executavel nao encontrado em {executablePath} (build pode ter gerado um " +
                              "objeto relocavel .o em vez de um Mach-O — confira se o produto em Package.swift " +
                              "e .executable/.executableTarget, nao .library/.target).");

        var appDir = Path.Combine(productsDir, $"{scheme}.app");
        if (Directory.Exists(appDir)) Directory.Delete(appDir, recursive: true);
        Directory.CreateDirectory(appDir);

        File.Copy(executablePath, Path.Combine(appDir, scheme), overwrite: true);

        // Resource bundle gerado pelo SwiftPM pros recursos declarados no target
        // (ex.: Resources/ do scaffold) segue a convencao "<pacote>_<target>.bundle".
        var resourceBundle = Path.Combine(productsDir, $"{scheme}_{scheme}.bundle");
        if (Directory.Exists(resourceBundle))
            CopyDirectoryRecursive(resourceBundle, Path.Combine(appDir, Path.GetFileName(resourceBundle)));

        var xtoolYml = Path.Combine(iosDir, "xtool.yml");
        string? bundleId = null;
        if (File.Exists(xtoolYml))
        {
            foreach (var line in File.ReadAllLines(xtoolYml))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("bundleID:", StringComparison.OrdinalIgnoreCase))
                {
                    bundleId = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    break;
                }
            }
        }
        bundleId ??= $"com.mabel.{scheme}";

        File.WriteAllText(Path.Combine(appDir, "Info.plist"), BuildInfoPlist(scheme, bundleId));

        return new(appDir, null);
    }

    /// <summary>
    /// Gera o conteudo do Info.plist minimo pra um bundle .app iOS instalavel —
    /// isolado da montagem real do bundle pra dar pra testar sem tocar disco.
    /// </summary>
    public static string BuildInfoPlist(string executableName, string bundleId) =>
$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>{executableName}</string>
    <key>CFBundleIdentifier</key>
    <string>{bundleId}</string>
    <key>CFBundleName</key>
    <string>{executableName}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSRequiresIPhoneOS</key>
    <true/>
    <key>MinimumOSVersion</key>
    <string>15.0</string>
    <key>UIRequiredDeviceCapabilities</key>
    <array>
        <string>arm64</string>
    </array>
</dict>
</plist>
""";

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    /// <summary>
    /// Assina o bundle com a primeira identidade "Apple Development" valida do
    /// keychain (`security find-identity -v -p codesigning`). Retorna null em
    /// sucesso, ou uma mensagem de erro acionavel. Mesmo com assinatura ok, a
    /// instalacao no device pode falhar por falta de provisioning profile
    /// embutido (ver doc da classe) — isso aparece como erro separado do
    /// `devicectl device install`, nao daqui.
    /// </summary>
    private string? SignAppBundle(string appPath)
    {
        var identityResult = _shell.Run("security find-identity -v -p codesigning");
        var identity = ParseFirstCodesigningIdentity(identityResult.Output);
        if (identity is null)
            return "Nenhuma identidade de assinatura valida encontrada (`security find-identity -v -p codesigning` " +
                   "retornou vazio). Confirme em Xcode > Settings > Accounts que ha uma conta Apple ID logada E " +
                   "um certificado 'Apple Development' gerado (Manage Certificates > +). Se o certificado existe " +
                   "mas mesmo assim nao aparece como identidade valida, confira se o certificado intermediario " +
                   "'Apple Worldwide Developer Relations Certification Authority' esta instalado no keychain " +
                   "(security find-certificate -c \"Apple Worldwide Developer Relations\") — sem ele a cadeia de " +
                   "confianca fica invalida e o certificado nao conta como identidade utilizavel.";

        var signRc = _shell.RunPassthrough($"codesign --force --sign \"{identity}\" \"{appPath}\"");
        if (signRc != 0)
            return $"'codesign' saiu com codigo {signRc} ao assinar {appPath}.";

        return null;
    }

    /// <summary>
    /// Extrai o nome da primeira identidade valida da saida de
    /// `security find-identity -v -p codesigning` (formato:
    /// `  1) &lt;SHA1&gt; "&lt;Nome da identidade&gt;"`), isolado pra testar sem
    /// depender do keychain real.
    /// </summary>
    public static string? ParseFirstCodesigningIdentity(string findIdentityOutput)
    {
        foreach (var line in findIdentityOutput.Split('\n'))
        {
            var start = line.IndexOf('"');
            var end = line.LastIndexOf('"');
            if (start >= 0 && end > start)
                return line[(start + 1)..end];
        }

        return null;
    }
}
