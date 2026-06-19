using ScreenGen;

return Cli.Run(args);

namespace ScreenGen
{
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            try
            {
                return Dispatch(args);
            }
            catch (ConfigException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        private static int Dispatch(string[] args)
        {
            string? config = null, targets = null, only = null, screen = null, output = null;
            var devices = new List<string>();
            bool dryRun = false, verbose = false, help = false, listTargets = false, validateTargets = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next(string name) =>
                    i + 1 < args.Length ? args[++i] : throw new ConfigException($"{name} requires a value");

                switch (a)
                {
                    case "generate": break;                       // the default verb
                    case "--config": config = Next(a); break;
                    case "--targets": targets = Next(a); break;
                    case "--only": only = Next(a); break;
                    case "--device": devices.Add(Next(a)); break;
                    case "--screen": screen = Next(a); break;
                    case "--output": output = Next(a); break;
                    case "--dry-run": dryRun = true; break;
                    case "-v" or "--verbose": verbose = true; break;
                    case "--list-targets": listTargets = true; break;
                    case "--validate-targets": validateTargets = true; break;
                    case "-h" or "--help": help = true; break;
                    default: throw new ConfigException($"unknown argument: {a}");
                }
            }

            if (help) { PrintHelp(); return 0; }
            if (validateTargets) return ValidateTargets(config, targets);
            if (listTargets) return ListTargets(config, targets);
            return Generate(config, targets, only, devices, screen, output, dryRun, verbose);
        }

        // --- generate -------------------------------------------------------

        private static int Generate(string? configPath, string? targetsPath, string? only,
            List<string> devices, string? screen, string? output, bool dryRun, bool verbose)
        {
            var cfg = ConfigLoader.Load(configPath ?? "screenshots.yaml");
            var catalog = TargetCatalog.Load(ResolveSeed(targetsPath, cfg), cfg.TargetDefs);

            // Selected target names: explicit --device list, else the config's targets.
            var names = devices.Count > 0 ? devices : cfg.Targets;
            var unknown = names.Where(n => !catalog.ContainsKey(n)).ToList();
            if (unknown.Count > 0)
                throw new ConfigException(
                    $"unknown target(s): {string.Join(", ", unknown)}\nknown targets: {string.Join(", ", catalog.Keys.Order())}");

            var selected = names.Select(n => catalog[n]).ToList();
            if (only is not null)
            {
                var store = ParseStore(only);
                selected = selected.Where(t => t.Store == store).ToList();
            }
            if (selected.Count == 0)
                throw new ConfigException("no targets selected after filtering");

            var screens = SelectScreens(cfg.Screens, screen);
            string outDir = output is not null ? Path.GetFullPath(output) : cfg.ResolvePath(cfg.Output.Dir);

            using var renderer = new Renderer(msg => Console.Error.WriteLine("warning: " + msg));

            var items = new List<PlanItem>();
            foreach (var t in selected)
                foreach (var (spec, index) in screens)
                    items.Add(renderer.BuildPlan(cfg, t, spec, index, outDir));

            if (dryRun)
            {
                PrintDryRun(cfg, items, outDir);
                return 0;
            }

            int missing = items.Count(i => !i.SourceExists);
            if (missing > 0)
            {
                foreach (var i in items.Where(i => !i.SourceExists).DistinctBy(i => i.SourcePath))
                    Console.Error.WriteLine($"error: source image not found: {i.SourcePath}");
                return 1;
            }

            Console.WriteLine($"Rendering {items.Count} image(s) for {selected.Count} target(s) -> {outDir}");
            foreach (var item in items)
            {
                renderer.Render(cfg, item);
                if (verbose)
                    Console.WriteLine(
                        $"  {item.Target.Name} #{item.Index:D2}  {item.Target.Width}x{item.Target.Height}  " +
                        $"frame={item.Frame}  title~{item.Layout.TitleSize:0}px  " +
                        $"device=[{item.Layout.DeviceRect.Left:0},{item.Layout.DeviceRect.Top:0} " +
                        $"{item.Layout.DeviceRect.Width:0}x{item.Layout.DeviceRect.Height:0}]  -> {item.OutputPath}");
                else
                    Console.WriteLine($"  {Path.GetRelativePath(outDir, item.OutputPath)}");
            }
            Console.WriteLine("Done.");
            return 0;
        }

        // --- list / validate ------------------------------------------------

        private static int ListTargets(string? configPath, string? targetsPath)
        {
            var cfg = TryLoadConfig(configPath);
            var catalog = TargetCatalog.Load(ResolveSeed(targetsPath, cfg), cfg?.TargetDefs);

            Console.WriteLine($"{"name",-20} {"store",-7} {"size",-12} {"class",-7} {"fit",-8} {"frame",-9} subfolder");
            foreach (var t in catalog.Values.OrderBy(t => t.Store).ThenBy(t => t.Name))
            {
                var dcfg = cfg ?? new Config();
                var frame = DeviceFrameFactory.Resolve(dcfg, t);
                Console.WriteLine($"{t.Name,-20} {t.Store.ToString().ToLowerInvariant(),-7} " +
                    $"{$"{t.Width}x{t.Height}",-12} {t.Class.ToString().ToLowerInvariant(),-7} " +
                    $"{Util.ResolveFit(t).ToString().ToLowerInvariant(),-8} {frame.ToString().ToLowerInvariant(),-9} {t.OutputSubfolder}");
            }
            return 0;
        }

