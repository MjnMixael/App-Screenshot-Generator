using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SkiaSharp;

namespace ScreenGen.App;

public sealed class MainViewModel : ObservableObject
{
    private readonly Renderer _renderer = new();
    private bool _suspend;

    private string _configDir = Directory.GetCurrentDirectory();
    private int _titleWeight = 600;
    private int _subtitleWeight = 400;
    private Dictionary<string, TargetDto>? _loadedDefs;
    private IReadOnlyDictionary<string, Target> _catalog = new Dictionary<string, Target>();

    public event Action? PreviewInvalidated;

    public MainViewModel()
    {
        AddScreenCommand = new RelayCommand(AddScreen);
        RemoveScreenCommand = new RelayCommand(RemoveScreen, () => SelectedScreen is not null);
        MoveUpCommand = new RelayCommand(() => MoveScreen(-1), () => SelectedScreen is not null);
        MoveDownCommand = new RelayCommand(() => MoveScreen(+1), () => SelectedScreen is not null);
        BrowseImageCommand = new RelayCommand(BrowseImage, () => SelectedScreen is not null);
        LoadCommand = new RelayCommand(LoadYaml);
        SaveCommand = new RelayCommand(SaveYaml);
        GenerateCommand = new RelayCommand(Generate);
        OpenOutputCommand = new RelayCommand(OpenOutput);
        PickColorCommand = new RelayCommand<string>(PickColor);
        BrowseTitleFontCommand = new RelayCommand(() => BrowseFont(f => TitleFont = f));
        BrowseSubtitleFontCommand = new RelayCommand(() => BrowseFont(f => SubtitleFont = f));

        var def = AppEnv.FindUp("screenshots.yaml");
        if (def is not null) TryLoad(def);
        else ApplyConfig(new Config { ConfigDir = _configDir }, _configDir);
    }

