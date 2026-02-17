using Mabel.Core.Domain;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Doctor;

public record ToolStatus(string Name, string Description, bool Found, string? Version, string? Hint);
public record DiagnosticResult(IReadOnlyList<ToolStatus> Tools, bool PathConfigured, bool IsWsl, Platform Platforms);

public sealed class DiagnoseEnvironment
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public DiagnoseEnvironment(IShellExecutor shell, IFileSystem fs)
    {
        _shell = shell;
        _fs = fs;
    }

    public DiagnosticResult Execute(Platform platforms = Platform.All)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var tools = KnownTools.All
            .Where(t => (t.Platforms & platforms) != 0)
            .Select(t => Check(t, home))
            .ToList();

        return new(tools, CheckPath(home), CheckWsl(), platforms);
    }

    private ToolStatus Check(ToolRequirement req, string home)
    {
        var knownPaths = req.Name switch
        {
            "dotnet" => [Path.Combine(home, ".dotnet", "dotnet")],
            "swift"  => [Path.Combine(home, "swift", "usr", "bin", "swift")],
            _ => Array.Empty<string>(),
        };

        if (_shell.CommandExists(req.Name))
            return new(req.Name, req.Description, true, _shell.GetVersion(req.Name), null);

        foreach (var p in knownPaths)
            if (_fs.FileExists(p))
                return new(req.Name, req.Description, true, _shell.GetVersion(p), null);

        return new(req.Name, req.Description, false, null, req.InstallHint);
    }

    private bool CheckPath(string home)
    {
        var bashrc = Path.Combine(home, ".bashrc");
        return _fs.FileExists(bashrc) && _fs.ReadAllText(bashrc).Contains("# >>> mabel-framework >>>");
    }

    private bool CheckWsl()
    {
        try
        {
            return _shell.Run("uname -r").Output.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
