using System.Diagnostics;
using System.IO;
using System.Text;

namespace ScreenGen.App;

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool IsReady => State == "device";
}

/// <summary>
/// Thin wrapper over the Android Debug Bridge CLI. Captures device screenshots
/// with `adb exec-out screencap -p`, which streams raw PNG bytes over USB.
/// Requires platform-tools installed and USB debugging authorized on the device.
/// </summary>
internal static class AdbService
{
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    // Executable name differs by OS; the SDK layout does not.
    private static string AdbExe => OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    /// <summary>Locate an adb executable, or null if none is runnable. Searches
    /// PATH, the ANDROID_HOME/ANDROID_SDK_ROOT env vars, and the default SDK
    /// install location for the current OS.</summary>
    public static string? FindAdb()
    {
        var candidates = new List<string> { "adb" }; // PATH

        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(root))
                candidates.Add(Path.Combine(root, "platform-tools", AdbExe));
        }

        foreach (var sdk in DefaultSdkRoots())
            candidates.Add(Path.Combine(sdk, "platform-tools", AdbExe));

        foreach (var c in candidates)
        {
            if (c != "adb" && !File.Exists(c)) continue;
            if (CanRun(c)) return c;
        }
        return null;
    }

    /// <summary>Per-OS default Android SDK locations.</summary>
    private static IEnumerable<string> DefaultSdkRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Android", "Sdk");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Android", "sdk");
        }
        else // Linux
        {
            yield return Path.Combine(home, "Android", "Sdk");
            yield return Path.Combine(home, "Android", "sdk");
        }
    }

    public static async Task<List<AdbDevice>> ListDevicesAsync(string adb)
    {
        var (stdout, _, _) = await RunAsync(adb, "devices -l");
        var text = Encoding.UTF8.GetString(stdout);
        var devices = new List<AdbDevice>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices")) continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string serial = parts[0];
            string state = parts[1];
            string model = parts.FirstOrDefault(p => p.StartsWith("model:"))?["model:".Length..]
                .Replace('_', ' ') ?? serial;
            devices.Add(new AdbDevice(serial, state, model));
        }
        return devices;
    }

    /// <summary>Capture the device's current screen as PNG bytes.</summary>
    public static async Task<byte[]> CaptureAsync(string adb, string serial)
    {
        var (stdout, stderr, exit) = await RunAsync(
            adb,
            $"-s {serial} exec-out screencap -p");

        if (exit != 0)
            throw new InvalidOperationException(
                $"adb screencap failed: {stderr.Trim()}");

        // On devices with multiple displays, adb/screencap may emit a warning
        // before the actual PNG data:
        //
        // [Warning] Multiple displays were found, but no display id was specified.
        //
        // Find the PNG signature rather than assuming it starts at byte 0.
        var pngStart = FindPngStart(stdout);

        if (pngStart < 0)
            throw new InvalidOperationException(
                "adb did not return a PNG (is the screen on / device unlocked?)");

        if (pngStart == 0)
            return stdout;

        var png = new byte[stdout.Length - pngStart];
        Buffer.BlockCopy(stdout, pngStart, png, 0, png.Length);

        return png;
    }

    private static int FindPngStart(byte[] data)
    {
        if (data.Length < PngSignature.Length)
            return -1;

        for (var i = 0; i <= data.Length - PngSignature.Length; i++)
        {
            if (data.AsSpan(i, PngSignature.Length)
                .SequenceEqual(PngSignature))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool CanRun(string adb)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(adb, "version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.WaitForExit(4000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<(byte[] stdout, string stderr, int exit)> RunAsync(string adb, string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(adb, args)
            {
                RedirectStandardOutput = true,   // read as raw bytes (binary-safe)
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        p.Start();

        using var ms = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
        string err = await p.StandardError.ReadToEndAsync();
        await copy;
        await p.WaitForExitAsync();
        return (ms.ToArray(), err, p.ExitCode);
    }
}
