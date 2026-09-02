using System.Diagnostics;
using System.IO;
using System.Text;

namespace ScreenGen.App;

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool IsReady => State == "device";
}

/// <summary>
/// Information about the display Android currently considers active.
///
/// PhysicalId is the physical display ID required by `screencap -d`.
/// LogicalId is Android's logical display ID, such as "0" -- informational
/// only, so a dump that omits it does not cost us the display.
/// </summary>
public sealed record ActiveDisplay(string? LogicalId, string PhysicalId);

/// <summary>
/// Thin wrapper over the Android Debug Bridge CLI.
///
/// Handles:
/// - Finding adb
/// - Detecting connected Android devices
/// - Determining the currently active physical display
/// - Capturing the active display as PNG bytes
///
/// This is particularly important for foldable devices such as the
/// Samsung Galaxy Z Fold, which exposes multiple physical displays.
/// </summary>
internal static class AdbService
{
    private static readonly byte[] PngSignature =
    {
        137, 80, 78, 71, 13, 10, 26, 10
    };

    // Executable name differs by OS; the SDK layout does not.
    private static string AdbExe =>
        OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    /// <summary>
    /// Locate an adb executable, or null if none is runnable.
    ///
    /// Searches:
    /// 1. PATH
    /// 2. ANDROID_HOME
    /// 3. ANDROID_SDK_ROOT
    /// 4. Default Android SDK locations for the current OS
    /// </summary>
    public static string? FindAdb()
    {
        var candidates = new List<string>
        {
            "adb"
        };

        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = Environment.GetEnvironmentVariable(env);

            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(
                    Path.Combine(root, "platform-tools", AdbExe));
            }
        }

        foreach (var sdk in DefaultSdkRoots())
        {
            candidates.Add(
                Path.Combine(sdk, "platform-tools", AdbExe));
        }

        foreach (var candidate in candidates)
        {
            // "adb" means we're relying on PATH.
            // For explicit paths, make sure the file exists first.
            if (candidate != "adb" && !File.Exists(candidate))
                continue;

            if (CanRun(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Per-OS default Android SDK locations.
    /// </summary>
    private static IEnumerable<string> DefaultSdkRoots()
    {
        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            var localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            yield return Path.Combine(
                localAppData,
                "Android",
                "Sdk");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(
                home,
                "Library",
                "Android",
                "sdk");
        }
        else
        {
            // Linux
            yield return Path.Combine(
                home,
                "Android",
                "Sdk");

            yield return Path.Combine(
                home,
                "Android",
                "sdk");
        }
    }

    /// <summary>
    /// List Android devices currently known to adb.
    /// </summary>
    public static async Task<List<AdbDevice>> ListDevicesAsync(
        string adb)
    {
        var (stdout, _, _) =
            await RunAsync(adb, "devices -l");

        var text = Encoding.UTF8.GetString(stdout);

        var devices = new List<AdbDevice>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 ||
                line.StartsWith(
                    "List of devices",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            var serial = parts[0];
            var state = parts[1];

            var model =
                parts
                    .FirstOrDefault(
                        p => p.StartsWith(
                            "model:",
                            StringComparison.Ordinal))
                    ?["model:".Length..]
                    .Replace('_', ' ')
                ?? serial;

            devices.Add(
                new AdbDevice(
                    serial,
                    state,
                    model));
        }

        return devices;
    }

    /// <summary>
    /// Which physical display Android currently has active, or null if this
    /// device will not tell us. Never throws -- callers treat "don't know" as
    /// a normal answer and let screencap choose.
    ///
    /// Android exposes both a logical display ID and a physical display
    /// ID. The logical ID is not necessarily valid for screencap -d, so we
    /// take the physical one out of uniqueId, which is what `screencap -d`
    /// expects and saves a second dumpsys call to SurfaceFlinger.
    /// </summary>
    public static async Task<ActiveDisplay?> TryGetActiveDisplayAsync(
        string adb,
        string serial)
    {
        try
        {
            var (stdout, _, exit) =
                await RunAsync(
                    adb,
                    $"-s {serial} shell dumpsys display");

            return exit == 0
                ? ParseActiveDisplay(Encoding.UTF8.GetString(stdout))
                : null;
        }
        catch
        {
            // A device we cannot read is one we capture the old way, not one
            // we refuse to capture.
            return null;
        }
    }

    /// <summary>
    /// Pick the active internal display out of `dumpsys display` output.
    /// Returns null if this device's dump does not say, which is a normal
    /// answer -- the format is a debug dump, not an API, and it varies by
    /// Android version and vendor.
    /// </summary>
    private static ActiveDisplay? ParseActiveDisplay(string text)
    {
        /*
         * The useful portion of dumpsys display looks approximately like:
         *
         * DisplayViewport{
         *     type=INTERNAL,
         *     valid=true,
         *     isActive=true,
         *     displayId=0,
         *     uniqueId='local:4630946481096930692',
         *     ...
         * }
         *
         * The entire DisplayViewport may span multiple lines, so we cannot
         * simply inspect one line for all of the values.
         *
         * Instead, find each DisplayViewport block and inspect it as a unit.
         */

        var viewportStart = 0;

        while (true)
        {
            var start = text.IndexOf(
                "DisplayViewport{",
                viewportStart,
                StringComparison.Ordinal);

            if (start < 0)
                break;

            var end = text.IndexOf(
                '}',
                start);

            if (end < 0)
                break;

            var viewport =
                text[start..(end + 1)];

            viewportStart = end + 1;

            // We only care about the active viewport -- and only about a
            // built-in panel. A cast, a screen recorder or Android Auto can
            // put an active VIRTUAL/EXTERNAL viewport in this list, and
            // capturing that instead of the phone screen is not what anyone
            // pressing "Capture" meant.
            if (!viewport.Contains(
                    "isActive=true",
                    StringComparison.Ordinal) ||
                !viewport.Contains(
                    "type=INTERNAL",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var uniqueId =
                ExtractQuotedValue(
                    viewport,
                    "uniqueId='");

            if (string.IsNullOrWhiteSpace(uniqueId))
                continue;

            const string localPrefix = "local:";

            if (!uniqueId.StartsWith(
                    localPrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var physicalId =
                uniqueId[localPrefix.Length..];

            if (string.IsNullOrWhiteSpace(physicalId))
                continue;

            return new ActiveDisplay(
                ExtractValue(viewport, "displayId=", ','),
                physicalId);
        }

        return null;
    }

    /// <summary>
    /// Capture the currently active physical display as PNG bytes.
    ///
    /// The active display is resolved immediately before each capture, so
    /// opening or closing a foldable between captures is handled on its own.
    /// Naming the display is what stops a multi-display phone from handing us
    /// whichever panel screencap enumerated first, but it depends on reading
    /// `dumpsys display`. When that comes back in a shape we do not recognise
    /// we let screencap pick, which is what every single-display phone did
    /// before any of this -- an unreadable dump must not cost someone their
    /// screenshot.
    /// </summary>
    public static async Task<byte[]> CaptureAsync(
        string adb,
        string serial)
    {
        var display =
            await TryGetActiveDisplayAsync(
                adb,
                serial);

        var which =
            display is null
                ? "the default display"
                : $"display {display.PhysicalId}";

        var (stdout, stderr, exit) =
            await RunAsync(
                adb,
                display is null
                    ? $"-s {serial} exec-out screencap -p"
                    : $"-s {serial} exec-out screencap -p -d {display.PhysicalId}");

        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"adb screencap failed for {which}: {stderr.Trim()}");
        }

        // `exec-out` merges the device's stderr into the same stream as its
        // stdout, so screencap's own warnings arrive ahead of the image --
        // notably the multi-display warning on a foldable when no display was
        // named. Find the signature rather than assuming byte 0.
        var pngStart = FindPngStart(stdout);

        if (pngStart < 0)
        {
            throw new InvalidOperationException(
                $"adb did not return a PNG for {which} " +
                $"(is the screen on / device unlocked?)");
        }

        if (pngStart == 0)
            return stdout;

        var png = new byte[stdout.Length - pngStart];

        Buffer.BlockCopy(
            stdout,
            pngStart,
            png,
            0,
            png.Length);

        return png;
    }

    /// <summary>
    /// Index of the PNG signature within a capture stream, or -1 if there
    /// isn't one.
    /// </summary>
    private static int FindPngStart(byte[] data) =>
        data.AsSpan().IndexOf(PngSignature);

    /// <summary>
    /// Extract an unquoted value from a string.
    ///
    /// Example:
    /// displayId=0,
    /// returns "0".
    /// </summary>
    private static string? ExtractValue(
        string text,
        string prefix,
        char terminator)
    {
        var start =
            text.IndexOf(
                prefix,
                StringComparison.Ordinal);

        if (start < 0)
            return null;

        start += prefix.Length;

        var end =
            text.IndexOf(
                terminator,
                start);

        if (end < 0)
            return null;

        return text[start..end].Trim();
    }

    /// <summary>
    /// Extract a single-quoted value.
    ///
    /// Example:
    /// uniqueId='local:4630946481096930692'
    ///
    /// returns:
    /// local:4630946481096930692
    /// </summary>
    private static string? ExtractQuotedValue(
        string text,
        string prefix)
    {
        var start =
            text.IndexOf(
                prefix,
                StringComparison.Ordinal);

        if (start < 0)
            return null;

        start += prefix.Length;

        var end =
            text.IndexOf(
                '\'',
                start);

        if (end < 0)
            return null;

        return text[start..end].Trim();
    }

    /// <summary>
    /// Verify that adb can be launched.
    /// </summary>
    private static bool CanRun(string adb)
    {
        try
        {
            using var process =
                Process.Start(
                    new ProcessStartInfo(
                        adb,
                        "version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });

            if (process is null)
                return false;

            process.WaitForExit(4000);

            return process.HasExited &&
                   process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run an adb command and return its raw stdout bytes, stderr text,
    /// and exit code.
    ///
    /// stdout is intentionally handled as bytes because screencap produces
    /// binary PNG data.
    /// </summary>
    private static async Task<(
        byte[] stdout,
        string stderr,
        int exit)> RunAsync(
            string adb,
            string args)
    {
        using var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo(
                        adb,
                        args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
            };

        process.Start();

        using var memoryStream =
            new MemoryStream();

        var stdoutTask =
            process.StandardOutput
                .BaseStream
                .CopyToAsync(memoryStream);

        var stderrTask =
            process.StandardError
                .ReadToEndAsync();

        await Task.WhenAll(
            stdoutTask,
            stderrTask);

        await process.WaitForExitAsync();

        return (
            memoryStream.ToArray(),
            await stderrTask,
            process.ExitCode);
    }
}
