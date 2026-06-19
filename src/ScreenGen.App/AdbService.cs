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

    /// <summary>Locate an adb executable, or null if none is runnable.</summary>
    public static string? FindAdb()
    {
        var candidates = new List<string> { "adb" }; // PATH

        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(root))
                candidates.Add(Path.Combine(root, "platform-tools", "adb.exe"));
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));

        foreach (var c in candidates)
        {
            if (c != "adb" && !File.Exists(c)) continue;
            if (CanRun(c)) return c;
        }
        return null;
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
        var (stdout, stderr, exit) = await RunAsync(adb, $"-s {serial} exec-out screencap -p");
        if (exit != 0)
            throw new InvalidOperationException($"adb screencap failed: {stderr.Trim()}");
        if (stdout.Length < 8 || !stdout.AsSpan(0, 8).SequenceEqual(PngSignature))
            throw new InvalidOperationException("adb did not return a PNG (is the screen on / device unlocked?)");
        return stdout;
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
