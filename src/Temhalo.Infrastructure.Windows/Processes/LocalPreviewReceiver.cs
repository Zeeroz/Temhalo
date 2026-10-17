using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Temhalo.Infrastructure.Windows.Preview;

namespace Temhalo.Infrastructure.Windows.Processes;

/// <summary>Optional loopback receiver. Owns only the process it starts, never an existing listener.</summary>
public sealed class LocalPreviewReceiver(string executable, string directory, ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private NativeProcess? _process;
    private WindowsProcessJob? _job;
    private bool _disposed;

    public async Task EnsureAsync(LocalPreviewTarget target, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (await ListeningAsync(target, token).ConfigureAwait(false)) return;
            if (_process is not null) { await _process.DisposeAsync().ConfigureAwait(false); _process = null; }
            _job?.Dispose(); _job = new WindowsProcessJob();
            Directory.CreateDirectory(directory);
            string config = Path.Combine(directory, "vlc-preview.yml");
            await File.WriteAllTextAsync(config, Configuration(target), token).ConfigureAwait(false);
            _process = NativeProcess.Start(executable, [config], "Récepteur aperçu VLC", false, false, _job, logger, clearMediaMtxEnvironment: true);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (_process.HasExited) throw new IOException("Le récepteur local ne démarre pas. Vérifiez que son port est disponible.");
                if (await ListeningAsync(target, token).ConfigureAwait(false)) return;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            throw new IOException("Le récepteur local ne répond pas. Réessayez l’aperçu.");
        }
        finally { _gate.Release(); }
    }

    private static async Task<bool> ListeningAsync(LocalPreviewTarget target, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        using var client = new TcpClient(target.Address.AddressFamily);
        try { await client.ConnectAsync(target.Address, target.Port, timeout.Token).ConfigureAwait(false); return true; }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    internal static string Configuration(LocalPreviewTarget target) => $$"""
        logLevel: warn
        logDestinations: [stdout]
        logStructured: true
        readTimeout: 30s
        writeTimeout: 30s
        writeQueueSize: 512
        authMethod: internal
        authInternalUsers:
          - user: any
            ips: [127.0.0.1, "::1"]
            permissions:
              - action: publish
              - action: read
        api: false
        metrics: false
        pprof: false
        playback: false
        rtsp: false
        hls: false
        webrtc: false
        srt: false
        moq: false
        rtmp: true
        rtmpAddress: {{JsonSerializer.Serialize(target.ListenAddress)}}
        rtmpEncryption: "no"
        paths:
          all_others:
            source: publisher
            overridePublisher: false
        """;

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false); }
            finally { _process = null; _job?.Dispose(); _job = null; }
        }
        finally { _gate.Release(); }
    }
}
