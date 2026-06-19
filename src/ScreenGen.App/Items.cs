using System.IO;

namespace ScreenGen.App;

/// <summary>One editable screen row (image + captions).</summary>
public sealed class ScreenItem : ObservableObject
{
    private string _image = "";
    private string _title = "";
    private string _subtitle = "";

    public string Image { get => _image; set { if (Set(ref _image, value)) Raise(nameof(Display)); } }
    public string Title { get => _title; set { if (Set(ref _title, value)) Raise(nameof(Display)); } }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    public string Display =>
        !string.IsNullOrWhiteSpace(Title) ? Title :
        !string.IsNullOrWhiteSpace(Image) ? Path.GetFileName(Image) : "(screen)";

    public ScreenSpec ToSpec() => new()
    {
        Image = Image,
        Title = Title,
        Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle,
    };
}

/// <summary>One target with a checkbox controlling whether it's generated.</summary>
public sealed class TargetItem : ObservableObject
{
    private bool _isSelected;
    public required string Name { get; init; }
    public required string Label { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
