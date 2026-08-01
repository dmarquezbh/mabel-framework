using Mabel.Core.Features.Apple;
using Mabel.Core.Tests.Fakes;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class AppleDevicesTests
{
    [Fact]
    public void ResolveXtool_UsesMabelXtoolEnvWhenSet()
    {
        var resolved = AppleDevices.ResolveXtool(
            mabelXtoolEnv: "/custom/path/xtool-wrapper.sh",
            fileExists: _ => false,
            candidates: ["/would/never/be/checked/xtool"]);

        Assert.Equal("/custom/path/xtool-wrapper.sh", resolved);
    }

    [Fact]
    public void ResolveXtool_FallsBackToPatchedBuildCandidateWhenEnvUnset()
    {
        // Simula o build patcheado do macOS existindo no segundo candidato
        // (release ausente, debug presente) — ver docs/gerenciar-devices-apple-xtool-macos.md.
        var resolved = AppleDevices.ResolveXtool(
            mabelXtoolEnv: null,
            fileExists: path => path.EndsWith("debug/xtool"),
            candidates: ["/home/user/xtool-src-macos/.build/release/xtool", "/home/user/xtool-src-macos/.build/debug/xtool"]);

        Assert.Equal("/home/user/xtool-src-macos/.build/debug/xtool", resolved);
    }

    [Fact]
    public void ResolveXtool_FallsBackToPathXtoolWhenNoCandidateExists()
    {
        var resolved = AppleDevices.ResolveXtool(
            mabelXtoolEnv: null,
            fileExists: _ => false,
            candidates: ["/nope/xtool"]);

        Assert.Equal("xtool", resolved);
    }

    [Fact]
    public void PatchedXtoolCandidates_EmptyOffMacOS()
    {
        if (OperatingSystem.IsMacOS()) return; // candidatos automaticos so existem no macOS

        Assert.Empty(AppleDevices.PatchedXtoolCandidates());
    }

    [Fact]
    public void PatchedXtoolCandidates_PointsAtXtoolSrcMacosOnMacOS()
    {
        if (!OperatingSystem.IsMacOS()) return; // deteccao real de SO so faz sentido rodando num Mac

        var candidates = AppleDevices.PatchedXtoolCandidates().ToList();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Contains("xtool-src-macos", c));
    }

    // Comando fixo nos testes de instância — evita que o teste dependa do
    // estado real do filesystem (ResolveXtool() sem args auto-detecta um
    // build patcheado se ~/xtool-src-macos existir na máquina que roda o teste).
    [Fact]
    public void XtoolAvailable_DelegatesToShellCommandExists()
    {
        var shell = new FakeShellExecutor().WithCommand("xtool");
        var apple = new AppleDevices(shell, "xtool");

        Assert.True(apple.XtoolAvailable());
    }

    [Fact]
    public void SupportsSetStatus_TrueWhenHelpMentionsSetStatus()
    {
        var shell = new FakeShellExecutor()
            .WithResult("xtool ds devices --help", 0, "SUBCOMMANDS:\n  list\n  set-status");
        var apple = new AppleDevices(shell, "xtool");

        Assert.True(apple.SupportsSetStatus());
    }

    [Fact]
    public void SupportsSetStatus_FalseForStockXtool()
    {
        var shell = new FakeShellExecutor()
            .WithResult("xtool ds devices --help", 0, "SUBCOMMANDS:\n  list");
        var apple = new AppleDevices(shell, "xtool");

        Assert.False(apple.SupportsSetStatus());
    }

    [Fact]
    public void SetStatus_SuccessWhenOutputStartsWithOk()
    {
        var shell = new FakeShellExecutor()
            .WithResult("xtool ds devices set-status ABC123 DISABLED", 0, "OK: ABC123 name=iPhone status=DISABLED");
        var apple = new AppleDevices(shell, "xtool");

        var result = apple.SetStatus("ABC123", enabled: false);

        Assert.True(result.Success);
        Assert.Contains("DISABLED", result.Message);
    }
}
