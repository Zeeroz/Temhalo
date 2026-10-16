using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Temhalo.Infrastructure.Windows.Logging;

public static class SafeFileLogger
{
    public static Logger Create(string directory, ILogEventSink? liveSink = null, string prefix = "temhalo-",
        long maximumFileBytes = 10 * 1024 * 1024, int retainedFiles = 7, SecretRedactor? redactor = null)
    {
        if (maximumFileBytes < 512 || retainedFiles < 1) throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        Directory.CreateDirectory(directory);
        var configuration = new LoggerConfiguration().MinimumLevel.Information();
        if (liveSink is not null) configuration.WriteTo.Sink(liveSink);
        return configuration.WriteTo.File(new RedactingFormatter(redactor ?? SecretRedactor.Shared),
            Path.Combine(directory, prefix + ".log"), rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: maximumFileBytes, rollOnFileSizeLimit: true,
            retainedFileCountLimit: retainedFiles, shared: true).CreateLogger();
    }

    private sealed class RedactingFormatter(SecretRedactor redactor) : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter _inner = new("[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}", CultureInfo.GetCultureInfo("fr-FR"));
        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var buffer = new StringWriter(CultureInfo.InvariantCulture);
            _inner.Format(logEvent, buffer);
            output.WriteLine(redactor.Redact(buffer.ToString().TrimEnd()));
        }
    }
}
