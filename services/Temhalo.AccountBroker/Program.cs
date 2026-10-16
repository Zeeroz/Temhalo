using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Temhalo.AccountBroker;

public static class Program
{
    public static Task Main(string[] args) => BrokerApplication.Build(args).RunAsync();
}

public sealed record BrokerOptions(string ClientId, string ClientSecret, int CallbackPort = 18473)
{
    public string RedirectUri => $"http://localhost:{CallbackPort}/oauth/callback";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret) || CallbackPort is < 1024 or > 65535)
            throw new InvalidOperationException("Configurez Kick__ClientId, Kick__ClientSecret et un port de retour local valide sur le serveur.");
    }
    public override string ToString() => "Configuration du service Kick [secret masqué]";
}

/// <summary>No credentials in source, desktop binaries, logs, URLs or browser storage. Fixed upstream and callback.</summary>
public static class BrokerApplication
{
    public static WebApplication Build(string[] args, BrokerOptions? options = null, HttpMessageHandler? testHandler = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Logging.ClearProviders(); // In particular, never log callback codes, forms or upstream bodies.
        options ??= new(builder.Configuration["Kick:ClientId"] ?? "", builder.Configuration["Kick:ClientSecret"] ?? "",
            int.TryParse(builder.Configuration["Kick:CallbackPort"], out int port) ? port : 18473);
        options.Validate();
        builder.WebHost.ConfigureKestrel(server => { server.AddServerHeader = false; server.Limits.MaxRequestBodySize = 32768; });
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = 429;
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        var http = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(http.Dispose);
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { service = "Temhalo account connection" }));
        app.MapPost("/oauth/token", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            // Native app only: browsers cannot set this header cross-origin without a CORS preflight.
            if (context.Request.Headers.Origin.Count > 0 || context.Request.Headers["X-Temhalo-Client"] != "desktop" ||
                !context.Request.HasFormContentType) return Results.BadRequest(new { error = "invalid_request" });
            IFormCollection form;
            try { form = await context.Request.ReadFormAsync(context.RequestAborted); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "invalid_request" }); }
            if (form["client_id"] != options.ClientId || form["client_id"].Count != 1 || form["grant_type"].Count != 1)
                return Results.BadRequest(new { error = "invalid_request" });
            string grant = form["grant_type"].ToString();
            var fields = new Dictionary<string, string>
            { ["client_id"] = options.ClientId, ["client_secret"] = options.ClientSecret, ["grant_type"] = grant };
            if (grant == "authorization_code")
            {
                string verifier = form["code_verifier"].ToString(), code = form["code"].ToString();
                if (form["redirect_uri"] != options.RedirectUri || code.Length is < 1 or > 4096 || form["code"].Count != 1 ||
                    !Regex.IsMatch(verifier, "^[A-Za-z0-9._~-]{43,128}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    return Results.BadRequest(new { error = "invalid_request" });
                fields["redirect_uri"] = options.RedirectUri; fields["code"] = code; fields["code_verifier"] = verifier;
            }
            else if (grant == "refresh_token")
            {
                if (form["refresh_token"].Count != 1 || form["refresh_token"].ToString().Length is < 1 or > 16384)
                    return Results.BadRequest(new { error = "invalid_request" });
                fields["refresh_token"] = form["refresh_token"].ToString();
            }
            else return Results.BadRequest(new { error = "unsupported_grant_type" });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.kick.com/oauth/token") { Content = new FormUrlEncodedContent(fields) };
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return Results.Json(new { error = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized ? "invalid_grant" : "temporarily_unavailable" }, statusCode: response.StatusCode == HttpStatusCode.TooManyRequests ? 429 : 400);
                if (response.Content.Headers.ContentLength > 65536) return Results.StatusCode(502);
                await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                using var bytes = new MemoryStream(); byte[] buffer = new byte[4096]; int read;
                while ((read = await stream.ReadAsync(buffer, context.RequestAborted)) > 0)
                { if (bytes.Length + read > 65536) return Results.StatusCode(502); bytes.Write(buffer, 0, read); }
                using var json = JsonDocument.Parse(bytes.ToArray());
                var root = json.RootElement;
                return Results.Json(new
                {
                    access_token = root.GetProperty("access_token").GetString(),
                    refresh_token = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : "",
                    token_type = "bearer", expires_in = int.Parse(root.GetProperty("expires_in").ToString())
                });
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) { return Results.StatusCode(504); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { return Results.Json(new { error = "temporarily_unavailable" }, statusCode: 502); }
        }).DisableAntiforgery();
        return app;
    }
}
