using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Temhalo.Infrastructure.Windows.Logging;

namespace Temhalo.Infrastructure.Windows.Processes;

internal sealed class NativeProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly Task[] _diagnostics;
    private readonly bool _hasInput;
    public string Name { get; }
    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public Task Exited { get; }
    public Stream Output => _process.StandardOutput.BaseStream;
    public Stream Input => _process.StandardInput.BaseStream;

    private NativeProcess(Process process, string name, bool hasInput, bool binaryOutput, ILogger logger,
        Action<string>? diagnostic, string? sensitiveValue)
    {
        _process = process;
        Name = name;
        _logger = logger;
        _hasInput = hasInput;
        Exited = process.WaitForExitAsync();
        _diagnostics = binaryOutput
            ? [DrainAsync(process.StandardError, diagnostic, sensitiveValue)]
            : [DrainAsync(process.StandardError, diagnostic, sensitiveValue), DrainAsync(process.StandardOutput, diagnostic, sensitiveValue)];
        logger.LogInformation("{Role} démarré, PID {ProcessId}", name, process.Id);
    }

    public static NativeProcess Start(string executable, IEnumerable<string> arguments, string name,
        bool hasInput, bool binaryOutput, WindowsProcessJob job, ILogger logger,
        bool clearMediaMtxEnvironment = false, Action<string>? diagnostic = null, string? sensitiveValue = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = hasInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        if (clearMediaMtxEnvironment)
        {
            // Machine-level MTX_* overrides must not reopen protocols disabled in our local config.
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("MTX_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
        }
        // FFREPORT can create an independent, unsanitized argument/diagnostic log.
        start.Environment.Remove("FFREPORT");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Native process failed to start.");
            job.Assign(process);
            return new NativeProcess(process, name, hasInput, binaryOutput, logger, diagnostic, sensitiveValue);
        }
        catch
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose();
            throw;
        }
    }

    private async Task DrainAsync(StreamReader reader, Action<string>? diagnostic, string? sensitiveValue)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrEmpty(sensitiveValue)) line = line.Replace(sensitiveValue, "[redacted]", StringComparison.Ordinal);
                string clean = NativeLogSanitizer.Sanitize(line);
                diagnostic?.Invoke(clean);
                // Progress is telemetry, not a log event at every refresh.
                if (!IsProgress(line))
                    _logger.LogInformation("{Role}: {NativeMessage}", Name, clean);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static bool IsProgress(string line)
    {
        int equals = line.IndexOf('=');
        return equals > 0 && line[..equals] is "frame" or "fps" or "stream_0_0_q" or "bitrate" or "total_size" or
            "out_time_us" or "out_time_ms" or "out_time" or "dup_frames" or "drop_frames" or "speed" or "progress";
    }

    public void CloseInput()
    {
        if (!_hasInput) return;
        try { _process.StandardInput.Close(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async Task StopAsync(bool sendQuit, TimeSpan grace)
    {
        if (HasExited) return;
        if (sendQuit && _hasInput)
        {
            try
            {
                // Interactive FFmpeg input only; never send q into the emitter's FLV stdin.
                await _process.StandardInput.WriteLineAsync("q").WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { }
        }
        try { await Exited.WaitAsync(grace).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            _logger.LogWarning("{Role}: délai d'arrêt dépassé, terminaison du processus", Name);
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await Exited.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            CloseInput();
            await StopAsync(false, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            await Task.WhenAll(_diagnostics).ConfigureAwait(false);
            _logger.LogInformation("{Role} terminé, PID {ProcessId}, code {ExitCode}", Name, Id, _process.ExitCode);
        }
        finally { _process.Dispose(); }
    }
}
