using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Temhalo.Infrastructure.Windows.Accounts;

/// <summary>One-shot loopback receiver, listening before the browser opens. No URL/request logging.</summary>
public sealed class LoopbackAuthorization : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TaskCompletionSource<string> _code = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _state;
    public Uri RedirectUri { get; private set; } = null!;
    private LoopbackAuthorization(WebApplication app, string state) { _app = app; _state = state; }
    public static async Task<LoopbackAuthorization> StartAsync(string state, int port, bool localhost, CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(LoopbackAuthorization).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        { server.AddServerHeader = false; server.Limits.MaxRequestBodySize = 0; server.Listen(IPAddress.Loopback, port); });
        var app = builder.Build();
        var receiver = new LoopbackAuthorization(app, state);
        app.Run(receiver.ReceiveAsync);
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var uri = new UriBuilder(address) { Host = localhost ? "localhost" : "127.0.0.1", Path = "/oauth/callback" };
            receiver.RedirectUri = uri.Uri;
            return receiver;
        }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task ReceiveAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.Method != "GET" || context.Request.Path != "/oauth/callback" ||
            context.Request.Host.Port != RedirectUri.Port ||
            !string.Equals(context.Request.Host.Host, RedirectUri.Host, StringComparison.OrdinalIgnoreCase) ||
            !context.Request.Query.TryGetValue("state", out var state) || state.Count != 1 || !OAuthProof.Matches(_state, state.ToString()))
        { context.Response.StatusCode = 400; await context.Response.WriteAsync("Retour non reconnu. Revenez dans Temhalo."); return; }
        var code = context.Request.Query["code"];
        if (context.Request.Query.ContainsKey("error"))
            _code.TrySetException(new AccountConnectionException("Autorisation refusée ou annulée. Vous pouvez réessayer ou utiliser une clé manuellement."));
        else if (code.Count == 1 && code.ToString().Length is > 0 and <= 4096)
            _code.TrySetResult(code.ToString());
        else { context.Response.StatusCode = 400; await context.Response.WriteAsync("Code absent. Revenez dans Temhalo."); return; }
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Vous pouvez fermer cet onglet et revenir dans Temhalo.");
    }
    public Task<string> WaitAsync(CancellationToken token) => _code.Task.WaitAsync(token);
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _app.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally { await _app.DisposeAsync().ConfigureAwait(false); }
    }
}
