using Mabel.Core.Ports;

namespace Mabel.Core.Tests.Fakes;

public sealed class FakeShellExecutor : IShellExecutor
{
    private readonly Dictionary<string, ShellResult> _results = new();
    private readonly HashSet<string> _commands = new();
    private readonly List<string> _history = new();

    /// <summary>Recorded commands executed via Run or RunPassthrough.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>Register a command that exists on PATH.</summary>
    public FakeShellExecutor WithCommand(string name)
    {
        _commands.Add(name);
        return this;
    }

    /// <summary>Register a canned result for a command prefix.</summary>
    public FakeShellExecutor WithResult(string commandPrefix, int exitCode, string output = "", string error = "")
    {
        _results[commandPrefix] = new ShellResult(exitCode, output, error);
        return this;
    }

    public ShellResult Run(string command, string? workingDir = null)
    {
        _history.Add(command);

        // Check exact match first, then prefix match
        if (_results.TryGetValue(command, out var exact))
            return exact;

        foreach (var (prefix, result) in _results)
            if (command.StartsWith(prefix))
                return result;

        // Default: command not found
        return new ShellResult(127, "", $"command not found: {command}");
    }

    public int RunPassthrough(string command, string? workingDir = null)
    {
        _history.Add(command);
        return Run(command, workingDir).ExitCode;
    }

    public bool CommandExists(string command) => _commands.Contains(command);

    public string? GetVersion(string command, string versionFlag = "--version")
    {
        var r = Run($"{command} {versionFlag}");
        return r.Success && !string.IsNullOrWhiteSpace(r.Output) ? r.Output.Split('\n')[0].Trim() : null;
    }
}
