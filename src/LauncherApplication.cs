using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using AtomUI.Theme.DesignTokens;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Window = AppearanceWindow;
using TextBlock = Avalonia.Controls.TextBlock;

public sealed partial class LauncherApplication : Application
{
    internal string ConfigPath { get; init; } = "";
    internal ProductConfig Configuration { get; init; } = new();
    internal string? StartupError { get; init; }
    internal string? SmokeOutput { get; init; }
    private readonly SemaphoreSlim themeLock = new(1);

    internal static AppBuilder Build(string path, ProductConfig config, string? error = null, string? smokeOutput = null)
    {
        var builder = AppBuilder.Configure(() => new LauncherApplication
            { ConfigPath = path, Configuration = config, StartupError = error, SmokeOutput = smokeOutput });
        builder = OperatingSystem.IsLinux()
            ? builder.UseX11().UseSkia().UseHarfBuzz()
            : builder.UseAtomUIPlatformDetect();
        builder = builder.WithAtomUIDefaultOptions().WithInterFont();
        if (OperatingSystem.IsLinux())
        {
#pragma warning disable AVALONIA_X11_CSD
            builder = builder.With(new X11PlatformOptions
            {
                EnableDrawnDecorations = true,
                RenderingMode = [X11RenderingMode.Software]
            });
#pragma warning restore AVALONIA_X11_CSD
            if (Environment.GetEnvironmentVariable("WUWA_BACKEND") != "x11")
            {
                builder = builder.With(new WaylandPlatformOptions
                {
                    AppId = "wuwa-touch-launcher",
#if !WINDOWS
                    SoftwareRendering = true,
#endif
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
                    ForceDrawnDecorations = true
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
                }).UseWaylandWithFallback();
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface]
            });
        }
        return builder.UseAtomUI(atom => atom
            .WithApplicationId("WuwaTouchLauncher")
            .WithDefaultFontFamily(UiStyle.Fonts)
            .WithInitialTheme(IThemeManager.DEFAULT_THEME_ID, Theme(config.Appearance.Theme != "light"))
            .UseDesktopControls());
    }

    private static ThemeConfig Theme(bool dark) => new ThemeConfigBuilder()
        .WithAlgorithms(dark ? [ThemeAlgorithm.Default, ThemeAlgorithm.Dark] : [ThemeAlgorithm.Default])
        .WithToken(nameof(DesignToken.ColorPrimary), dark ? "#85A9FF" : "#3C65CC")
        .WithToken(nameof(DesignToken.BorderRadius), "8")
        .Build();

    internal bool IsDark(PlatformThemeVariant? systemTheme = null) => Configuration.Appearance.Theme switch
    {
        "dark" => true,
        "light" => false,
        _ => (systemTheme ?? PlatformSettings?.GetColorValues().ThemeVariant) != PlatformThemeVariant.Light
    };

    internal async Task ApplyAppearanceAsync(PlatformThemeVariant? systemTheme = null)
    {
        await themeLock.WaitAsync();
        try
        {
            bool dark = IsDark(systemTheme);
            if (this.GetThemeManager() is { } manager)
            {
                var result = await manager.ApplyThemeAsync(new(IThemeManager.DEFAULT_THEME_ID,
                    Theme(dark), ThemeTransitionReason.LocalConfigChanged));
                if (result.Status == ThemeTransitionStatus.Failed)
                    throw new InvalidOperationException("无法应用界面主题，请重试。");
            }
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            UiStyle.ApplyPalette(dark);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                foreach (var window in desktop.Windows.OfType<AppearanceWindow>().ToArray())
                    window.ApplySurface(Configuration.Appearance);
        }
        finally { themeLock.Release(); }
    }

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        // AtomUI's Linux CSD underlay otherwise paints an opaque theme fill
        // underneath the independently tinted title/content frames.
        Styles.Add(new Style(selector => selector
            .OfType<Avalonia.Controls.Chrome.WindowDrawnDecorations>()
            .Template().OfType<Avalonia.Controls.Border>().Name("PART_WindowFrame"))
        {
            Setters = { new Setter(Avalonia.Controls.Border.BackgroundProperty, Brushes.Transparent) }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            UiStyle.ApplyPalette(IsDark());
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = StartupError is null
                ? new LauncherWindow(ConfigPath, Configuration, SmokeOutput)
                : ErrorWindow(StartupError);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static Window ErrorWindow(string text)
    {
        var window = new Window
        {
            Title = "鸣潮启动器 · 无法打开配置", Width = 640, Height = 330,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily(UiStyle.Fonts)
        };
        var close = UiStyle.Button("关闭", "close", () => window.Close(), true);
        window.Content = UiStyle.Column(18,
            UiStyle.Text("暂时无法打开启动器", 24, true),
            UiStyle.Hint(text),
            UiStyle.Hint("原配置未被覆盖。请修正配置文件，或使用 --config 指定其他文件。"),
            close);
        ((Avalonia.Controls.Control)window.Content).Margin = new Thickness(28);
        window.ApplySurface(new());
        return window;
    }
}
