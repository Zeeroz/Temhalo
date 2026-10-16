namespace Temhalo.App.Services;

internal static class AppStorage
{
    public static string DataDirectory { get; } = ReadDataDirectory();
    private static string ReadDataDirectory()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--data-directory");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temhalo");
    }
}
