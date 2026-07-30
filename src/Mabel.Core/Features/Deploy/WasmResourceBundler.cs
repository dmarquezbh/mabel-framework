using Mabel.Core.Ports;

namespace Mabel.Core.Features.Deploy;

/// <summary>
/// Compila o WASM do web_app (Blazor WASM) e o embute em Sources/&lt;target&gt;/Resources
/// do host iOS, garantindo que o diretorio Resources exista e nao esteja vazio
/// (requisito do .copy("Resources") no Package.swift). Compartilhado entre os
/// fluxos de deploy — xtool (<see cref="DeployToDevice"/>) e Xcode nativo
/// (<see cref="XcodeNativeDeploy"/>) — pra nao duplicar a mesma logica.
/// </summary>
public static class WasmResourceBundler
{
    public static string? Prepare(IShellExecutor shell, string projectPath, string iosDir)
    {
        var webApp = Path.Combine(projectPath, "web_app");
        if (Directory.Exists(webApp))
        {
            var rc = shell.RunPassthrough("dotnet build -c Release", workingDir: webApp);
            if (rc != 0)
                return "Falha ao compilar o WASM do web_app (verifique o workload wasm-tools: dotnet workload install wasm-tools).";
        }

        // Localiza o .wasm compilado (mesmos candidatos do dev server).
        string? wasm = null;
        foreach (var rel in new[]
        {
            Path.Combine("bin", "Release", "net10.0", "wwwroot", "_framework"),
            Path.Combine("bin", "Release", "net10.0"),
            Path.Combine("bin", "Debug", "net10.0", "wwwroot", "_framework"),
            Path.Combine("bin", "Debug", "net10.0"),
        })
        {
            var d = Path.Combine(webApp, rel);
            if (!Directory.Exists(d)) continue;
            wasm = Directory.GetFiles(d, "*.wasm").FirstOrDefault();
            if (wasm is not null) break;
        }

        // Resources fica sob Sources/<target>/Resources (target unico do host).
        var sourcesRoot = Path.Combine(iosDir, "Sources");
        var target = Directory.Exists(sourcesRoot)
            ? Directory.GetDirectories(sourcesRoot).FirstOrDefault()
            : null;
        var resources = Path.Combine(target ?? Path.Combine(iosDir, "Sources", "ios_app"), "Resources");
        Directory.CreateDirectory(resources);

        if (wasm is not null)
            File.Copy(wasm, Path.Combine(resources, "app.wasm"), overwrite: true);
        else
            // Sem WASM (host placeholder ainda nao carrega wasm) — placeholder p/ o .copy nao quebrar.
            File.WriteAllText(Path.Combine(resources, ".keep"), "placeholder para .copy(Resources)\n");

        return null;
    }
}
