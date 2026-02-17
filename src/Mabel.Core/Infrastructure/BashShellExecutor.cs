using System.Diagnostics;
using Mabel.Core.Ports;

namespace Mabel.Core.Infrastructure;

public sealed class BashShellExecutor : IShellExecutor
{
    /// <summary>
    /// Escapes a string for safe use as a bash argument using single-quote wrapping.
    /// This prevents all shell metacharacter interpretation ($, `, \, !, etc.).
    /// </summary>
    private static string EscapeBashArg(string arg)
        => "'" + arg.Replace("'", "'\\''") + "'";

    public ShellResult Run(string command, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c {EscapeBashArg(command)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDir is not null) psi.WorkingDirectory = workingDir;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: /bin/bash -c ...");

        // Read stderr async to avoid deadlock when pipe buffer fills
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return new(proc.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    public int RunPassthrough(string command, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c {EscapeBashArg(command)}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDir is not null) psi.WorkingDirectory = workingDir;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: /bin/bash -c ...");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    public bool CommandExists(string command) => Run($"command -v {command}").Success;

    public string? GetVersion(string command, string versionFlag = "--version")
    {
        var r = Run($"{command} {versionFlag} 2>/dev/null");
        return r.Success && !string.IsNullOrWhiteSpace(r.Output) ? r.Output.Split('\n')[0].Trim() : null;
    }
}
