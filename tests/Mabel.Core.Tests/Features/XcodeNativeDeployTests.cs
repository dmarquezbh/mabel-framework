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

    [Fact]
    public void BuildInfoPlist_ContemChavesObrigatorias()
    {
        var plist = XcodeNativeDeploy.BuildInfoPlist("ios_app", "com.pjus.mabeltest");

        Assert.Contains("<key>CFBundleExecutable</key>", plist);
        Assert.Contains("<string>ios_app</string>", plist);
        Assert.Contains("<key>CFBundleIdentifier</key>", plist);
        Assert.Contains("<string>com.pjus.mabeltest</string>", plist);
        Assert.Contains("<key>CFBundlePackageType</key>", plist);
        Assert.Contains("<string>APPL</string>", plist);
        Assert.Contains("<key>LSRequiresIPhoneOS</key>", plist);
    }

    [Fact]
    public void ParseFirstCodesigningIdentity_ExtraiNomeDaIdentidade()
    {
        var output = "  1) AB12CD34EF \"Apple Development: Daniel Marques (2ZPCXMP4E4)\"\n     1 valid identities found";
        Assert.Equal("Apple Development: Daniel Marques (2ZPCXMP4E4)", XcodeNativeDeploy.ParseFirstCodesigningIdentity(output));
    }

    [Fact]
    public void ParseFirstCodesigningIdentity_ReturnsNull_QuandoNenhumaIdentidade()
    {
        Assert.Null(XcodeNativeDeploy.ParseFirstCodesigningIdentity("     0 valid identities found"));
    }

    [Fact]
    public void Execute_MontaAssinaEInstalaAppBundle_QuandoXcodebuildSoProduzExecutavelNu()
    {
        using var project = new TempProjectDir();
        var iosDir = Path.Combine(project.Path, "ios_app");
        Directory.CreateDirectory(iosDir);

        // Simula o executavel Mach-O nu que `xcodebuild build` produz pra um
        // Package.swift .executable puro (sem .xcodeproj/target Application) —
        // exatamente o cenario documentado em docs/mabel-xcode-native-deploy.md.
        var productsDir = Path.Combine(iosDir, ".xcode-build", "Build", "Products", "Debug-iphoneos");
        Directory.CreateDirectory(productsDir);
        File.WriteAllText(Path.Combine(productsDir, "ios_app"), "fake-macho-executable");

        var shell = new FakeShellExecutor()
            .WithResult("xcodebuild -list -json", 0, RealListJson)
            .WithResult("xcodebuild build", 0)
            .WithResult("security find-identity -v -p codesigning", 0,
                "  1) AB12CD34EF \"Apple Development: Daniel Marques (2ZPCXMP4E4)\"\n     1 valid identities found")
            .WithResult("codesign --force --sign", 0)
            .WithResult("xcrun devicectl device install app", 0)
            .WithResult("xcrun devicectl device process launch", 0);
        var fs = new FakeFileSystem().WithDirectory(iosDir);

        var deployer = new XcodeNativeDeploy(shell, fs);
        var result = deployer.Execute(project.Path, deviceUdid: "FAKE-UDID-0001");

        Assert.True(result.Success, result.Error);

        var appDir = Path.Combine(productsDir, "ios_app.app");
        Assert.True(Directory.Exists(appDir));
        Assert.True(File.Exists(Path.Combine(appDir, "ios_app")));
        Assert.True(File.Exists(Path.Combine(appDir, "Info.plist")));
        Assert.Contains("com.mabel.ios_app", File.ReadAllText(Path.Combine(appDir, "Info.plist")));
        Assert.Contains(shell.History, cmd => cmd.StartsWith("codesign --force --sign \"Apple Development"));
    }

    [Fact]
    public void Execute_RetornaErroAcionavel_QuandoNenhumaIdentidadeDeAssinatura()
    {
        using var project = new TempProjectDir();
        var iosDir = Path.Combine(project.Path, "ios_app");
        Directory.CreateDirectory(iosDir);

        var productsDir = Path.Combine(iosDir, ".xcode-build", "Build", "Products", "Debug-iphoneos");
        Directory.CreateDirectory(productsDir);
        File.WriteAllText(Path.Combine(productsDir, "ios_app"), "fake-macho-executable");

        var shell = new FakeShellExecutor()
            .WithResult("xcodebuild -list -json", 0, RealListJson)
            .WithResult("xcodebuild build", 0)
            .WithResult("security find-identity -v -p codesigning", 0, "     0 valid identities found");
        var fs = new FakeFileSystem().WithDirectory(iosDir);

        var deployer = new XcodeNativeDeploy(shell, fs);
        var result = deployer.Execute(project.Path, deviceUdid: "FAKE-UDID-0001");

        Assert.False(result.Success);
        Assert.Contains("Nenhuma identidade de assinatura valida", result.Error);
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
