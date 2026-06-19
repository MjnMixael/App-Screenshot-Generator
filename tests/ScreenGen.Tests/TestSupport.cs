using ScreenGen;

namespace ScreenGen.Tests;

internal static class TestSupport
{
    /// <summary>Walk up from the test output dir to the repo root (where the seed files live).</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "screenshots.yaml")) &&
                File.Exists(Path.Combine(dir.FullName, "targets.yaml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate repo root (screenshots.yaml + targets.yaml)");
    }

    public static string ConfigPath() => Path.Combine(RepoRoot(), "screenshots.yaml");
    public static string SeedPath() => Path.Combine(RepoRoot(), "targets.yaml");

    public static TargetDto Dto(string store, int w, int h, string cls, string sub) =>
        new() { Store = store, Width = w, Height = h, Class = cls, OutputSubfolder = sub };
}
