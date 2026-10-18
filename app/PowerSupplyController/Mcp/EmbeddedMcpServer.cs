using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace PowerSupplyController.Mcp;

public sealed class EmbeddedMcpServer(PowerSupplyService service) : IAsyncDisposable
{
    private WebApplication? _app;
    public bool IsRunning => _app is not null;

    public async Task StartAsync()
    {
        if (_app is not null) return;
        var settings = service.Settings;
        settings.Validate();
        if (!settings.Enabled) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        // Explicit Listen ignores ASPNETCORE_URLS and never exposes the supply on LAN interfaces.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, settings.Port);
            options.Limits.MaxRequestBodySize = 16 * 1024;
            options.Limits.MaxConcurrentConnections = 16;
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(service);
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "rd6012-power-supply", Version = "1.0.0" };
            options.ServerInstructions = "本服务控制真实电源。先查询能力和状态，遵守用户配置限值。写入不确定时先读取，禁止自动重试写入。";
        }).WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<PowerSupplyTools>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            string host = context.Request.Host.Host;
            if ((host != "127.0.0.1" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Host.Port != settings.Port)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (context.Request.Headers.TryGetValue("Origin", out var origins) &&
                (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
                 origin.GetLeftPart(UriPartial.Authority) != $"http://{context.Request.Host}" ||
                 origins[0] != origin.GetLeftPart(UriPartial.Authority)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            byte[] expected = Encoding.UTF8.GetBytes("Bearer " + settings.Token);
            byte[] actual = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.MapMcp("/mcp");
        try
        {
            await app.StartAsync();
            _app = app;
            service.SetActive(true);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        service.SetActive(false);
        var app = _app;
        _app = null;
        if (app is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await app.StopAsync(timeout.Token); }
        finally { await app.DisposeAsync(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
