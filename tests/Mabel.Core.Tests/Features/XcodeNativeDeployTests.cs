using Mabel.Core.Features.Deploy;
using Mabel.Core.Tests.Fakes;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class XcodeNativeDeployTests
{
    // JSON real capturado de `xcodebuild -list -json` contra o sample
    // hello-world-ios (scheme sintetizado pelo Xcode a partir do Package.swift).
    private const string RealListJson =
        """
        {
          "workspace" : {
            "name" : "mabel-xcode-test2",
            "schemes" : [
              "ios_app"
            ]
          }
        }
        """;

    [Fact]
    public void ParseScheme_ExtractsFirstWorkspaceScheme()
    {
        Assert.Equal("ios_app", XcodeNativeDeploy.ParseScheme(RealListJson));
    }

    [Fact]
    public void ParseScheme_ExtractsFirstProjectScheme_WhenNoWorkspace()
    {
        var json = """{ "project": { "schemes": ["MyApp"] } }""";
        Assert.Equal("MyApp", XcodeNativeDeploy.ParseScheme(json));
    }

    [Fact]
    public void ParseScheme_ReturnsNull_ForInvalidJson()
    {
        Assert.Null(XcodeNativeDeploy.ParseScheme("not json"));
    }

    [Fact]
    public void ParseScheme_ReturnsNull_WhenNoSchemesListed()
    {
        Assert.Null(XcodeNativeDeploy.ParseScheme("""{ "workspace": { "schemes": [] } }"""));
    }

    [Fact]
    public void Execute_MissingIosAppDir_ReturnsError()
    {
        using var project = new TempProjectDir();
        var shell = new FakeShellExecutor();
        var fs = new FakeFileSystem(); // sem ios_app/ registrado no FakeFileSystem

        var deployer = new XcodeNativeDeploy(shell, fs);
        var result = deployer.Execute(project.Path);

        Assert.False(result.Success);
        Assert.Contains("ios_app", result.Error);
    }

    [Fact]
    public void Execute_NoDeviceFound_ReturnsError()
    {
        using var project = new TempProjectDir();
        var iosDir = Path.Combine(project.Path, "ios_app");
        Directory.CreateDirectory(iosDir);

        var shell = new FakeShellExecutor(); // devicectl nao configurado -> lista vazia
        var fs = new FakeFileSystem().WithDirectory(iosDir);

        var deployer = new XcodeNativeDeploy(shell, fs);
        var result = deployer.Execute(project.Path);

        Assert.False(result.Success);
        Assert.Contains("Nenhum device", result.Error);
    }

    // WasmResourceBundler.Prepare mexe em disco real (Directory.CreateDirectory)
    // mesmo em teste, entao os testes de Execute usam um diretorio temp real
    // pra nao colidir entre execucoes e pra limpar depois.
    private sealed class TempProjectDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mabel-xcode-deploy-test-{Guid.NewGuid():N}");

        public TempProjectDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
