using System.IO;

namespace ScreenGen.App;

/// <summary>Locates the seed files (targets.yaml / screenshots.yaml) for the app,
/// searching the working directory upward, then falling back to the exe folder.</summary>
internal static class AppEnv
{
    public static string? FindUp(string fileName)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, fileName);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }

    public static string ResolveSeed(string? configDir)
    {
        if (configDir is not null)
        {
            var beside = Path.Combine(configDir, "targets.yaml");
            if (File.Exists(beside)) return beside;
        }
        return FindUp("targets.yaml") ?? Path.Combine(AppContext.BaseDirectory, "targets.yaml");
    }
}
