using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.Infrastructure.Windows.LocalWeb;

public sealed record PublicOutputState(string Id, string Name, bool Ready, bool Reconnecting, long ExtraDelayMilliseconds);
public sealed record LocalViewState(bool Running, bool CanControl, int TargetSeconds, long? CurrentDelayMilliseconds,
    string Phase, bool IngestConnected, int[] Presets, int ProtectionSeconds, InterfacePreferences Overlay,
    PublicOutputState[] Outputs);
public sealed record DelayCommand(string Action, int? Seconds = null);
public sealed record DelayCommandResult(bool Accepted, int TargetSeconds, string Message);

/// <summary>Loopback-only HTTP endpoints. Capability tokens stay in URL fragments and protected storage.</summary>
public sealed class LocalControlServer : IAsyncDisposable
{
    private WebApplication? _application;
    private readonly LocalAccessSettings _access;
    private readonly Func<LocalViewState> _readState;
    private readonly Func<DelayCommand, CancellationToken, Task<DelayCommandResult>> _command;
    public Uri? Address { get; private set; }
    public string OverlayUrl => Address is null ? "" : new Uri(Address, "overlay").AbsoluteUri + "#" + _access.OverlayToken;
    public string DockUrl => Address is null ? "" : new Uri(Address, "dock").AbsoluteUri + "#" + _access.DockToken;

    public LocalControlServer(LocalAccessSettings access, Func<LocalViewState> readState,
        Func<DelayCommand, CancellationToken, Task<DelayCommandResult>> command)
    { access.Validate(); _access = access; _readState = readState; _command = command; }

    public async Task StartAsync(CancellationToken token = default, bool dynamicPortForTests = false)
    {
        if (_application is not null) throw new InvalidOperationException("Le serveur local est déjà démarré.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], ApplicationName = typeof(LocalControlServer).Assembly.FullName, EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear(); // Environment URLs/configuration must never widen this listener.
        builder.Logging.ClearProviders(); // Never record private links, authorization headers or request bodies.
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Listen(IPAddress.Loopback, dynamicPortForTests ? 0 : _access.Port);
            server.Limits.MaxRequestBodySize = 1024;
            server.Limits.MaxConcurrentConnections = 32;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
                request.Host.Host != "127.0.0.1" || request.Host.Port != context.Connection.LocalPort)
            { context.Response.StatusCode = 403; return; }
            string origin = request.Headers.Origin.ToString();
            if (origin.Length > 0 && origin != $"http://127.0.0.1:{context.Connection.LocalPort}")
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            await next(context).ConfigureAwait(false);
        });
        foreach (var view in new[] { "overlay", "dock" })
        {
            string selected = view;
            app.MapGet("/" + view, async context =>
            {
                string html = Encoding.UTF8.GetString(Resource("page.html")).Replace("@@VIEW@@", selected, StringComparison.Ordinal);
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
            });
        }
        foreach (var (name, type) in new[] { ("app.css", "text/css"), ("app.js", "text/javascript"),
            ("Inter.ttf", "font/ttf"), ("Space.ttf", "font/ttf"), ("Mono.ttf", "font/ttf"), ("halo.svg", "image/svg+xml") })
        {
            byte[] bytes = Resource(name);
            app.MapGet("/assets/" + name, async context =>
            { context.Response.ContentType = type; await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false); });
        }
        app.MapGet("/api/state", async context =>
        {
            if (!Authorized(context, control: false)) { context.Response.StatusCode = 401; return; }
            await context.Response.WriteAsJsonAsync(_readState(), context.RequestAborted).ConfigureAwait(false);
        });
        app.MapPost("/api/delay", async context =>
        {
            if (!Authorized(context, control: true)) { context.Response.StatusCode = 401; return; }
            if (!context.Request.HasJsonContentType()) { context.Response.StatusCode = 415; return; }
            DelayCommand? command;
            try { command = await context.Request.ReadFromJsonAsync<DelayCommand>(context.RequestAborted).ConfigureAwait(false); }
            catch (JsonException) { context.Response.StatusCode = 400; return; }
            catch (BadHttpRequestException) { context.Response.StatusCode = 413; return; }
            if (command is null || command.Action is not ("set" or "adjust" or "protect") ||
                command.Action == "set" && command.Seconds is not (>= 0 and <= 300) ||
                command.Action == "adjust" && command.Seconds is not (>= -300 and <= 300))
            { context.Response.StatusCode = 400; return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var result = await _command(command, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                context.Response.StatusCode = result.Accepted ? 200 : 409;
                await context.Response.WriteAsJsonAsync(result, context.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { context.Response.StatusCode = 503; }
        });
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            Address = new Uri(addresses.Addresses.Single().TrimEnd('/') + "/");
            _application = app;
        }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private bool Authorized(HttpContext context, bool control)
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length != 71) return false;
        byte[] candidate = Encoding.ASCII.GetBytes(authorization[7..]);
        return CryptographicOperations.FixedTimeEquals(candidate, Encoding.ASCII.GetBytes(_access.DockToken)) ||
            !control && CryptographicOperations.FixedTimeEquals(candidate, Encoding.ASCII.GetBytes(_access.OverlayToken));
    }
    private static byte[] Resource(string name)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Temhalo.LocalWeb." + name)
            ?? throw new InvalidOperationException("Ressource navigateur absente.");
        using var memory = new MemoryStream(); source.CopyTo(memory); return memory.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        var app = _application; _application = null; Address = null;
        if (app is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await app.StopAsync(timeout.Token).ConfigureAwait(false); }
            finally { await app.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
