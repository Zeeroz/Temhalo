using System.Text.Json;
using Temhalo.Infrastructure.Windows.Processes;

internal sealed partial class Validation
{
    public async Task<int> RunMultistreamAsync()
    {
        try
        {
            CheckPort(1935);
            Directory.CreateDirectory(_directory);
            _diagnostics = TextWriter.Synchronized(new StreamWriter(Path.Combine(_directory, "native.log")) { AutoFlush = true });
            _logger = new CaptureLogger(_diagnostics);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "native-tools.resolved.json")));
            foreach (var tool in manifest.RootElement.GetProperty("Tools").EnumerateArray())
                if (tool.GetProperty("Name").GetString() == "FFmpeg") _ffmpeg = tool.GetProperty("Executable").GetString()!;
                else if (tool.GetProperty("Name").GetString() == "MediaMTX") _mtx = tool.GetProperty("Executable").GetString()!;
            _source = Path.Combine(_directory, "source.flv");
            Console.WriteLine("Mire H.264/AAC puis relais réel vers deux fichiers indépendants.");
            var fixture = Start(_ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "8", "-c:v", "h264_mf", "-hw_encoding", "0", "-b:v", "1500k", "-g", "30", "-bf", "0", "-c:a", "aac", "-b:a", "160k", "-f", "flv", "-n", _source]);
            await fixture.RequireSuccessAsync();
            var relay = new LocalRelaySupervisor(new(_mtx, _ffmpeg, Path.Combine(root, "config", "mediamtx.yml"), Path.Combine(_directory, "multistream"))
            {
                Outputs = [new("a", "Première"), new("b", "Seconde")],
                FixedDelay = new() { DelaySeconds = 2, MaximumBufferBytes = 32 * 1048576 },
                RetryMinimum = TimeSpan.FromMilliseconds(250), RetryMaximum = TimeSpan.FromSeconds(1), ShutdownTimeout = TimeSpan.FromSeconds(20)
            }, _logger);
            using var stop = new CancellationTokenSource();
            Task running = relay.RunAsync(stop.Token);
            try
            {
                await Until(() => relay.Snapshot.MediaMtxPid.HasValue, "ingest démarré");
                var publisher = Publish();
                await Until(() => relay.Snapshot.Outputs is { Count: 2 } outputs && outputs.All(output => output.Ready), "deux sorties actives");
                var initial = relay.Snapshot.Outputs!;
                int? healthyPid = initial[1].Pid;
                KillOwnedRelayPid(initial[0].Pid, "ffmpeg");
                await Until(() => relay.Snapshot.Outputs![0] is { Attempt: > 1, Ready: true }, "première sortie reconnectée");
                Require(relay.Snapshot.Outputs![1].Pid == healthyPid && relay.Snapshot.Outputs[1].Attempt == 1,
                    "La seconde sortie ne doit pas redémarrer lors de la panne de la première.");
                relay.DelayControl.SetTarget(4);
                await Until(() => relay.Snapshot.Delay is { Control.TargetReached: true, LastMediaAgeMilliseconds: >= 3500 }, "délai commun de quatre secondes");
                var beforeTrim = relay.Snapshot.Outputs!;
                relay.CatchUpOutputs(); relay.DelayControl.SetTarget(0);
                await Until(() => relay.Snapshot.Delay is { Control.TargetReached: true, LastMediaAgeMilliseconds: < 2000 }, "retour à zéro sur image clé");
                Require(relay.Snapshot.Outputs![0].Pid == beforeTrim[0].Pid && relay.Snapshot.Outputs[1].Pid == healthyPid,
                    "Un changement de délai ne doit pas recréer les émetteurs sains.");
                await publisher.DisposeAsync();
                stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30));
                string first = relay.Snapshot.OutputFile!;
                string second = Path.Combine(_directory, "multistream", "output-b", "relay-0001.flv");
                await Decode(first); await Inspect(first); await Decode(second); await Inspect(second);
                Require(relay.Snapshot.Delay!.BufferBytes == 0, "Les références partagées doivent être libérées après arrêt.");
                await NetworkFanoutAsync();
                await File.WriteAllTextAsync(Path.Combine(_directory, "multistream-result.json"), JsonSerializer.Serialize(new
                {
                    Stage = "5bis-services", Status = "passed", CheckedUtc = DateTime.UtcNow,
                    HealthyOutputKeptSameProcess = true, ZeroKeptHealthyConnections = true,
                    FirstOutputAttempts = relay.Snapshot.Outputs![0].Attempt,
                    FinalBufferBytes = relay.Snapshot.Delay.BufferBytes,
                    TwoLocalRtmpDestinationsDecoded = true,
                    DecodedFiles = new[] { first, second }, Outputs = relay.Snapshot.Outputs
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Validation multistream réussie : " + Path.Combine(_directory, "multistream-result.json"));
                return 0;
            }
            finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30)); }
        }
        catch (Exception ex) { Console.Error.WriteLine("Échec : " + ex.Message); return 1; }
        finally { foreach (var child in _children) await child.DisposeAsync(); await _diagnostics.DisposeAsync(); }
    }

    private async Task NetworkFanoutAsync()
    {
        CheckPort(1936);
        string sinkConfig = Path.Combine(_directory, "sink.yml");
        await File.WriteAllTextAsync(sinkConfig, """
            logLevel: info
            logDestinations: [stdout]
            rtsp: false
            hls: false
            webrtc: false
            srt: false
            moq: false
            rtmp: true
            rtmpAddress: 127.0.0.1:1936
            authMethod: internal
            authInternalUsers:
              - user: any
                ips: [127.0.0.1]
                permissions:
                  - action: publish
                  - action: read
            paths:
              live/preview-a:
                source: publisher
              live/preview-b:
                source: publisher
            """);
        var sink = Start(_mtx, [sinkConfig]);
        await Until(() => Listening(1936), "serveur RTMP de réception local");
        var relay = new LocalRelaySupervisor(new(_mtx, _ffmpeg, Path.Combine(root, "config", "mediamtx.yml"), Path.Combine(_directory, "network"))
        {
            Outputs = [new("a", "RTMP A", new("rtmp://127.0.0.1:1936/live", "preview-a")),
                new("b", "RTMP B", new("rtmp://127.0.0.1:1936/live", "preview-b"))],
            FixedDelay = new() { DelaySeconds = 1, MaximumBufferBytes = 32 * 1048576 }, ShutdownTimeout = TimeSpan.FromSeconds(20)
        }, _logger);
        using var stop = new CancellationTokenSource();
        Task running = relay.RunAsync(stop.Token);
        try
        {
            await Until(() => relay.Snapshot.MediaMtxPid.HasValue, "ingest du test RTMP");
            var publisher = Publish();
            await Until(() => relay.Snapshot.Outputs is { Count: 2 } outputs && outputs.All(output => output.Ready), "deux publications RTMP locales");
            var captures = new List<(OwnedProcess Process, string File)>();
            foreach (string name in new[] { "a", "b" })
            {
                string file = Path.Combine(_directory, "received-" + name + ".flv");
                var capture = Start(_ffmpeg, ["-hide_banner", "-loglevel", "error", "-rtmp_live", "live", "-i",
                    "rtmp://127.0.0.1:1936/live/preview-" + name, "-t", "4", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "flv", "-n", file]);
                captures.Add((capture, file));
            }
            foreach (var capture in captures) { await capture.Process.RequireSuccessAsync(); await Decode(capture.File); await Inspect(capture.File); }
            await publisher.DisposeAsync(); stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30));
            await sink.DisposeAsync(); Console.WriteLine("  OK · deux flux RTMP reçus et décodés en H.264/AAC");
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30)); }
    }
}
