using Mabel.Core.Domain;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Scaffold;

public record ScaffoldRequest(string AppName, string BundleId, Platform Platforms);
public record ScaffoldResult(bool Success, string? Error);

/// <summary>
/// Cria um novo projeto Mabel. Cada target gera um host nativo que
/// carrega o .wasm e renderiza via SkiaSharp/CoreGraphics (sem WebView).
/// </summary>
public sealed class CreateProject
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public CreateProject(IShellExecutor shell, IFileSystem fs) { _shell = shell; _fs = fs; }

    public ScaffoldResult Execute(ScaffoldRequest req)
    {
        var dir = Path.GetFullPath(req.AppName);

        if (_fs.DirectoryExists(dir))
            return new(false, $"Diretorio '{req.AppName}' ja existe.");

        // Web app (Blazor WASM -> sera compilado pra WASI)
        var rc = _shell.RunPassthrough($"dotnet new blazorwasm -o \"{dir}/web_app\" --no-restore");
        if (rc != 0) return new(false, "Falha ao criar projeto Blazor.");

        foreach (var p in req.Platforms.Each())
        {
            var r = p switch
            {
                Platform.Ios     => ScaffoldIos(dir, req.BundleId),
                Platform.Android => ScaffoldAndroid(dir, req.BundleId),
                Platform.Desktop => ScaffoldDesktop(dir),
                _ => new(true, null),
            };
            if (!r.Success) return r;
        }

        WriteManifest(dir, req);
        return new(true, null);
    }

    private ScaffoldResult ScaffoldIos(string dir, string bundleId)
    {
        var iosDir = Path.Combine(dir, "ios_app");
        _fs.CreateDirectory(Path.Combine(iosDir, "Sources", "ios_app"));

        _fs.WriteAllText(Path.Combine(iosDir, "xtool.yml"), $"version: 1\nbundleID: {bundleId}\n");

        _fs.WriteAllText(Path.Combine(iosDir, "Package.swift"),
"""
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ios_app",
    platforms: [.iOS(.v15)],
    products: [.library(name: "ios_app", targets: ["ios_app"])],
    targets: [
        .target(name: "ios_app", resources: [.copy("Resources")])
    ]
)
""");

        _fs.WriteAllText(Path.Combine(iosDir, "Sources", "ios_app", "ContentView.swift"),
"""
import SwiftUI

// Mabel Host - Canvas rendering (sem WebView)
// O MabelCanvasView renderiza comandos recebidos do modulo WASM.
// Para usar, copie MabelCanvasView.swift e MabelView.swift do framework.

struct ContentView: View {
    var body: some View {
        Text("Mabel App")
            .font(.title)
            .padding()
        Text("Canvas rendering via WASI")
            .foregroundColor(.gray)
    }
}
""");

        return new(true, null);
    }

    private ScaffoldResult ScaffoldAndroid(string dir, string bundleId)
    {
        _fs.CreateDirectory(Path.Combine(dir, "android_app"));
        _fs.WriteAllText(Path.Combine(dir, "android_app", "README.md"),
            $"# Android Target\nBundle ID: {bundleId}\nCanvas rendering via WASI (em desenvolvimento).\n");
        return new(true, null);
    }

    private ScaffoldResult ScaffoldDesktop(string dir)
    {
        var rc = _shell.RunPassthrough($"dotnet new console -o \"{Path.Combine(dir, "desktop_app")}\" --no-restore");
        if (rc != 0) return new(false, "Falha ao criar projeto Desktop.");
        return new(true, null);
    }

    private void WriteManifest(string dir, ScaffoldRequest req)
    {
        var platforms = string.Join(", ", req.Platforms.Each().Select(p => $"\"{p.Label()}\""));
        _fs.WriteAllText(Path.Combine(dir, "mabel.json"),
$$"""
{
  "name": "{{req.AppName}}",
  "bundleId": "{{req.BundleId}}",
  "version": "1.0.0",
  "platforms": [{{platforms}}],
  "webApp": "web_app",
  "renderer": "canvas"
}
""");
    }
}
