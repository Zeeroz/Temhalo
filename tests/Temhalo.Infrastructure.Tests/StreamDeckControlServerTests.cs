using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Temhalo.Infrastructure.Windows.LocalControl;
using Temhalo.Infrastructure.Windows.LocalWeb;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class StreamDeckControlServerTests
{
    private static LocalViewState State(int target = 0) => new(false, true, target, null, "Stopped", false, [0, 15, 30], 60,
        [new("private-id", "private-output-name", false, false, 0)]);
    private static string PipeName() => "Temhalo.StreamDeck.test." + Guid.NewGuid().ToString("N");
    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000); await SendAsync(pipe, new { type = "hello", protocol = 1 }); return pipe;
    }
    private static async Task SendAsync(Stream pipe, object value) => await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n"));
    private static async Task<JsonElement> ReceiveAsync(StreamReader reader, string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.GetProperty("type").GetString() == type) return doc.RootElement.Clone();
        }
        throw new IOException("Le canal a fermé avant la réponse.");
    }

    [Fact]
    public async Task ConnectionNeedsNoLinkAndStateIsPushedWithoutPolling()
    {
        int target = 0;
        await using var server = new StreamDeckControlServer(() => State(target), (_, _) => throw new InvalidOperationException(), PipeName());
        await server.StartAsync();
        await using var client = await ConnectAsync(server.PipeName); using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        var initial = await ReceiveAsync(reader, "state");
        Assert.Equal(0, initial.GetProperty("state").GetProperty("targetSeconds").GetInt32());
        Assert.DoesNotContain("private-output-name", initial.GetRawText()); Assert.DoesNotContain("private-id", initial.GetRawText());
        Assert.DoesNotContain("overlay", initial.GetRawText()); Assert.Equal(1, server.ConnectedClients);
        target = 15; server.PublishState();
        var pushed = await ReceiveAsync(reader, "state"); Assert.Equal(15, pushed.GetProperty("state").GetProperty("targetSeconds").GetInt32());
    }

    [Fact]
    public async Task CommandsAreValidatedAndExpiredOrDuplicateCommandsNeverRun()
    {
        int target = 0, calls = 0;
        await using var server = new StreamDeckControlServer(() => State(target), (command, _) =>
        { calls++; target = command.Seconds ?? 60; return Task.FromResult(new DelayCommandResult(true, target, "Appliqué")); }, PipeName());
        await server.StartAsync(); await using var client = await ConnectAsync(server.PipeName);
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true); await ReceiveAsync(reader, "state");
        string id = Guid.NewGuid().ToString();
        object Command(string requestId, int value, long expires) => new { type = "command", id = requestId, command = new { action = "set", seconds = value }, expiresAt = expires };
        await SendAsync(client, Command(id, 15, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1500));
        Assert.True((await ReceiveAsync(reader, "result")).GetProperty("result").GetProperty("accepted").GetBoolean());
        await SendAsync(client, Command(id, 30, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1500));
        Assert.False((await ReceiveAsync(reader, "result")).GetProperty("result").GetProperty("accepted").GetBoolean());
        await SendAsync(client, Command(Guid.NewGuid().ToString(), 60, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 100));
        Assert.False((await ReceiveAsync(reader, "result")).GetProperty("result").GetProperty("accepted").GetBoolean());
        await SendAsync(client, Command(Guid.NewGuid().ToString(), 301, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1500));
        Assert.False((await ReceiveAsync(reader, "result")).GetProperty("result").GetProperty("accepted").GetBoolean());
        Assert.Equal(1, calls); Assert.Equal(15, target);
    }

    [Fact]
    public async Task OversizedInputDisconnectsOnlyThatClientAndNewClientsCanConnect()
    {
        await using var server = new StreamDeckControlServer(() => State(), (_, _) => throw new InvalidOperationException(), PipeName());
        await server.StartAsync();
        await using (var client = await ConnectAsync(server.PipeName))
        {
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true); await ReceiveAsync(reader, "state");
            await client.WriteAsync(Encoding.UTF8.GetBytes(new string('X', 4097) + "\n"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            Assert.Null(await reader.ReadLineAsync(timeout.Token));
        }
        await using var replacement = await ConnectAsync(server.PipeName); using var next = new StreamReader(replacement, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(1, (await ReceiveAsync(next, "state")).GetProperty("protocol").GetInt32());
    }

    [Fact]
    public async Task DisposalClosesIdleClientsAndAReplacementServerUsesTheSameName()
    {
        string name = PipeName(); var server = new StreamDeckControlServer(() => State(), (_, _) => throw new InvalidOperationException(), name);
        await server.StartAsync(); await using var client = await ConnectAsync(name);
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true); await ReceiveAsync(reader, "state");
        await server.DisposeAsync();
        await using var next = new StreamDeckControlServer(() => State(30), (_, _) => throw new InvalidOperationException(), name);
        await next.StartAsync(); await using var newClient = await ConnectAsync(name); using var newReader = new StreamReader(newClient, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(30, (await ReceiveAsync(newReader, "state")).GetProperty("state").GetProperty("targetSeconds").GetInt32());
    }

    [Fact]
    public async Task TheRealNodeClientControlsTheRealDotNetServerWithoutHttp()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        Assert.NotNull(directory);
        int target = 0, calls = 0;
        StreamDeckControlServer? server = null;
        server = new(() => State(target), (command, _) =>
        {
            calls++; target = command.Action == "adjust" ? Math.Clamp(target + command.Seconds!.Value, 0, 300) : command.Seconds ?? 60;
            server!.PublishState(); return Task.FromResult(new DelayCommandResult(true, target, "Appliqué"));
        }, PipeName());
        await using var lifetime = server;
        await server.StartAsync();
        var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(directory.FullName, "plugins", "streamdeck", "tools", "probe-pipe.mjs"));
        start.ArgumentList.Add(@"\\.\pipe\" + server.PipeName);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token), errors = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, errors); Assert.Contains("[15,25,15,0]", output);
        Assert.Equal(4, calls); Assert.Equal(0, target);
    }

    [Fact]
    public async Task ASecondInstanceCannotStealTheDeckFromTheFirstApplication()
    {
        string name = PipeName();
        await using var first = new StreamDeckControlServer(() => State(15), (_, _) => throw new InvalidOperationException(), name);
        await using var second = new StreamDeckControlServer(() => State(30), (_, _) => throw new InvalidOperationException(), name);
        await first.StartAsync(); await Assert.ThrowsAsync<IOException>(() => second.StartAsync());
        await using var client = await ConnectAsync(name); using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(15, (await ReceiveAsync(reader, "state")).GetProperty("state").GetProperty("targetSeconds").GetInt32());
    }

    [Fact]
    public async Task ShutdownCancelsAClientThatNeverCompletesTheHandshake()
    {
        var server = new StreamDeckControlServer(() => State(), (_, _) => throw new InvalidOperationException(), PipeName());
        await server.StartAsync(); await using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000); await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }
}