    // --- option lists ---
    public string[] BgTypes { get; } = { "gradient", "flat" };
    public string[] FrameTypes { get; } = { "stylized", "none" };
    public string[] Islands { get; } = { "none", "dynamic", "notch" };
    public string[] Formats { get; } = { "png", "jpeg" };
    public string[] FontFamilies { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();

    // --- general ---
    private string _project = "App";
    public string Project { get => _project; set { if (Set(ref _project, value)) Edited(); } }

    // --- fonts (a file path or an installed family name) ---
    private string _titleFont = "assets/fonts/Inter-VariableFont_opsz,wght.ttf";
    public string TitleFont
    {
        get => _titleFont;
        set { if (Set(ref _titleFont, value)) { Raise(nameof(TitleFontDisplay)); Edited(); } }
    }
    private string _subtitleFont = "assets/fonts/Inter-VariableFont_opsz,wght.ttf";
    public string SubtitleFont
    {
        get => _subtitleFont;
        set { if (Set(ref _subtitleFont, value)) { Raise(nameof(SubtitleFontDisplay)); Edited(); } }
    }
    public string TitleFontDisplay => FontDisplay(TitleFont);
    public string SubtitleFontDisplay => FontDisplay(SubtitleFont);
    private static string FontDisplay(string v) =>
        string.IsNullOrWhiteSpace(v) ? "(bundled)" :
        (v.Contains('/') || v.Contains('\\')) ? Path.GetFileName(v) : v;

    // --- background ---
    private string _bgType = "gradient";
    public string BgType { get => _bgType; set { if (Set(ref _bgType, value)) Edited(); } }
    private string _bgCenter = "#323234";
    public string BgCenter { get => _bgCenter; set { if (Set(ref _bgCenter, value)) Edited(); } }
    private string _bgEdge = "#1F1F21";
    public string BgEdge { get => _bgEdge; set { if (Set(ref _bgEdge, value)) Edited(); } }
    private string _bgFlat = "#2B2B2D";
    public string BgFlat { get => _bgFlat; set { if (Set(ref _bgFlat, value)) Edited(); } }

    // --- text ---
    private string _titleColor = "#FFFFFF";
    public string TitleColor { get => _titleColor; set { if (Set(ref _titleColor, value)) Edited(); } }
    private string _subtitleColor = "#C9C9CC";
    public string SubtitleColor { get => _subtitleColor; set { if (Set(ref _subtitleColor, value)) Edited(); } }
    private bool _uppercase = true;
    public bool Uppercase { get => _uppercase; set { if (Set(ref _uppercase, value)) Edited(); } }
    private double _letterSpacing = 0.12;
    public double LetterSpacing { get => _letterSpacing; set { if (Set(ref _letterSpacing, value)) Edited(); } }
    private double _maxSizePct = 0.06;
    public double MaxSizePct { get => _maxSizePct; set { if (Set(ref _maxSizePct, value)) Edited(); } }

    // --- layout ---
    private double _topMarginPct = 0.06;
    public double TopMarginPct { get => _topMarginPct; set { if (Set(ref _topMarginPct, value)) Edited(); } }
    private double _textBlockGapPct = 0.022;
    public double TextBlockGapPct { get => _textBlockGapPct; set { if (Set(ref _textBlockGapPct, value)) Edited(); } }
    private double _titleToDeviceGapPct = 0.04;
    public double TitleToDeviceGapPct { get => _titleToDeviceGapPct; set { if (Set(ref _titleToDeviceGapPct, value)) Edited(); } }
    private double _deviceWidthPct = 0.82;
    public double DeviceWidthPct { get => _deviceWidthPct; set { if (Set(ref _deviceWidthPct, value)) Edited(); } }

    // --- frame ---
    private string _frameType = "stylized";
    public string FrameType { get => _frameType; set { if (Set(ref _frameType, value)) Edited(); } }
    private string _bezelColor = "#0B0B0D";
    public string BezelColor { get => _bezelColor; set { if (Set(ref _bezelColor, value)) Edited(); } }
    private double _bezelWidthPct = 0.013;
    public double BezelWidthPct { get => _bezelWidthPct; set { if (Set(ref _bezelWidthPct, value)) Edited(); } }
    private double _cornerRadiusPct = 0.09;
    public double CornerRadiusPct { get => _cornerRadiusPct; set { if (Set(ref _cornerRadiusPct, value)) Edited(); } }
    private string _island = "none";
    public string Island { get => _island; set { if (Set(ref _island, value)) Edited(); } }

    // --- output ---
    private string _outputDir = "out";
    public string OutputDir { get => _outputDir; set => Set(ref _outputDir, value); }
    private string _format = "png";
    public string Format { get => _format; set { if (Set(ref _format, value)) Edited(); } }

    // --- collections ---
    public ObservableCollection<ScreenItem> Screens { get; } = new();
    private ScreenItem? _selectedScreen;
    public ScreenItem? SelectedScreen
    {
        get => _selectedScreen;
        set { if (Set(ref _selectedScreen, value)) { RaiseCommandStates(); Edited(); } }
    }

    public ObservableCollection<TargetItem> Targets { get; } = new();
    public ObservableCollection<string> TargetNames { get; } = new();
    private string? _previewTarget;
    public string? PreviewTarget { get => _previewTarget; set { if (Set(ref _previewTarget, value)) Edited(); } }

    // --- preview / status ---
    public SKImage? PreviewImage { get; private set; }
    private string _previewInfo = "";
    public string PreviewInfo { get => _previewInfo; private set => Set(ref _previewInfo, value); }
    private string _status = "Ready.";
    public string Status { get => _status; set => Set(ref _status, value); }

    // --- commands ---
    public RelayCommand AddScreenCommand { get; }
    public RelayCommand RemoveScreenCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand BrowseImageCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand<string> PickColorCommand { get; }
    public RelayCommand BrowseTitleFontCommand { get; }
    public RelayCommand BrowseSubtitleFontCommand { get; }

    // ------------------------------------------------------------------ build

    private Config BuildConfig() => new()
    {
        Project = Project,
        ConfigDir = _configDir,
        Style = new StyleConfig
        {
            Background = new BackgroundConfig { Type = BgType, Center = BgCenter, Edge = BgEdge, Color = BgFlat },
            TitleColor = TitleColor,
            SubtitleColor = SubtitleColor,
            Font = new FontConfig { Title = TitleFont, Subtitle = SubtitleFont, TitleWeight = _titleWeight, SubtitleWeight = _subtitleWeight },
            Title = new TitleConfig { Uppercase = Uppercase, LetterSpacing = LetterSpacing, MaxSizePct = MaxSizePct },
            Layout = new LayoutConfig
            {
                TopMarginPct = TopMarginPct,
                TextBlockGapPct = TextBlockGapPct,
                TitleToDeviceGapPct = TitleToDeviceGapPct,
                DeviceWidthPct = DeviceWidthPct,
            },
            Frame = new FrameConfig
            {
                Type = FrameType,
                BezelColor = BezelColor,
                BezelWidthPct = BezelWidthPct,
                CornerRadiusPct = CornerRadiusPct,
                Island = Island,
            },
        },
        Screens = Screens.Select(s => s.ToSpec()).ToList(),
        Output = new OutputConfig { Dir = OutputDir, Format = Format },
        Targets = Targets.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
        TargetDefs = _loadedDefs,
    };

    // --------------------------------------------------------------- preview

    private void Edited()
    {
        if (_suspend) return;
        UpdatePreview();
    }

    public void UpdatePreview()
    {
        if (_suspend) return;
        try
        {
            var cfg = BuildConfig();
            var target = ResolvePreviewTarget(cfg);
            if (target is null) { PreviewInfo = "no targets available"; PreviewInvalidated?.Invoke(); return; }

            var screen = SelectedScreen?.ToSpec() ?? new ScreenSpec { Title = Project };
            int idx = SelectedScreen is not null ? Screens.IndexOf(SelectedScreen) + 1 : 1;
            var item = _renderer.BuildPlan(cfg, target, screen, idx, cfg.ResolvePath(OutputDir));

            var img = _renderer.RenderToImage(cfg, item);
            PreviewImage?.Dispose();
            PreviewImage = img;
            PreviewInfo = $"{target.Name}   {target.Width}×{target.Height}" +
                          (item.Layout.TextDeviceOverlap ? "    ⚠ text/device overlap" : "");
        }
        catch (Exception ex)
        {
            PreviewInfo = "preview error: " + ex.Message;
        }
        PreviewInvalidated?.Invoke();
    }

    private Target? ResolvePreviewTarget(Config cfg)
    {
        if (PreviewTarget is not null && _catalog.TryGetValue(PreviewTarget, out var t)) return t;
        var firstSel = Targets.FirstOrDefault(x => x.IsSelected);
        if (firstSel is not null && _catalog.TryGetValue(firstSel.Name, out var s)) return s;
        return _catalog.Values.FirstOrDefault();
    }

    // ------------------------------------------------------------ load / save

    private void LoadYaml()
    {
        var dlg = new OpenFileDialog { Filter = "YAML config (*.yaml;*.yml)|*.yaml;*.yml|All files|*.*" };
        if (dlg.ShowDialog() == true) TryLoad(dlg.FileName);
    }

    private void TryLoad(string path)
    {
        try
        {
            var cfg = ConfigLoader.Load(path);
            ApplyConfig(cfg, cfg.ConfigDir);
            Status = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyConfig(Config cfg, string dir)
    {
        _suspend = true;
        try
        {
            _configDir = dir;
            _loadedDefs = cfg.TargetDefs;
            _titleWeight = cfg.Style.Font.TitleWeight;
            _subtitleWeight = cfg.Style.Font.SubtitleWeight;
            TitleFont = cfg.Style.Font.Title ?? TitleFont;
            SubtitleFont = cfg.Style.Font.Subtitle ?? SubtitleFont;

            Project = cfg.Project;
            BgType = cfg.Style.Background.Type; BgCenter = cfg.Style.Background.Center;
            BgEdge = cfg.Style.Background.Edge; BgFlat = cfg.Style.Background.Color;
            TitleColor = cfg.Style.TitleColor; SubtitleColor = cfg.Style.SubtitleColor;
            Uppercase = cfg.Style.Title.Uppercase; LetterSpacing = cfg.Style.Title.LetterSpacing;
            MaxSizePct = cfg.Style.Title.MaxSizePct;
            TopMarginPct = cfg.Style.Layout.TopMarginPct; TextBlockGapPct = cfg.Style.Layout.TextBlockGapPct;
            TitleToDeviceGapPct = cfg.Style.Layout.TitleToDeviceGapPct;
            DeviceWidthPct = cfg.Style.Layout.DeviceWidthPct;
            FrameType = cfg.Style.Frame.Type; BezelColor = cfg.Style.Frame.BezelColor;
            BezelWidthPct = cfg.Style.Frame.BezelWidthPct; CornerRadiusPct = cfg.Style.Frame.CornerRadiusPct;
            Island = cfg.Style.Frame.Island;
            OutputDir = cfg.Output.Dir; Format = cfg.Output.Format;

            foreach (var s in Screens) s.PropertyChanged -= OnScreenChanged;
            Screens.Clear();
            foreach (var s in cfg.Screens)
                AddScreenItem(new ScreenItem { Image = s.Image, Title = s.Title, Subtitle = s.Subtitle ?? "" });
            SelectedScreen = Screens.FirstOrDefault();

            LoadCatalog(cfg.Targets);
        }
        finally
        {
            _suspend = false;
        }
        UpdatePreview();
    }

    private void LoadCatalog(List<string> selectedNames)
    {
        try
        {
            _catalog = TargetCatalog.Load(AppEnv.ResolveSeed(_configDir), _loadedDefs);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Targets failed to load: " + ex.Message, "Targets", MessageBoxButton.OK, MessageBoxImage.Warning);
            _catalog = new Dictionary<string, Target>();
        }

        var sel = new HashSet<string>(selectedNames);
        Targets.Clear();
        TargetNames.Clear();
        foreach (var t in _catalog.Values.OrderBy(t => t.Store).ThenBy(t => t.Name))
        {
            Targets.Add(new TargetItem
            {
                Name = t.Name,
                Label = $"{t.Name}   {t.Width}×{t.Height}",
                IsSelected = sel.Count == 0 || sel.Contains(t.Name),
            });
            TargetNames.Add(t.Name);
        }
        PreviewTarget = selectedNames.FirstOrDefault(n => _catalog.ContainsKey(n)) ?? TargetNames.FirstOrDefault();
    }

    private void SaveYaml()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "YAML config (*.yaml)|*.yaml",
            FileName = "screenshots.yaml",
            InitialDirectory = _configDir,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ConfigLoader.Save(BuildConfig(), dlg.FileName);
            Status = $"Saved {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --------------------------------------------------------------- generate

    private void Generate()
    {
        var cfg = BuildConfig();
        if (cfg.Screens.Count == 0) { MessageBox.Show("Add at least one screen.", "Generate"); return; }

        var selected = Targets.Where(t => t.IsSelected).Select(t => _catalog[t.Name]).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one target.", "Generate"); return; }

        string outDir = cfg.ResolvePath(OutputDir);
        var errors = new List<string>();
        int ok = 0;

        foreach (var t in selected)
        {
            for (int i = 0; i < cfg.Screens.Count; i++)
            {
                var item = _renderer.BuildPlan(cfg, t, cfg.Screens[i], i + 1, outDir);
                if (!item.SourceExists) { errors.Add($"{t.Name} #{i + 1}: missing {item.SourcePath}"); continue; }
                try { _renderer.Render(cfg, item); ok++; }
                catch (Exception ex) { errors.Add($"{t.Name} #{i + 1}: {ex.Message}"); }
            }
        }

        Status = $"Generated {ok} image(s) to {outDir}" + (errors.Count > 0 ? $"  ({errors.Count} skipped)" : "");
        if (errors.Count > 0)
            MessageBox.Show(string.Join("\n", errors.Take(20)), "Some images were skipped",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else if (Directory.Exists(outDir) &&
                 MessageBox.Show($"Done — {ok} image(s).\nOpen the output folder?", "Generate",
                     MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            OpenFolder(outDir);
    }

    private void OpenOutput()
    {
        var outDir = BuildConfig().ResolvePath(OutputDir);
        if (Directory.Exists(outDir)) OpenFolder(outDir);
        else MessageBox.Show("Output folder doesn't exist yet — generate first.", "Open output");
    }

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    // ----------------------------------------------------------- screen edits

    private void AddScreen()
    {
        var item = new ScreenItem { Title = "New Screen" };
        AddScreenItem(item);
        SelectedScreen = item;
        Edited();
    }

    private void AddScreenItem(ScreenItem item)
    {
        item.PropertyChanged += OnScreenChanged;
        Screens.Add(item);
    }

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenItem.Display)) return; // display is derived; avoid double work
        Edited();
    }

    private void RemoveScreen()
    {
        if (SelectedScreen is null) return;
        int idx = Screens.IndexOf(SelectedScreen);
        SelectedScreen.PropertyChanged -= OnScreenChanged;
        Screens.Remove(SelectedScreen);
        SelectedScreen = Screens.Count > 0 ? Screens[Math.Min(idx, Screens.Count - 1)] : null;
        Edited();
    }

    private void MoveScreen(int delta)
    {
        if (SelectedScreen is null) return;
        int i = Screens.IndexOf(SelectedScreen);
        int j = i + delta;
        if (j < 0 || j >= Screens.Count) return;
        Screens.Move(i, j);
        Edited();
    }

    private void BrowseImage()
    {
        if (SelectedScreen is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files|*.*",
            InitialDirectory = Directory.Exists(_configDir) ? _configDir : null,
        };
        if (dlg.ShowDialog() == true)
            SelectedScreen.Image = dlg.FileName;
    }

    private void BrowseFont(Action<string> set)
    {
        var dlg = new OpenFileDialog { Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf|All files|*.*" };
        if (dlg.ShowDialog() == true) set(dlg.FileName);
    }

    private void PickColor(string? key)
    {
        if (key is null) return;
        var picked = ColorPickerService.Pick(GetColor(key));
        if (picked is not null) SetColor(key, picked);
    }

    private string GetColor(string key) => key switch
    {
        nameof(BgCenter) => BgCenter,
        nameof(BgEdge) => BgEdge,
        nameof(BgFlat) => BgFlat,
        nameof(TitleColor) => TitleColor,
        nameof(SubtitleColor) => SubtitleColor,
        nameof(BezelColor) => BezelColor,
        _ => "#000000",
    };

    private void SetColor(string key, string value)
    {
        switch (key)
        {
            case nameof(BgCenter): BgCenter = value; break;
            case nameof(BgEdge): BgEdge = value; break;
            case nameof(BgFlat): BgFlat = value; break;
            case nameof(TitleColor): TitleColor = value; break;
            case nameof(SubtitleColor): SubtitleColor = value; break;
            case nameof(BezelColor): BezelColor = value; break;
        }
    }

    private void RaiseCommandStates()
    {
        RemoveScreenCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        BrowseImageCommand.RaiseCanExecuteChanged();
    }
}
