using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ScreenGen.App;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.PreviewInvalidated += () => Dispatcher.Invoke(Preview.InvalidateVisual);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x16, 0x16, 0x18));

        var img = _vm.PreviewImage;
        if (img is null) return;

        var info = e.Info;
        float scale = Math.Min((float)info.Width / img.Width, (float)info.Height / img.Height) * 0.96f;
        float w = img.Width * scale, h = img.Height * scale;
        float x = (info.Width - w) / 2f, y = (info.Height - h) / 2f;

        canvas.DrawImage(img, new SKRect(x, y, x + w, y + h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
