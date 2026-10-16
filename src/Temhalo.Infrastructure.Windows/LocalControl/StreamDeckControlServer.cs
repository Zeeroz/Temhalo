using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Temhalo.Infrastructure.Windows.LocalWeb;

namespace Temhalo.Infrastructure.Windows.LocalControl;

/// <summary>Per-user Windows IPC. No browser, HTTP listener or capability link.
/// State publication is non-blocking; each client retains only its latest snapshot.</summary>
public sealed class StreamDeckControlServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<LocalViewState> _state;
    private readonly Func<DelayCommand, CancellationToken, Task<DelayCommandResult>> _command;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<long, Connection> _clients = new();
    private sealed record Snapshot(long Revision, byte[] Bytes);
    private Snapshot _latest = new(0, []);
    private long _revision;
    private Task? _listener, _heartbeat;
    private Semaphore? _instance;
    private int _disposed;
    private long _clientId;
    public string PipeName { get; }
    public int ConnectedClients => _clients.Values.Count(client => client.Ready);
    public bool Running => _listener is { IsCompleted: false };

    public static string UserPipeName => "Temhalo.StreamDeck.v1." +
        (WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Compte Windows indisponible."));

    public StreamDeckControlServer(Func<LocalViewState> state,
        Func<DelayCommand, CancellationToken, Task<DelayCommandResult>> command, string? pipeName = null)
    {
        _state = state; _command = command; PipeName = pipeName ?? UserPipeName;
        if (PipeName.Length > 200 || PipeName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
            throw new ArgumentException("Canal Stream Deck invalide.");
    }

    public Task StartAsync()
    {
        if (_listener is not null) throw new InvalidOperationException("Le canal Stream Deck est déjà démarré.");
        string name = "Local\\Temhalo.StreamDeck." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PipeName)))[..32];
        var gate = new Semaphore(1, 1, name);
        if (!gate.WaitOne(0)) { gate.Dispose(); throw new IOException("Une autre instance de Temhalo utilise le canal Stream Deck."); }
        _instance = gate;
        try
        {
            PublishState();
            var first = CreatePipe();
            _listener = AcceptAsync(first); _heartbeat = HeartbeatAsync();
        }
        catch { _instance.Release(); _instance.Dispose(); _instance = null; throw; }
        return Task.CompletedTask;
    }

    private NamedPipeServerStream CreatePipe() => new(PipeName, PipeDirection.InOut, 8,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);

    // Called by the application's state owner, independently of media buffers.
    public void PublishState()
    {
        var state = _state();
        byte[] bytes = Frame(new { type = "state", protocol = 1, state = new {
            state.Running, state.CanControl, state.TargetSeconds, state.CurrentDelayMilliseconds,
            state.Phase, state.IngestConnected, state.ProtectionSeconds,
            outputs = state.Outputs.Select(output => new { output.Ready, output.Reconnecting }).ToArray()
        }});
        if (Volatile.Read(ref _latest).Bytes.AsSpan().SequenceEqual(bytes)) return;
        var snapshot = new Snapshot(Interlocked.Increment(ref _revision), bytes);
        Volatile.Write(ref _latest, snapshot);
        foreach (var client in _clients.Values) if (client.Ready) client.SetState(snapshot);
    }

    private async Task AcceptAsync(NamedPipeServerStream first)
    {
        NamedPipeServerStream? waiting = first;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                while (_clients.Count >= 4) await Task.Delay(100, _stop.Token).ConfigureAwait(false);
                waiting ??= CreatePipe();
                await waiting.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                long id = Interlocked.Increment(ref _clientId);
                var connection = new Connection(waiting, _stop.Token); waiting = null;
                _clients[id] = connection;
                connection.Completion = ServeAsync(id, connection);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IOException) { /* The channel status in the application reports failure. */ }
        finally { if (waiting is not null) await waiting.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task HeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                foreach (var client in _clients.Values) if (client.Ready) client.Pulse();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(long id, Connection client)
    {
        Task? writer = null;
        var seen = new HashSet<string>(StringComparer.Ordinal); var recent = new Queue<string>();
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(client.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(3));
            await using var frames = ReadFramesAsync(client.Pipe, handshake.Token).GetAsyncEnumerator();
            if (!await frames.MoveNextAsync().ConfigureAwait(false)) return;
            using (var hello = JsonDocument.Parse(frames.Current))
                if (hello.RootElement.GetProperty("type").GetString() != "hello" ||
                    hello.RootElement.GetProperty("protocol").GetInt32() != 1) return;
            handshake.CancelAfter(Timeout.InfiniteTimeSpan);
            client.Ready = true; client.SetState(Volatile.Read(ref _latest));
            writer = client.WriteAsync();
            while (await frames.MoveNextAsync().ConfigureAwait(false))
            {
                using var document = JsonDocument.Parse(frames.Current);
                var message = document.RootElement;
                if (message.GetProperty("type").GetString() != "command") return;
                string requestId = message.GetProperty("id").GetString() ?? "";
                if (!Guid.TryParseExact(requestId, "D", out _)) return;
                var command = message.GetProperty("command").Deserialize<DelayCommand>(Json);
                var expires = message.GetProperty("expiresAt").GetInt64();
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                DelayCommandResult result;
                if (!seen.Add(requestId)) result = new(false, _state().TargetSeconds, "Commande déjà traitée.");
                else
                {
                    recent.Enqueue(requestId);
                    if (recent.Count > 64) seen.Remove(recent.Dequeue());
                    if (!Valid(command) || expires <= now || expires > now + 3000)
                        result = new(false, _state().TargetSeconds, "Commande invalide ou expirée.");
                    else
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(client.Token);
                        deadline.CancelAfter(TimeSpan.FromMilliseconds(expires - now));
                        try { result = await _command(command!, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { result = new(false, _state().TargetSeconds, "Commande non confirmée. Vérifiez la cible."); }
                    }
                }
                await client.ReplyAsync(Frame(new { type = "result", id = requestId, result })).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { }
        finally
        {
            client.Cancel();
            if (writer is not null) try { await writer.ConfigureAwait(false); } catch (IOException) { } catch (OperationCanceledException) { }
            _clients.TryRemove(id, out _); await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool Valid(DelayCommand? command) => command is not null && (command.Action switch
    {
        "set" => command.Seconds is >= 0 and <= 300,
        "adjust" => command.Seconds is >= -300 and <= 300,
        "protect" => true,
        _ => false
    });

    private static byte[] Frame<T>(T value)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (json.Length > 65535) throw new InvalidDataException("Message de contrôle trop volumineux.");
        byte[] bytes = new byte[json.Length + 1]; json.CopyTo(bytes, 0); bytes[^1] = (byte)'\n'; return bytes;
    }

    private static async IAsyncEnumerable<byte[]> ReadFramesAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        byte[] block = new byte[1024]; using var line = new MemoryStream(256);
        int count;
        while ((count = await stream.ReadAsync(block, token).ConfigureAwait(false)) > 0)
            for (int i = 0; i < count; i++)
            {
                if (block[i] == (byte)'\n') { yield return line.ToArray(); line.SetLength(0); }
                else { if (line.Length >= 4096) throw new InvalidDataException("Message de contrôle trop volumineux."); line.WriteByte(block[i]); }
            }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_listener is not null) await _listener.ConfigureAwait(false);
        if (_heartbeat is not null) await _heartbeat.ConfigureAwait(false);
        foreach (var client in _clients.Values) client.Cancel();
        await Task.WhenAll(_clients.Values.Select(client => client.Completion)).ConfigureAwait(false);
        _stop.Dispose();
        if (_instance is not null) { _instance.Release(); _instance.Dispose(); _instance = null; }
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly Channel<byte[]> _replies = Channel.CreateBounded<byte[]>(8);
        private Snapshot _state = new(0, []);
        public NamedPipeServerStream Pipe { get; }
        public CancellationToken Token => _stop.Token;
        public volatile bool Ready;
        public Task Completion { get; set; } = Task.CompletedTask;
        public Connection(NamedPipeServerStream pipe, CancellationToken token) { Pipe = pipe; _stop = CancellationTokenSource.CreateLinkedTokenSource(token); }
        public void Pulse() => _wake.Writer.TryWrite(0);
        public void SetState(Snapshot state)
        {
            // A new publication may race with the initial handshake snapshot.
            // Never replace a newer pending state with an older one.
            Snapshot previous;
            do { previous = Volatile.Read(ref _state); if (state.Revision <= previous.Revision) return; }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, state, previous), previous));
            Pulse();
        }
        public async ValueTask ReplyAsync(byte[] frame) { await _replies.Writer.WriteAsync(frame, Token).ConfigureAwait(false); Pulse(); }
        public void Cancel() { try { _stop.Cancel(); } catch (ObjectDisposedException) { } }
        public async Task WriteAsync()
        {
            Snapshot? sent = null;
            try
            {
                while (await _wake.Reader.WaitToReadAsync(Token).ConfigureAwait(false))
                {
                    while (_wake.Reader.TryRead(out _)) { }
                    while (_replies.Reader.TryRead(out var reply)) await WriteFrameAsync(reply).ConfigureAwait(false);
                    var state = Volatile.Read(ref _state);
                    await WriteFrameAsync(ReferenceEquals(state, sent) ? Frame(new { type = "heartbeat", protocol = 1 }) : state.Bytes).ConfigureAwait(false);
                    sent = state;
                }
            }
            finally { Cancel(); }
        }
        private async Task WriteFrameAsync(byte[] frame)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
            await Pipe.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
        }
        public async ValueTask DisposeAsync() { await Pipe.DisposeAsync().ConfigureAwait(false); _stop.Dispose(); }
    }
}
