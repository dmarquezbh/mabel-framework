using MabelHelloWorld;

// ── Mabel Hello World ──────────────────────────────────────────────────
//
// This sample demonstrates Mabel's rendering pipeline.
//
// It builds a list of RenderCommands (the same format that would be
// sent from a WASM guest to the native host via the WASI protocol)
// and prints them to stdout.
//
// In a real Mabel app, these commands would be compiled into a .wasm
// module and rendered by the native host (Core Graphics on iOS,
// SkiaSharp on Desktop) — no WebView involved.
//
// Run:
//   export PATH="$HOME/.dotnet:$PATH"
//   dotnet run --project samples/hello-world
//

// Simulate an iPhone-sized screen (390x844 = iPhone 14)
const float screenWidth = 390f;
const float screenHeight = 844f;

Console.WriteLine();
Console.WriteLine("  Mabel Framework - Hello World");
Console.WriteLine("  ─────────────────────────────");
Console.WriteLine();
Console.WriteLine($"  Screen: {screenWidth}x{screenHeight} (simulated iPhone 14)");
Console.WriteLine();

// Build one frame of render commands
var frame = HelloApp.BuildFrame(screenWidth, screenHeight);

// Print the commands (in production, these go to the native canvas)
HelloApp.PrintFrame(frame);

Console.WriteLine();
Console.WriteLine("  This output shows the render commands that would be");
Console.WriteLine("  sent to the native host via the WASI protocol.");
Console.WriteLine("  On iOS, these draw on Core Graphics (no WebView).");
Console.WriteLine("  On Desktop, these draw on SkiaSharp.");
Console.WriteLine();
