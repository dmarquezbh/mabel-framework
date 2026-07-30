using Mabel.Core.Ports;

namespace Mabel.Core.Features.Deploy;

/// <summary>
/// Detecta se estamos rodando num Mac de verdade com um Xcode.app completo
/// instalado (nao so as Command Line Tools). Quando true, build/deploy iOS
/// pode usar xcodebuild/xcrun devicectl nativamente em vez do xtool — que
/// existe especificamente pra permitir dev iOS SEM Mac (Linux/WSL).
/// </summary>
public sealed class XcodeEnvironment
{
    private readonly IShellExecutor _shell;
    public XcodeEnvironment(IShellExecutor shell) => _shell = shell;

    /// <summary>
    /// True quando `xcode-select -p` aponta pra um Xcode.app real
    /// (termina em "Xcode.app/Contents/Developer"), e nao so pras Command Line
    /// Tools (que terminam em "/Library/Developer/CommandLineTools"). O CLT
    /// sozinho nao tem xcodebuild com suporte a device iOS nem devicectl.
    /// </summary>
    public bool IsNativeXcodeMac()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        if (!_shell.CommandExists("xcodebuild")) return false;

        var r = _shell.Run("xcode-select -p");
        return r.Success && r.Output.TrimEnd().EndsWith("Xcode.app/Contents/Developer", StringComparison.Ordinal);
    }
}
