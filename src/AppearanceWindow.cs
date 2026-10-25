using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AtomUI.Desktop.Controls;
using Window = AtomUI.Desktop.Controls.Window;

/// <summary>One material/tint for the content frame and AtomUI's Windows title bar.</summary>
internal class AppearanceWindow : Window
{
    private readonly SolidColorBrush frameFill = new(Colors.Transparent);
    private AppearanceSettings appearance = new();
    private bool applyingSurface;

    internal static bool SupportsMica => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    internal string SelectedEffect => ResolveEffect(appearance.WindowsEffect, SupportsMica);
    internal bool IsMicaSelected => OperatingSystem.IsWindows() && SelectedEffect == "mica";
    internal bool IsOpacityManagedBySystem => IsMicaSelected ||
        ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
    internal event EventHandler? SurfaceChanged;
    internal Color FrameColor => frameFill.Color;

    internal static string ResolveEffect(string choice, bool windows11) =>
        choice == "auto" ? windows11 ? "mica" : "acrylic" : choice;

    internal AppearanceWindow()
    {
        Background = Brushes.Transparent;
        ContentFrameBackground = TitleBarFrameBackground = frameFill;
        Foreground = UiStyle.Foreground;
        if (OperatingSystem.IsWindows())
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Opened += (_, _) => ApplySurface(appearance);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == ActualTransparencyLevelProperty || change.Property == ActualThemeVariantProperty)
                ApplySurface(appearance);
        };
    }

    protected override void NotifyConfigureTitleBar(WindowTitleBar titleBar)
    {
        base.NotifyConfigureTitleBar(titleBar);
        // AtomUI's Windows title bar otherwise paints its own opaque theme fill.
        // The frame properties alone only cover Linux CSD.
        if (OperatingSystem.IsWindows()) titleBar.Background = frameFill;
    }

    internal void ApplySurface(AppearanceSettings settings)
    {
        appearance = settings;
        if (applyingSurface) return;
        applyingSurface = true;
        try
        {
            bool windows = OperatingSystem.IsWindows();
            string effect = SelectedEffect;
            TransparencyLevelHint = windows && effect == "mica" && SupportsMica
                ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]
                : windows && effect is "acrylic" or "mica"
                    ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]
                    : settings.BackgroundOpacity < 100
                        ? [WindowTransparencyLevel.Transparent] : [WindowTransparencyLevel.None];
            byte alpha = ActualTransparencyLevel == WindowTransparencyLevel.None ? (byte)255
                : ActualTransparencyLevel == WindowTransparencyLevel.Mica ||
                    ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur ? (byte)0
                : (byte)Math.Round(settings.BackgroundOpacity * 2.55);
            Color color = UiStyle.Background.Color;
            frameFill.Color = Color.FromArgb(alpha, color.R, color.G, color.B);
            TransparencyBackgroundFallback = UiStyle.Background;
        }
        finally { applyingSurface = false; }
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
    }

    internal string SurfaceDescription()
    {
        var level = ActualTransparencyLevel;
        string actual = level == WindowTransparencyLevel.Mica ? "云母（Mica）"
            : level == WindowTransparencyLevel.AcrylicBlur ? "亚克力"
            : level == WindowTransparencyLevel.Transparent ? "透明背景"
            : "不透明背景";
        string fallback = IsMicaSelected && !SupportsMica ? "此系统不支持云母，已请求亚克力。"
            : OperatingSystem.IsWindows() && SelectedEffect is "mica" or "acrylic" &&
                level != WindowTransparencyLevel.Mica && level != WindowTransparencyLevel.AcrylicBlur
                ? "系统未提供所选材质。"
                : "";
        return fallback + "当前显示：" + actual + "。";
    }
}
