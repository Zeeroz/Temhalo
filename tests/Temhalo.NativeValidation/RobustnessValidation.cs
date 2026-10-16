using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Temhalo.Infrastructure.Windows.Processes;

internal sealed partial class Validation
{
    // A real wall-clock run, not an accelerated clock. Never changes the machine's network settings.
    public async Task<int> RunRobustnessAsync(int durationSeconds)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        LocalRelaySupervisor? relay = null;
        Task? running = null, sampling = null;
        var samples = new List<PerformanceSample>();
        var watch = new Stopwatch();
        try
        {
            CheckPort(1935); CheckPort(1936);
            Directory.CreateDirectory(_directory);
            Console.WriteLine("Rapports : " + _directory);
            _diagnostics = TextWriter.Synchronized(new StreamWriter(Path.Combine(_directory, "native.log")) { AutoFlush = true });
            _logger = new CaptureLogger(_diagnostics);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "native-tools.resolved.json")));
            foreach (var tool in manifest.RootElement.GetProperty("Tools").EnumerateArray())
                if (tool.GetProperty("Name").GetString() == "FFmpeg") _ffmpeg = tool.GetProperty("Executable").GetString()!;
                else if (tool.GetProperty("Name").GetString() == "MediaMTX") _mtx = tool.GetProperty("Executable").GetString()!;
            _source = Path.Combine(_directory, "source-8mbps.flv");
            await Start(_ffmpeg, ["-hide_banner", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30",
                "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "8", "-c:v", "h264_mf", "-hw_encoding", "0",
                "-b:v", "8000k", "-g", "30", "-bf", "0", "-c:a", "aac", "-b:a", "160k", "-f", "flv", "-n", _source]).RequireSuccessAsync();
            double sourceMbps = new FileInfo(_source).Length / 1_000_000d; // Eight seconds: bytes * 8 / 8s.
            Require(sourceMbps >= 7 && sourceMbps <= 10, "Le débit de la mire doit rester proche de 8 Mbit/s.");
            string sinkConfig = Path.Combine(_directory, "sink.yml");
            await File.WriteAllTextAsync(sinkConfig, """
                logLevel: warn
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
                  all_others:
                    source: publisher
                """);
            var sink = Start(_mtx, [sinkConfig]);
            await Until(() => Listening(1936), "réception locale");
            relay = new(new(_mtx, _ffmpeg, Path.Combine(root, "config", "mediamtx.yml"), Path.Combine(_directory, "soak"))
            {
                Outputs = Enumerable.Range(1, 4).Select(i => new RelayOutput("out" + i, "Local " + i,
                    new("rtmp://127.0.0.1:1936/live", "soak" + i))).ToArray(),
                FixedDelay = new() { DelaySeconds = 300, MaximumBufferBytes = 512L * 1048576 },
                RetryMinimum = TimeSpan.FromMilliseconds(300), RetryMaximum = TimeSpan.FromSeconds(2),
                ShutdownTimeout = TimeSpan.FromSeconds(20)
            }, _logger);
            running = relay.RunAsync(cancel.Token);
            await Until(() => relay.Snapshot.MediaMtxPid.HasValue, "ingest démarré");
            OwnedProcess Publisher() => Start(_ffmpeg, ["-hide_banner", "-loglevel", "warning", "-re", "-stream_loop", "-1", "-i", _source,
                "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "flv", "-flvflags", "no_duration_filesize", LocalRelayOptions.IngestUrl]);
            var publisher = Publisher();
            watch.Start();
            sampling = SamplePerformanceAsync(relay, watch, samples, cancel.Token);
            async Task Wait(Func<bool> condition, string label, int timeout = 40)
            {
                var deadline = Stopwatch.StartNew();
                while (!condition())
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    Require(!running.IsCompleted, "Le relais s'est arrêté : " + relay.Snapshot.LastEvent);
                    if (deadline.Elapsed.TotalSeconds > timeout) throw new TimeoutException(label);
                    await Task.Delay(200, cancel.Token);
                }
                _checks.Add(new { Name = label, Status = "passed", Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) });
                Console.WriteLine("  OK · " + label);
            }
            bool AllReady() => relay.Snapshot.Outputs is { Count: 4 } outputs && outputs.All(o => o.Ready);
            await Wait(AllReady, "300 secondes en RAM puis quatre sorties actives", 340);
            long fiveMinuteBytes = relay.Snapshot.Delay!.BufferBytes;
            Require(fiveMinuteBytes > 240L * 1048576 && fiveMinuteBytes < 400L * 1048576, "Budget mémoire inattendu pour cinq minutes.");
            relay.CatchUpOutputs(); relay.DelayControl.SetTarget(0);
            await Wait(() => relay.Snapshot.Delay is { LastMediaAgeMilliseconds: < 2000, Control.TargetReached: true }, "rattrapage depuis cinq minutes");
            relay.DelayControl.SetTarget(30);
            await Wait(() => relay.Snapshot.Delay is { Control.TargetReached: true, LastMediaAgeMilliseconds: >= 28000 }, "augmentation par paliers", 65);
            var before = relay.Snapshot;
            KillOwnedRelayPid(before.Outputs![0].Pid, "ffmpeg");
            await Wait(() => AllReady() && relay.Snapshot.Outputs![0].Attempt > before.Outputs[0].Attempt, "reconnexion d'un émetteur");
            Require(relay.Snapshot.Outputs!.Skip(1).Select(o => o.Pid).SequenceEqual(before.Outputs.Skip(1).Select(o => o.Pid)), "Les autres sorties ont redémarré.");
            before = relay.Snapshot;
            KillOwnedRelayPid(before.ReaderPid, "ffmpeg");
            await Wait(() => relay.Snapshot.Delay!.InputSessions > before.Delay!.InputSessions && AllReady(), "reconnexion du lecteur");
            Require(relay.Snapshot.Outputs!.Select(o => o.Pid).SequenceEqual(before.Outputs!.Select(o => o.Pid)), "La panne du lecteur a fermé une sortie.");
            await publisher.DisposeAsync();
            await Wait(() => !relay.Snapshot.IngestConnected && relay.Snapshot.Delay is { InputCompleted: true, BufferedTags: 0 }, "OBS absent : vidange puis attente", 50);
            publisher = Publisher();
            await Wait(() => relay.Snapshot.IngestConnected, "republication OBS");
            before = relay.Snapshot;
            KillOwnedRelayPid(before.MediaMtxPid, "mediamtx");
            await publisher.DisposeAsync();
            await Wait(() => relay.Snapshot.MediaMtxPid.HasValue && relay.Snapshot.MediaMtxPid != before.MediaMtxPid, "redémarrage MediaMTX");
            publisher = Publisher();
            await Wait(() => relay.Snapshot.IngestConnected, "reprise après panne ingest");
            await sink.DisposeAsync();
            long readBefore = relay.Snapshot.Delay!.ReadTags;
            await Wait(() => relay.Snapshot.Outputs!.All(o => o.Reconnecting), "quatre destinations indisponibles");
            await Task.Delay(TimeSpan.FromSeconds(30), cancel.Token);
            Require(relay.Snapshot.Delay!.ReadTags > readBefore + 500 && relay.Snapshot.IngestConnected && !running.IsCompleted,
                "L'entrée doit continuer pendant la panne des destinations.");
            sink = Start(_mtx, [sinkConfig]);
            await Wait(AllReady, "retour des quatre destinations", 50);
            relay.CatchUpOutputs(); relay.DelayControl.SetTarget(0);
            await Wait(() => relay.Snapshot.Delay is { LastMediaAgeMilliseconds: < 2000, Control.TargetReached: true }, "suppression du retard après panne");
            int[] targets = [15, 30, 60, 0]; int index = 0;
            while (watch.Elapsed.TotalSeconds < durationSeconds - 12)
            {
                int target = targets[index++ % targets.Length];
                if (target < relay.DelayControl.Request.TargetSeconds) relay.CatchUpOutputs();
                relay.DelayControl.SetTarget(target);
                await Wait(() => relay.Snapshot.Delay!.Control.TargetReached && AllReady(), "cible " + target + " s", 90);
                double rest = Math.Min(30, durationSeconds - 12 - watch.Elapsed.TotalSeconds);
                if (rest > 0) await Task.Delay(TimeSpan.FromSeconds(rest), cancel.Token);
            }
            relay.CatchUpOutputs(); relay.DelayControl.SetTarget(0);
            await Wait(() => relay.Snapshot.Delay is { LastMediaAgeMilliseconds: < 2000, Control.TargetReached: true } && AllReady(), "fin au minimum local");
            var captures = Enumerable.Range(1, 4).Select(i =>
            {
                string file = Path.Combine(_directory, "final-" + i + ".flv");
                return (File: file, Process: Start(_ffmpeg, ["-hide_banner", "-v", "error", "-rtmp_live", "live", "-i",
                    "rtmp://127.0.0.1:1936/live/soak" + i, "-t", "4", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "flv", "-n", file]));
            }).ToArray();
            foreach (var capture in captures) await capture.Process.RequireSuccessAsync();
            if (watch.Elapsed.TotalSeconds < durationSeconds) await Task.Delay(TimeSpan.FromSeconds(durationSeconds - watch.Elapsed.TotalSeconds), cancel.Token);
            double elapsed = watch.Elapsed.TotalSeconds;
            await publisher.DisposeAsync(); cancel.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30)); await sampling;
            foreach (var capture in captures) { await Decode(capture.File); await Inspect(capture.File); }
            Require(relay.Snapshot.Delay!.BufferBytes == 0, "Références conservées après arrêt.");
            var steady = samples.Where(s => s.AllReady).ToArray();
            Require(steady.Length > 10, "Pas assez de mesures en émission.");
            double cpuMean = steady.Average(s => s.TotalCpuPercent), cpu95 = steady.OrderBy(s => s.TotalCpuPercent).ElementAt((int)(steady.Length * .95)).TotalCpuPercent;
            Require(cpuMean < 5, "Le CPU moyen du relais et de ses processus dépasse 5 %.");
            await File.WriteAllTextAsync(Path.Combine(_directory, "robustness-result.json"), JsonSerializer.Serialize(new
            {
                Stage = 6, Status = "passed", RequestedSeconds = durationSeconds, ElapsedSeconds = elapsed,
                CheckedUtc = DateTime.UtcNow, SourceMbps = sourceMbps, Destinations = 4, InitialDelaySeconds = 300,
                FiveMinuteBufferBytes = fiveMinuteBytes, PeakLogicalBufferBytes = samples.Max(s => s.BufferBytes),
                FinalBufferBytes = relay.Snapshot.Delay.BufferBytes, LogicalProcessors = Environment.ProcessorCount,
                CpuMeanPercent = cpuMean, CpuP95Percent = cpu95, PeakWorkingSetBytes = samples.Max(s => s.WorkingSetBytes),
                CpuScope = "Validation host plus ingest MediaMTX, FFmpeg reader and four emitters; synthetic publisher, receiver and capture excluded",
                Checks = _checks, FinalFilesDecoded = captures.Select(c => Path.GetFileName(c.File)).ToArray(),
                PublicPlatformsTested = false, AudioVisualLipSyncManuallyChecked = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Robustesse réussie : " + Path.Combine(_directory, "robustness-result.json"));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Échec robustesse : " + ex.Message); return 1; }
        finally
        {
            cancel.Cancel();
            if (running is not null) try { await running.WaitAsync(TimeSpan.FromSeconds(35)); } catch (Exception) { }
            if (sampling is not null) try { await sampling; } catch (OperationCanceledException) { }
            foreach (var child in _children) await child.DisposeAsync();
            await _diagnostics.DisposeAsync(); Console.CancelKeyPress -= handler;
        }
    }

    private sealed record PerformanceSample(double Seconds, double TotalCpuPercent, long WorkingSetBytes, long BufferBytes, bool AllReady);
    private async Task SamplePerformanceAsync(LocalRelaySupervisor relay, Stopwatch watch, List<PerformanceSample> samples, CancellationToken token)
    {
        using var csv = new StreamWriter(Path.Combine(_directory, "performance.csv")) { AutoFlush = true };
        await csv.WriteLineAsync("seconds,cpu_percent,working_set_bytes,logical_buffer_bytes,all_ready");
        var previous = new Dictionary<int, TimeSpan>(); double previousTime = 0;
        try
        {
            while (true)
            {
                await Task.Delay(1000, token);
                var snapshot = relay.Snapshot;
                int[] ids = new int?[] { Environment.ProcessId, snapshot.MediaMtxPid, snapshot.ReaderPid }
                    .Concat(snapshot.Outputs?.Select(o => o.Pid) ?? []).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
                double cpu = 0; long memory = 0;
                foreach (int id in ids)
                {
                    try
                    {
                        using var process = Process.GetProcessById(id);
                        TimeSpan total = process.TotalProcessorTime; memory += process.WorkingSet64;
                        if (previous.TryGetValue(id, out var last)) cpu += Math.Max(0, (total - last).TotalSeconds);
                        previous[id] = total;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
                double now = watch.Elapsed.TotalSeconds;
                var sample = new PerformanceSample(now, cpu / (now - previousTime) / Environment.ProcessorCount * 100,
                    memory, snapshot.Delay?.BufferBytes ?? 0, snapshot.Outputs is { Count: 4 } outputs && outputs.All(o => o.Ready));
                previousTime = now; samples.Add(sample);
                await csv.WriteLineAsync(FormattableString.Invariant($"{now:F3},{sample.TotalCpuPercent:F3},{memory},{sample.BufferBytes},{sample.AllReady}"));
                if (samples.Count % 30 == 0) Console.WriteLine(FormattableString.Invariant($"  {now:F0}s · {sample.BufferBytes / 1048576d:F1} MiB de buffer · CPU {sample.TotalCpuPercent:F2}% · {snapshot.State}"));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
