using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.DevServer;

/// <summary>
/// Servidor HTTP + WebSocket embutido para hot reload.
///
/// Endpoints:
///   GET /mabel.wasm         -> serve o modulo WASM compilado
///   GET /status             -> JSON com versao e timestamp do ultimo build
///   WebSocket /ws           -> notifica "reload" quando o WASM e recompilado
///
/// O app Mabel Go no celular:
///   1. Conecta via WebSocket em ws://<ip>:5555/ws
///   2. Baixa o .wasm inicial de http://<ip>:5555/mabel.wasm
///   3. Ao receber "reload", baixa novamente e re-renderiza
/// </summary>
public sealed class MabelDevServer : IDisposable
{
    private readonly IShellExecutor _shell;
    private readonly string _projectPath;
    private readonly int _port;
    private readonly bool _verbose;

    private HttpListener? _listener;
    private HttpListener? _fallbackListener;
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private readonly List<WebSocket> _clients = new();
    private readonly object _lock = new();
    private int _buildVersion;
    private DateTime _lastBuild = DateTime.MinValue;
    private CancellationTokenSource? _cts;
    private int _rebuildInProgress;

    public MabelDevServer(IShellExecutor shell, string projectPath, int port = 5555, bool verbose = false)
    {
        _shell = shell;
        _projectPath = Path.GetFullPath(projectPath);
        _port = port;
        _verbose = verbose;
    }

    /// <summary>
    /// Inicia o dev server. Bloqueia ate Ctrl+C.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = _cts.Token;

        var webAppDir = Path.Combine(_projectPath, "web_app");

        // Build inicial
        Log("Building WASM...");
        var rc = _shell.RunPassthrough("dotnet build -c Release", workingDir: webAppDir);
        if (rc != 0)
        {
            LogError("Initial build failed.");
            return 1;
        }
        Interlocked.Increment(ref _buildVersion);
        _lastBuild = DateTime.UtcNow;
        Log("Build OK");

        // File watcher
        StartWatcher(webAppDir);

