using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Console.Configuration;
using TradingSystem.Console.Trading;

namespace TradingSystem.Console.Api;

/// <summary>
/// 🌐 HTTP Management API Service
/// Provides REST API for:
/// - Viewing trading status
/// - Resetting risk controls
/// - Modifying parameters
/// - Starting/Stopping trading pairs
/// </summary>
public class ManagementApiService
{
    private readonly ILogger<ManagementApiService> _logger;
    private readonly ManagementConfig _config;
    private readonly IMultiAssetTradingManager _tradingManager;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public ManagementApiService(
        ILogger<ManagementApiService> logger,
        IOptions<ManagementConfig> config,
        IMultiAssetTradingManager tradingManager)
    {
        _logger = logger;
        _config = config.Value;
        _tradingManager = tradingManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.EnableApi)
        {
            _logger.LogInformation("Management API is disabled");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_config.HttpPort}/");

        try
        {
            _listener.Start();
            _logger.LogInformation("🌐 Management API listening on port {Port}", _config.HttpPort);
            _logger.LogInformation("📋 Available endpoints:");
            _logger.LogInformation("   GET  /health          - Health check");
            _logger.LogInformation("   GET  /status          - All assets status");
            _logger.LogInformation("   GET  /status/{{symbol}} - Single asset status");
            _logger.LogInformation("   POST /risk/reset/{{symbol}} - Reset risk for asset");
            _logger.LogInformation("   POST /risk/reset/all  - Reset all risk");
            _logger.LogInformation("   POST /start/{{symbol}}  - Start trading asset");
            _logger.LogInformation("   POST /stop/{{symbol}}   - Stop trading asset");
            _logger.LogInformation("   GET  /config          - Get current config");
            _logger.LogInformation("   POST /config/{{symbol}} - Update asset config");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
                }
                catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod;

        try
        {
            // Validate API Token (except /health)
            if (path != "/health" && !ValidateToken(request))
            {
                await SendResponseAsync(response, 401, new { error = "Unauthorized" });
                return;
            }

            // Route Handling
            var result = await RouteRequestAsync(method, path, request);
            await SendResponseAsync(response, result.statusCode, result.data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling request: {method} {path}");
            await SendResponseAsync(response, 500, new { error = ex.Message });
        }
    }

