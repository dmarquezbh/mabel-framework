using Mabel.Core.Ports;

namespace Mabel.Core.Features.Apple;

// Manage devices registered on the Apple Developer account via xtool.
// Enable/disable requires an xtool build with the `ds devices set-status`
// subcommand (see docs/gerenciar-devices-apple-xtool.md). The Apple API has
// no device delete — DISABLED frees the provisioning slot just the same.
public sealed class AppleDevices
{
    private readonly IShellExecutor _shell;
    private readonly string _xtool;

    // Resolve uma vez na construção (via ResolveXtool()) — mantém os métodos
    // de instância hermeticos e testáveis com um comando fixo.
    public AppleDevices(IShellExecutor shell) : this(shell, ResolveXtool()) { }

    public AppleDevices(IShellExecutor shell, string xtool)
    {
        _shell = shell;
        _xtool = xtool;
    }

    public record Result(bool Success, string Message);

    // Override the xtool invocation with MABEL_XTOOL (full command prefix,
    // e.g. a wrapper script that sets LD_LIBRARY_PATH for a local build).
    // Sem a env var, tenta achar um build patcheado (subcomando `set-status`)
    // em locais conhecidos antes de cair no `xtool` de PATH.
    public static string ResolveXtool() =>
        ResolveXtool(Environment.GetEnvironmentVariable("MABEL_XTOOL"), File.Exists, PatchedXtoolCandidates());

    // Testável: recebe a env var, o probe de existência e os candidatos já resolvidos.
    public static string ResolveXtool(string? mabelXtoolEnv, Func<string, bool> fileExists, IEnumerable<string> candidates)
    {
        if (mabelXtoolEnv is { Length: > 0 }) return mabelXtoolEnv;

        foreach (var candidate in candidates)
            if (fileExists(candidate)) return candidate;

        return "xtool";
    }

    // Auto-detecção só no macOS: lá o build nativo (`swift build --product xtool`)
    // não precisa de LD_LIBRARY_PATH/libxadi (usa OmnisetteADIProvider via rede,
    // ver docs/gerenciar-devices-apple-xtool-macos.md). No Linux/WSL o binário
    // patcheado só funciona atrás do wrapper com LD_LIBRARY_PATH — por isso lá
    // continua exigindo MABEL_XTOOL explícito, sem candidato automático.
    public static IEnumerable<string> PatchedXtoolCandidates()
    {
        if (!OperatingSystem.IsMacOS()) yield break;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "xtool-src-macos", ".build", "release", "xtool");
        yield return Path.Combine(home, "xtool-src-macos", ".build", "debug", "xtool");
    }

    public bool XtoolAvailable() => _shell.CommandExists(_xtool.Split(' ')[0]);

    // Stock xtool (<= 1.17) only ships `ds devices list`; set-status needs the
    // patched build documented in docs/gerenciar-devices-apple-xtool.md.
    public bool SupportsSetStatus()
    {
        var r = _shell.Run($"{_xtool} ds devices --help");
        return r.Success && r.Output.Contains("set-status");
    }

    public int List() => _shell.RunPassthrough($"{_xtool} ds devices list");

    public Result SetStatus(string deviceId, bool enabled)
    {
        var status = enabled ? "ENABLED" : "DISABLED";
        var r = _shell.Run($"{_xtool} ds devices set-status {deviceId} {status}");
        var output = string.IsNullOrWhiteSpace(r.Output) ? r.Error : r.Output;
        return new(r.Success && output.Contains("OK:"), output.Trim());
    }
}
