using Temhalo.Infrastructure.Windows.Logging;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class SafeFileLoggerTests
{
    [Fact]
    public void RotationRetainsBoundedFilesAndRedactsPropertiesAndExceptions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Temhalo-log-tests-" + Guid.NewGuid().ToString("N"));
        string key = "fake-secret-" + Guid.NewGuid().ToString("N");
        var redactor = new SecretRedactor(); redactor.Remember(key);
        try
        {
            using (var log = SafeFileLogger.Create(directory, maximumFileBytes: 1024, retainedFiles: 3, redactor: redactor))
                for (int i = 0; i < 100; i++)
                    log.Error(new IOException("failure " + key), "Diagnostic {Number} {Key} {Url}", i, key, "rtmp://example.test/live/" + key);
            string[] files = Directory.GetFiles(directory, "*.log");
            Assert.Equal(3, files.Length);
            foreach (string file in files)
            {
                string content = File.ReadAllText(file);
                Assert.DoesNotContain(key, content); Assert.DoesNotContain("rtmp://", content);
                Assert.Contains("[redacted]", content);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
