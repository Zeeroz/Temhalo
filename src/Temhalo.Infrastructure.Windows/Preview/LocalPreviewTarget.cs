using System.Diagnostics;
using System.Net;
using Microsoft.Win32;

namespace Temhalo.Infrastructure.Windows.Preview;

/// <summary>Only an explicit loopback output can be opened in an external player.</summary>
public sealed class LocalPreviewTarget
{
    public IPAddress Address { get; }
    public int Port { get; }
    public string StreamPath { get; }
    private string PlaybackUrl => $"rtmp://{Host}:{Port}/{StreamPath}";
    private string Host => Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{Address}]" : Address.ToString();
    public string ListenAddress => $"{Host}:{Port}";

    private LocalPreviewTarget(IPAddress address, int port, string path)
    { Address = address; Port = port; StreamPath = path; }

    public static LocalPreviewTarget? TryCreate(string serverUrl, string streamKey)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var server) || server.Scheme != "rtmp" ||
            server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0 ||
            serverUrl.Any(char.IsWhiteSpace)) return null;
        IPAddress? address;
        if (server.DnsSafeHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)) address = IPAddress.Loopback;
        else if (!IPAddress.TryParse(server.DnsSafeHost, out address) || !IPAddress.IsLoopback(address)) return null;
        // Do not accidentally preview the OBS ingest or take over a system service.
        int port = server.Port < 0 ? 1935 : server.Port;
        if (port is < 1024 or > 65535 or 1935) return null;
        string path = server.AbsolutePath.Trim('/') + "/" + streamKey;
        path = path.TrimStart('/');
        if (path.Length is < 1 or > 1024 || path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or '~')))) return null;
        return new(address, port, path);
    }

    public ProcessStartInfo VlcStartInfo(string executable)
    {
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal };
        foreach (string value in new[] { "--no-one-instance", "--no-video-title-show", "--network-caching=150",
            "--clock-jitter=0", "--avio-options={rtmp_buffer=0}", PlaybackUrl }) start.ArgumentList.Add(value);
        return start;
    }

    public static string? FindVlc()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\vlc.exe");
                if (key?.GetValue(null) is string path && ValidExecutable(path.Trim('"')) is { } found) return found;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            if (ValidExecutable(Path.Combine(Environment.GetFolderPath(folder), "VideoLAN", "VLC", "vlc.exe")) is { } found) return found;
        return null;
    }
    private static string? ValidExecutable(string path)
        => Path.IsPathFullyQualified(path) && Path.GetFileName(path).Equals("vlc.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;

    public override string ToString() => "Aperçu RTMP local [adresse masquée]";
}
