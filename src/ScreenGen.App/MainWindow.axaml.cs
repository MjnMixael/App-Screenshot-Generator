using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScreenGen.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        _vm.Owner = this;          // file pickers + dialogs are owner-relative
        DataContext = _vm;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