        private static int ValidateTargets(string? configPath, string? targetsPath)
        {
            var cfg = TryLoadConfig(configPath);
            var catalog = TargetCatalog.Load(ResolveSeed(targetsPath, cfg), cfg?.TargetDefs);
            Console.WriteLine($"OK: {catalog.Count} target definition(s) are valid.");
            return 0;
        }

        // --- helpers --------------------------------------------------------

        private static Config? TryLoadConfig(string? configPath)
        {
            string path = configPath ?? "screenshots.yaml";
            return File.Exists(path) ? ConfigLoader.Load(path) : null;
        }

        private static string ResolveSeed(string? explicitPath, Config? cfg)
        {
            if (explicitPath is not null)
            {
                var p = Path.GetFullPath(explicitPath);
                if (!File.Exists(p)) throw new ConfigException($"targets file not found: {explicitPath}");
                return p;
            }
            if (cfg is not null)
            {
                var beside = Path.Combine(cfg.ConfigDir, "targets.yaml");
                if (File.Exists(beside)) return beside;
            }
            var bundled = Path.Combine(AppContext.BaseDirectory, "targets.yaml");
            if (File.Exists(bundled)) return bundled;
            throw new ConfigException("targets.yaml not found (looked beside config and next to the executable); pass --targets <path>");
        }

        private static Store ParseStore(string only) => only.Trim().ToLowerInvariant() switch
        {
            "apple" or "ios" => Store.Apple,
            "android" or "google" or "play" => Store.Google,
            _ => throw new ConfigException($"--only must be apple|android (got '{only}')"),
        };

        private static List<(ScreenSpec Spec, int Index)> SelectScreens(List<ScreenSpec> all, string? screen)
        {
            var indexed = all.Select((s, i) => (Spec: s, Index: i + 1)).ToList();
            if (string.IsNullOrWhiteSpace(screen)) return indexed;

            var wanted = new HashSet<int>();
            foreach (var part in screen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out int n) || n < 1 || n > all.Count)
                    throw new ConfigException($"--screen: '{part}' is not a valid screen number (1..{all.Count})");
                wanted.Add(n);
            }
            return indexed.Where(x => wanted.Contains(x.Index)).ToList();
        }

        private static void PrintDryRun(Config cfg, List<PlanItem> items, string outDir)
        {
            Console.WriteLine($"DRY RUN — {items.Count} image(s), output dir: {outDir}");
            Console.WriteLine($"project: {cfg.Project}  format: {cfg.Output.Format}");
            Console.WriteLine();
            foreach (var grp in items.GroupBy(i => i.Target.Name))
            {
                var t = grp.First().Target;
                Console.WriteLine($"[{t.Name}] {t.Width}x{t.Height} {t.Store.ToString().ToLowerInvariant()} {t.Class.ToString().ToLowerInvariant()} frame={grp.First().Frame.ToString().ToLowerInvariant()} fit={Util.ResolveFit(t).ToString().ToLowerInvariant()}");
                foreach (var i in grp)
                {
                    string warn = i.Layout.TextDeviceOverlap ? "  [!] text/device overlap" : "";
                    string miss = i.SourceExists ? "" : "  [!] SOURCE MISSING";
                    Console.WriteLine(
                        $"   #{i.Index:D2} title~{i.Layout.TitleSize:0}px " +
                        $"device=[{i.Layout.DeviceRect.Left:0},{i.Layout.DeviceRect.Top:0} {i.Layout.DeviceRect.Width:0}x{i.Layout.DeviceRect.Height:0}] " +
                        $"-> {Path.GetRelativePath(outDir, i.OutputPath)}{warn}{miss}");
                }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
@"screengen — App Store / Google Play screenshot generator

USAGE:
  screengen generate [options]
  screengen --list-targets [--config <f>] [--targets <f>]
  screengen --validate-targets [--config <f>] [--targets <f>]

OPTIONS:
  --config <path>     project config YAML (default: screenshots.yaml)
  --targets <path>    target matrix seed YAML (default: beside config, else bundled)
  --only apple|android  render only one store's targets
  --device <name>     render specific target(s); repeatable
  --screen 1,5        render only these source screens (1-based)
  --output <dir>      override output.dir
  --dry-run           print the resolved plan; write nothing
  --list-targets      print the resolved device matrix
  --validate-targets  validate target defs and exit
  -v, --verbose       log layout math while rendering
  -h, --help          show this help

Output is always flattened to RGB (no alpha) for store compliance.");
        }
    }
}