        // HTTP + WebSocket listener
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            // Fallback to localhost only if binding to all interfaces fails.
            // Keep a reference to the failed listener so we can dispose it.
            _fallbackListener = _listener;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            Log($"Bound to localhost only ({ex.Message})");
        }

        var ip = GetLocalIp() ?? "localhost";
        Log($"Dev server running on http://{ip}:{_port}");
        Log($"  WASM:      http://{ip}:{_port}/mabel.wasm");
        Log($"  WebSocket: ws://{ip}:{_port}/ws");
        Log($"  Status:    http://{ip}:{_port}/status");
        Log("");
        Log("Point your Mabel Go app to this address.");
        Log("Press Ctrl+C to stop.\n");

        try
        {
            while (!token.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(token);
                _ = HandleRequestAsync(ctx, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) when (token.IsCancellationRequested) { }

        return 0;
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";

        try
        {
            if (ctx.Request.IsWebSocketRequest && path == "/ws")
            {
                await HandleWebSocket(ctx, token);
                return;
            }

            switch (path)
            {
                case "/mabel.wasm":
                    await ServeWasm(ctx);
                    break;

                case "/status":
                    ServeStatus(ctx);
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    var body = Encoding.UTF8.GetBytes("Not found. Endpoints: /mabel.wasm, /ws, /status");
                    await ctx.Response.OutputStream.WriteAsync(body, token);
                    ctx.Response.Close();
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_verbose) LogError($"Request error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* response may already be closed */ }
        }
    }

    private async Task HandleWebSocket(HttpListenerContext ctx, CancellationToken token)
    {
        var wsCtx = await ctx.AcceptWebSocketAsync(null);
        var ws = wsCtx.WebSocket;

        int clientCount;
        lock (_lock)
        {
            _clients.Add(ws);
            clientCount = _clients.Count;
        }
        Log($"Client connected ({clientCount} total)");

        try
        {
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (WebSocketException) { /* client disconnected abruptly */ }
        catch (OperationCanceledException) { /* server shutting down */ }
        finally
        {
            // Gracefully close the websocket if still open
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", closeTimeout.Token);
                }
                catch { /* best effort */ }
            }

            lock (_lock)
            {
                _clients.Remove(ws);
                clientCount = _clients.Count;
            }
            ws.Dispose();
            Log($"Client disconnected ({clientCount} total)");
        }
    }

    private async Task ServeWasm(HttpListenerContext ctx)
    {
        // Find the compiled WASM output
        var wasmPath = FindWasmOutput();
        if (wasmPath is null || !File.Exists(wasmPath))
        {
            ctx.Response.StatusCode = 404;
            var body = Encoding.UTF8.GetBytes("WASM not found. Is the project built?");
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
            return;
        }

        var bytes = await File.ReadAllBytesAsync(wasmPath);
        ctx.Response.ContentType = "application/wasm";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.Headers.Add("X-Build-Version", Interlocked.CompareExchange(ref _buildVersion, 0, 0).ToString());
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private void ServeStatus(HttpListenerContext ctx)
    {
        int version = Interlocked.CompareExchange(ref _buildVersion, 0, 0);
        int clientCount;
        lock (_lock) clientCount = _clients.Count;

        // Use proper JSON serialization to prevent injection
        var status = new
        {
            version,
            lastBuild = _lastBuild.ToString("O"),
            clients = clientCount,
            project = Path.GetFileName(_projectPath)
        };

        var json = JsonSerializer.Serialize(status);
        var body = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json";
        ctx.Response.OutputStream.Write(body);
        ctx.Response.Close();
    }

    private void StartWatcher(string webAppDir)
    {
        _watcher = new FileSystemWatcher(webAppDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };

        // Watch Blazor/Razor files
        _watcher.Filters.Add("*.razor");
        _watcher.Filters.Add("*.cs");
        _watcher.Filters.Add("*.css");
        _watcher.Filters.Add("*.html");

        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) =>
        {
            // Fire-and-forget but with error handling.
            // Use Interlocked to prevent concurrent rebuilds.
            _ = Task.Run(async () =>
            {
                try
                {
                    await RebuildAndNotify(webAppDir);
                }
                catch (Exception ex)
                {
                    LogError($"Rebuild error: {ex.Message}");
                }
            });
        };

        void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // Timer.Stop + Timer.Start is thread-safe for System.Timers.Timer.
            // Each call resets the debounce window.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += (s, e) => OnFileEvent(s, e);

        _watcher.EnableRaisingEvents = true;
        Log("Watching for file changes...");
    }

    private async Task RebuildAndNotify(string webAppDir)
    {
        // Prevent concurrent rebuilds
        if (Interlocked.Exchange(ref _rebuildInProgress, 1) == 1)
            return;

        try
        {
            Log("File changed — rebuilding...");

            var rc = _shell.RunPassthrough("dotnet build -c Release", workingDir: webAppDir);
            if (rc != 0)
            {
                LogError("Build failed. Fix errors and save again.");
                return;
            }

            var version = Interlocked.Increment(ref _buildVersion);
            _lastBuild = DateTime.UtcNow;
            Log($"Build OK (v{version})");

            // Notify all connected clients
            List<WebSocket> snapshot;
            lock (_lock) snapshot = _clients.ToList();

            var msg = Encoding.UTF8.GetBytes($"reload:{version}");
            var tasks = snapshot
                .Where(ws => ws.State == WebSocketState.Open)
                .Select(ws => SafeSendAsync(ws, msg));

            await Task.WhenAll(tasks);

            Log($"Notified {snapshot.Count} client(s)");
        }
        finally
        {
            Interlocked.Exchange(ref _rebuildInProgress, 0);
        }
    }

    private static async Task SafeSendAsync(WebSocket ws, byte[] msg)
    {
        try
        {
            await ws.SendAsync(msg, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Client may have disconnected; ignore send failures.
        }
    }

    private string? FindWasmOutput()
    {
        // Blazor WASM output path
        var candidates = new[]
        {
            Path.Combine(_projectPath, "web_app", "bin", "Release", "net10.0", "wwwroot", "_framework"),
            Path.Combine(_projectPath, "web_app", "bin", "Debug", "net10.0", "wwwroot", "_framework"),
            Path.Combine(_projectPath, "web_app", "bin", "Release", "net10.0"),
            Path.Combine(_projectPath, "web_app", "bin", "Debug", "net10.0"),
        };

        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir)) continue;
            var wasm = Directory.GetFiles(dir, "*.wasm").FirstOrDefault();
            if (wasm is not null) return wasm;
        }

        return null;
    }

    private string? GetLocalIp()
    {
        var r = _shell.Run("hostname -I 2>/dev/null || ipconfig getifaddr en0 2>/dev/null");
        return r.Success && !string.IsNullOrWhiteSpace(r.Output)
            ? r.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            : null;
    }

    private void Log(string msg) => Console.WriteLine($"  [dev] {msg}");
    private void LogError(string msg) => Console.Error.WriteLine($"  [dev] ERROR: {msg}");

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
        _listener?.Close();
        (_listener as IDisposable)?.Dispose();
        _fallbackListener?.Close();
        (_fallbackListener as IDisposable)?.Dispose();
        lock (_lock)
        {
            foreach (var ws in _clients)
                try { ws.Dispose(); } catch { }
            _clients.Clear();
        }
    }
}
