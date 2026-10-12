namespace NobreakMonitor.Service.Storage;

public static class AppPaths
{
    public static string DataDirectory { get; } = CreateDataDirectory();

    private static string CreateDataDirectory()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NobreakMonitor");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
