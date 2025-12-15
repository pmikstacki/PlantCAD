using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Controls.Viewport;

public partial class ViewportPanel : UserControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<ViewportPanel, string?>(nameof(Header));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<ViewportPanel, object?>(nameof(Icon));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ViewportPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
