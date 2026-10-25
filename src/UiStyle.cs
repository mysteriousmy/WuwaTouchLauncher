using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using AtomUI.Desktop.Controls;
using Button = AtomUI.Desktop.Controls.Button;
using TextBlock = Avalonia.Controls.TextBlock;
using Path = Avalonia.Controls.Shapes.Path;

internal static class UiStyle
{
    public static string Fonts => OperatingSystem.IsWindows()
        ? "Segoe UI, Microsoft YaHei UI, Microsoft YaHei, avares://wuwa-touch-launcher/Assets/Fonts#Wuwa Launcher UI Regular, sans-serif"
        : "Inter, avares://wuwa-touch-launcher/Assets/Fonts#Wuwa Launcher UI Regular, Noto Sans CJK SC, Microsoft YaHei, sans-serif";
    // Existing controls retain these brushes; changing a palette recolors every
    // page and owned dialog without rebuilding controls or losing edits/focus.
    public static readonly SolidColorBrush Background = new(Color.Parse("#12151C"));
    public static readonly SolidColorBrush Sidebar = new(Color.Parse("#300D1016"));
    public static readonly SolidColorBrush Surface = new(Color.Parse("#B0191E28"));
    public static readonly SolidColorBrush SurfaceRaised = new(Color.Parse("#202735"));
    public static readonly SolidColorBrush Border = new(Color.Parse("#2B3342"));
    public static readonly SolidColorBrush Foreground = new(Color.Parse("#E9EDF5"));
    public static readonly SolidColorBrush Muted = new(Color.Parse("#929DB0"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#A8C1FF"));
    public static readonly SolidColorBrush AccentSurface = new(Color.Parse("#24334F"));
    public static readonly SolidColorBrush AccentBorder = new(Color.Parse("#38517D"));
    public static readonly SolidColorBrush Success = new(Color.Parse("#84D7B4"));
    public static readonly SolidColorBrush Warning = new(Color.Parse("#F0BE82"));
    public static readonly IBrush OnAccent = Brushes.White;

    public static void ApplyPalette(bool dark)
    {
        Background.Color = Color.Parse(dark ? "#12151C" : "#F3F5FA");
        Sidebar.Color = Color.Parse(dark ? "#300D1016" : "#30DAE1EE");
        Surface.Color = Color.Parse(dark ? "#B0191E28" : "#B0FFFFFF");
        SurfaceRaised.Color = Color.Parse(dark ? "#202735" : "#E8ECF4");
        Border.Color = Color.Parse(dark ? "#2B3342" : "#D9DFEA");
        Foreground.Color = Color.Parse(dark ? "#E9EDF5" : "#182231");
        Muted.Color = Color.Parse(dark ? "#929DB0" : "#5C6A81");
        Accent.Color = Color.Parse(dark ? "#A8C1FF" : "#315CC4");
        AccentSurface.Color = Color.Parse(dark ? "#24334F" : "#E3EAFA");
        AccentBorder.Color = Color.Parse(dark ? "#38517D" : "#B6C8F1");
        Success.Color = Color.Parse(dark ? "#84D7B4" : "#218360");
        Warning.Color = Color.Parse(dark ? "#F0BE82" : "#986014");
    }

    public static TextBlock Text(string value, double size = 14, bool strong = false, IBrush? color = null) => new()
    {
        Text = value, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = color ?? Foreground, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    public static TextBlock Hint(string value) => Text(value, 12, color: Muted);
    public static StackPanel Column(double gap, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = gap };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    public static StackPanel Row(double gap, params Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    public static Grid Grid(string columns, params Control[] controls)
    {
        var grid = new Grid { ColumnDefinitions = new(columns), ColumnSpacing = 16 };
        for (int i = 0; i < controls.Length; i++)
        {
            Avalonia.Controls.Grid.SetColumn(controls[i], i);
            grid.Children.Add(controls[i]);
        }
        return grid;
    }
    public static Border Panel(Control child, int padding = 22) => new()
    {
        Background = Surface, BorderBrush = Border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), Child = child
    };
    public static Border Badge(string text, bool accent = false) => new()
    {
        Background = accent ? AccentSurface : SurfaceRaised, CornerRadius = new CornerRadius(6),
        Padding = new Thickness(9, 4), HorizontalAlignment = HorizontalAlignment.Left,
        Child = Text(text, 11, color: accent ? Accent : Muted)
    };
    public static Button Button(string label, string icon, Action action, bool primary = false, string? name = null)
    {
        var button = new Button
        {
            Name = name, Content = Row(8, Icon(icon, 17, primary ? OnAccent : null),
                Text(label, color: primary ? OnAccent : null)),
            ButtonType = primary ? ButtonType.Primary : ButtonType.Default,
            MinHeight = 38, Padding = new Thickness(14, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    public static Control Icon(string kind, double size = 22, IBrush? color = null)
    {
        string geometry = kind switch
        {
            "play" => "M8 5 L19 12 L8 19 Z",
            "graphics" => "M3 4 H21 V17 H3 Z M8 21 H16 M12 17 V21 M6 13 L10 9 L13 12 L16 7 L19 10",
            "input" => "M7 4 Q7 2 9 2 H15 Q17 2 17 4 V15 Q17 22 12 22 Q7 22 7 15 Z M12 3 V9 M7 10 H17",
            "settings" => "M4 6 H20 M4 12 H20 M4 18 H20 M8 3 V9 M16 9 V15 M10 15 V21",
            "folder" => "M3 6 H9 L11 8 H21 V20 H3 Z M3 6 V4 H9 L11 6 H19 V8",
            "refresh" => "M20 8 A8 8 0 1 0 20 16 M20 3 V8 H15",
            "check" => "M5 12 L10 17 L20 6",
            "save" => "M4 3 H17 L21 7 V21 H3 V3 Z M7 3 V9 H16 V3 M7 21 V14 H17 V21",
            "search" => "M16 16 L21 21 M18 10 A8 8 0 1 1 2 10 A8 8 0 1 1 18 10",
            "arrow" => "M4 12 H20 M14 6 L20 12 L14 18",
            "touch" => "M8 12 V6 A2 2 0 0 1 12 6 V11 M12 9 L18 11 Q20 12 19 16 L17 21 H10 L4 14 Q3 12 5 11 L8 14",
            "link" => "M10 8 L13 5 A4 4 0 0 1 19 11 L16 14 M14 16 L11 19 A4 4 0 0 1 5 13 L8 10 M8 16 L16 8",
            "file" => "M5 2 H14 L20 8 V22 H5 Z M14 2 V8 H20 M9 12 H16 M9 16 H16",
            "close" => "M6 6 L18 18 M18 6 L6 18",
            "info" => "M22 12 A10 10 0 1 1 2 12 A10 10 0 1 1 22 12 M12 10 V17 M12 6 V7",
            "wave" => "M2 14 Q5 6 8 14 T14 14 T20 14 M5 8 Q8 2 11 8 T17 8 M5 20 Q8 14 11 20 T17 20",
            "appearance" => "M12 4 A8 8 0 1 0 20 12 A6 6 0 0 1 12 4 Z M18 2 V6 M16 4 H20",
            _ => "M4 4 H20 V20 H4 Z"
        };
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Child = new Canvas
            {
                Width = 24, Height = 24,
                Children =
                {
                    new Path { Data = StreamGeometry.Parse(geometry), Stroke = color ?? Accent,
                        StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round }
                }
            }
        };
    }
}