    private bool ValidateToken(HttpListenerRequest request)
    {
        if (string.IsNullOrEmpty(_config.ApiToken))
            return true; // Skip validation if token not set

        var authHeader = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authHeader))
            return false;

        // Support Bearer token or direct token
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader.Substring(7)
            : authHeader;

        return token == _config.ApiToken;
    }

    private async Task<(int statusCode, object data)> RouteRequestAsync(
        string method, string path, HttpListenerRequest request)
    {
        // GET /health
        if (method == "GET" && path == "/health")
        {
            return (200, GetHealthStatus());
        }

        // GET /status
        if (method == "GET" && path == "/status")
        {
            return (200, GetAllStatus());
        }

        // GET /status/{symbol}
        if (method == "GET" && path.StartsWith("/status/"))
        {
            var symbol = path.Substring("/status/".Length).ToUpperInvariant();
            var status = _tradingManager.GetAssetStatus(symbol);
            if (status == null)
                return (404, new { error = $"Asset {symbol} not found" });
            return (200, status);
        }

        // POST /risk/reset/all
        if (method == "POST" && path == "/risk/reset/all")
        {
            _tradingManager.ResetAllRisk();
            _logger.LogWarning("🔄 All risk counters reset via API");
            return (200, new { success = true, message = "All risk counters reset" });
        }

        // POST /risk/reset/{symbol}
        if (method == "POST" && path.StartsWith("/risk/reset/"))
        {
            var symbol = path.Substring("/risk/reset/".Length).ToUpperInvariant();
            var success = _tradingManager.ResetAssetRisk(symbol);
            if (!success)
                return (404, new { error = $"Asset {symbol} not found" });
            _logger.LogWarning($"🔄 Risk counters reset for {symbol} via API");
            return (200, new { success = true, message = $"Risk counters reset for {symbol}" });
        }

        // POST /start/{symbol}
        if (method == "POST" && path.StartsWith("/start/"))
        {
            var symbol = path.Substring("/start/".Length).ToUpperInvariant();
            var success = await _tradingManager.StartAssetAsync(symbol);
            if (!success)
                return (400, new { error = $"Failed to start {symbol}" });
            _logger.LogInformation($"▶️ {symbol} started via API");
            return (200, new { success = true, message = $"{symbol} trading started" });
        }

        // POST /stop/{symbol}
        if (method == "POST" && path.StartsWith("/stop/"))
        {
            var symbol = path.Substring("/stop/".Length).ToUpperInvariant();
            var success = await _tradingManager.StopAssetAsync(symbol);
            if (!success)
                return (400, new { error = $"Failed to stop {symbol}" });
            _logger.LogInformation($"⏹️ {symbol} stopped via API");
            return (200, new { success = true, message = $"{symbol} trading stopped" });
        }

        // GET /config
        if (method == "GET" && path == "/config")
        {
            return (200, _tradingManager.GetAllConfigs());
        }

        // POST /config/{symbol} - Update Config
        if (method == "POST" && path.StartsWith("/config/"))
        {
            var symbol = path.Substring("/config/".Length).ToUpperInvariant();
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var newConfig = JsonSerializer.Deserialize<AssetConfigUpdate>(body, JsonOptions);
            if (newConfig == null)
                return (400, new { error = "Invalid config JSON" });

            var success = _tradingManager.UpdateAssetConfig(symbol, newConfig);
            if (!success)
                return (404, new { error = $"Asset {symbol} not found" });
            _logger.LogInformation($"⚙️ {symbol} config updated via API");
            return (200, new { success = true, message = $"{symbol} config updated" });
        }
        
        // 🔥 POST /config/reload - Hot Reload Config
        if (method == "POST" && path == "/config/reload")
        {
            var reloadSuccess = await _tradingManager.ReloadConfigAsync();
            if (reloadSuccess)
            {
                _logger.LogInformation("🔄 Configuration reload successful via API");
                return (200, new { success = true, message = "Configuration reloaded successfully" });
            }
            return (500, new { error = "Failed to reload configuration" });
        }

        // GET /metrics (Prometheus format)
        if (method == "GET" && path == "/metrics")
        {
            return (200, GetPrometheusMetrics());
        }

        return (404, new { error = "Endpoint not found" });
    }

    private object GetHealthStatus()
    {
        var allStatus = _tradingManager.GetAllStatus();
        var isHealthy = allStatus.Any(s => s.IsRunning);
        
        // 🔥 Enhanced Health Check: Detailed Diagnostics
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var uptime = DateTime.Now - process.StartTime;
        
        return new
        {
            status = isHealthy ? "healthy" : "unhealthy",
            timestamp = DateTime.UtcNow,
            activeAssets = allStatus.Count(s => s.IsRunning),
            totalAssets = allStatus.Count,
            
            // 🔥 System Resources
            diagnostics = new
            {
                memoryMB = process.WorkingSet64 / 1024 / 1024,
                gcMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024,
                uptimeHours = uptime.TotalHours,
                uptimeFormatted = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m",
                threads = process.Threads.Count,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2)
            },
            
            // 🔥 Detailed Asset Status
            assets = allStatus.Select(s => new
            {
                symbol = s.Symbol,
                running = s.IsRunning,
                inPosition = s.IsInPosition,
                riskPaused = s.RiskPaused,
                todayTrades = s.TodayTrades,
                todayPnL = s.TodayPnL,
                consecutiveLosses = s.ConsecutiveLosses,
                winRate = s.WinRate,
                lastTradeTime = s.LastTradeTime
            })
        };
    }

    private object GetAllStatus()
    {
        return new
        {
            timestamp = DateTime.UtcNow,
            assets = _tradingManager.GetAllStatus()
        };
    }

    private string GetPrometheusMetrics()
    {
        var sb = new StringBuilder();
        var allStatus = _tradingManager.GetAllStatus();

        sb.AppendLine("# HELP trading_asset_running Asset trading status");
        sb.AppendLine("# TYPE trading_asset_running gauge");
        foreach (var status in allStatus)
        {
            sb.AppendLine($"trading_asset_running{{symbol=\"{status.Symbol}\"}} {(status.IsRunning ? 1 : 0)}");
        }

        sb.AppendLine("# HELP trading_asset_position Asset position status");
        sb.AppendLine("# TYPE trading_asset_position gauge");
        foreach (var status in allStatus)
        {
            sb.AppendLine($"trading_asset_position{{symbol=\"{status.Symbol}\"}} {(status.IsInPosition ? 1 : 0)}");
        }

        sb.AppendLine("# HELP trading_asset_pnl Asset profit/loss");
        sb.AppendLine("# TYPE trading_asset_pnl gauge");
        foreach (var status in allStatus)
        {
            sb.AppendLine($"trading_asset_pnl{{symbol=\"{status.Symbol}\"}} {status.TodayPnL}");
        }

        sb.AppendLine("# HELP trading_asset_trades_today Asset trades count today");
        sb.AppendLine("# TYPE trading_asset_trades_today counter");
        foreach (var status in allStatus)
        {
            sb.AppendLine($"trading_asset_trades_today{{symbol=\"{status.Symbol}\"}} {status.TodayTrades}");
        }

        return sb.ToString();
    }

    private async Task SendResponseAsync(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        
        string content;
        if (data is string str)
        {
            content = str;
            response.ContentType = "text/plain; charset=utf-8";
        }
        else
        {
            content = JsonSerializer.Serialize(data, JsonOptions);
            response.ContentType = "application/json; charset=utf-8";
        }

        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }
}

/// <summary>
/// Asset Config Update DTO
/// </summary>
public class AssetConfigUpdate
{
    public decimal? Capital { get; set; }
    public string? Interval { get; set; }
    public string? AlphaModel { get; set; }
    public StopLossConfig? StopLoss { get; set; }
    public AssetRiskConfig? Risk { get; set; }
}

/// <summary>
/// Asset Status DTO
/// </summary>
public class AssetStatus
{
    public string Symbol { get; set; } = "";
    public bool IsRunning { get; set; }
    public bool IsInPosition { get; set; }
    public decimal Capital { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal TodayPnL { get; set; }
    public int TodayTrades { get; set; }
    public int TodayWinCount { get; set; }
    public int TotalTradeCount { get; set; }
    public int TotalWinCount { get; set; }
    public decimal WinRate { get; set; }
    public decimal MaxDrawdown { get; set; }
    public int ConsecutiveLosses { get; set; }
    public bool RiskPaused { get; set; }
    public string? RiskPauseReason { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public string CurrentSignal { get; set; } = "";
    public decimal StopLossPrice { get; set; }
    public decimal HighestPriceSinceEntry { get; set; }
    public int CandleCount { get; set; }
}
