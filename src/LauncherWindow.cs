using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AtomUI.Desktop.Controls;
using Button = AtomUI.Desktop.Controls.Button;
using Select = AtomUI.Desktop.Controls.Select;
using TextBlock = Avalonia.Controls.TextBlock;
using Window = AppearanceWindow;
using Slider = AtomUI.Desktop.Controls.Slider;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using static UiStyle;

internal sealed class LauncherWindow : Window
{
    private readonly string configPath;
    internal readonly ProductConfig Configuration;
    internal DiscoveryResult DiscoveryState { get; private set; } = new();
    internal Task InitialScan { get; private set; } = Task.CompletedTask;
    internal Task AppearanceUpdate { get; private set; } = Task.CompletedTask;
    internal WindowsMemoryReclaimer MemoryReclaimer { get; }
    private readonly ContentControl pageHost = new();
    private readonly TextBlock title = Text("启动游戏", 26, true);
    private readonly TextBlock subtitle = Hint("");
    private readonly TextBlock message = Hint("准备就绪");
    private readonly TextBlock saveStatus = Hint("配置已保存");
    private readonly TextBlock runtimeStatus = Hint("未运行");
    private readonly TextBlock backendStatus = Hint(OperatingSystem.IsWindows() ? "WINDOWS" : "LINUX");
    private readonly TextBlock launchLabel = Text("启动游戏", 14, true, OnAccent);
    private TextBlock graphicsCount = Hint("");
    private readonly Button launchButton;
    private readonly List<Button> navigation = [];
    private readonly List<Task> operations = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly string? smokeOutput;
    private string environmentText;
    private bool dirty, closed, scanning, launching, confirmingClose, allowClose;
    private bool waitingForSession;
    private ContentControl? resourceChoice;
    private int selectedPage;
    private string category = "画质与场景";
    private string graphicsSearch = "";
    private ContentControl? graphicsRows;
    private Slider? opacitySlider;
    private TextBlock? opacityHint, materialStatus;
    private bool FromLutris => LaunchSourcePolicy.UsesLutris(Configuration, DiscoveryState);

    internal LauncherWindow(string path, ProductConfig config, string? smoke = null)
    {
        configPath = path;
        Configuration = config;
        MemoryReclaimer = new(() => !closed && !scanning && !launching && !confirmingClose);
        smokeOutput = smoke;
        environmentText = string.Join('\n', config.Linux.Environment.Select(x => x.Key + "=" + x.Value));
        Title = "鸣潮启动器";
        Name = "launcher-window";
        Width = 1100; Height = 800; MinWidth = 940; MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily(Fonts); FontSize = 14;
        Foreground = UiStyle.Foreground;
        ApplySurface(config.Appearance);
        using (var icon = AssetLoader.Open(new Uri("avares://wuwa-touch-launcher/Assets/launcher.ico")))
            Icon = new WindowIcon(icon);
        launchButton = new Button
        {
            Name = "launch-game", Content = Row(12, UiStyle.Icon("play", 18, OnAccent), launchLabel),
            ButtonType = ButtonType.Primary, MinWidth = 184, Height = 46, Padding = new Thickness(22, 10)
        };
        launchButton.Click += (_, _) => Run(LaunchAsync);
        BuildShell();
        ShowPage(0);
        UpdateMode();
        Opened += (_, _) =>
        {
            backendStatus.Text = OperatingSystem.IsWindows() ? "Windows" :
                (PlatformImpl?.GetType().Assembly.GetName().Name == "Avalonia.Wayland" ? "Wayland" : "X11");
            timer.Tick += (_, _) => UpdateStatus();
            timer.Start();
            InitialScan = ScanAsync();
            operations.Add(InitialScan);
            UpdateAppearance();
            Run(async () =>
            {
                await InitialScan;
                await AppearanceUpdate;
                if (closed) return;
                MemoryReclaimer.Request();
                if (smokeOutput is not null) await UiSmoke.RunAsync(this, smokeOutput);
            });
        };
        SurfaceChanged += (_, _) => UpdateAppearanceControls();
        if (Application.Current?.PlatformSettings is { } platform)
            platform.ColorValuesChanged += SystemColorsChanged;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                MemoryReclaimer.Request();
        };
        Closing += (_, args) =>
        {
            if (allowClose || !dirty) return;
            args.Cancel = true;
            if (!confirmingClose) Run(ConfirmCloseAsync);
        };
        Closed += (_, _) =>
        {
            closed = true; timer.Stop(); MemoryReclaimer.Dispose();
            if (Application.Current?.PlatformSettings is { } platform)
                platform.ColorValuesChanged -= SystemColorsChanged;
        };
    }

    public static int Run(string path, ProductConfig config, string? smoke = null) =>
        LauncherApplication.Build(path, config, smokeOutput: smoke).StartWithClassicDesktopLifetime([]);

    public static void ShowStartupError(string text)
    {
        if (Application.Current is not null) return;
        try { LauncherApplication.Build("", new(), text).StartWithClassicDesktopLifetime([]); }
        catch (Exception ex) when (IsUserError(ex) || ex is DllNotFoundException or PlatformNotSupportedException)
        { Console.Error.WriteLine(ex.Message); }
    }

    private void BuildShell()
    {
        var brandMark = new Avalonia.Controls.Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Background = AccentSurface,
            Child = UiStyle.Icon("wave", 29), VerticalAlignment = VerticalAlignment.Center
        };
        var brand = Row(12, brandMark, Column(3, Text("鸣潮启动器", 16, true), Hint("WUWA LAUNCHER")));
        brand.Margin = new Thickness(0, 8, 0, 36);
        var nav = new StackPanel { Spacing = 7 };
        string[] names = ["启动游戏", "触控画质", "输入与热键", "设置"];
        string[] icons = ["play", "graphics", "input", "settings"];
        for (int i = 0; i < names.Length; i++)
        {
            int page = i;
            var button = new Button
            {
                Name = "nav-" + i, Content = Row(12, UiStyle.Icon(icons[i], 20), Text(names[i], 14)),
                ButtonType = ButtonType.Text, Height = 46,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10)
            };
            button.Click += (_, _) => ShowPage(page);
            navigation.Add(button); nav.Children.Add(button);
        }
        var side = new Avalonia.Controls.Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(18, 16, 18, 22) };
        side.Children.Add(brand);
        Avalonia.Controls.Grid.SetRow(nav, 1); side.Children.Add(nav);
        var sideBottom = Column(10, Row(7, new EllipseDot(Success), runtimeStatus), backendStatus, Hint("版本 0.6.0"));
        Avalonia.Controls.Grid.SetRow(sideBottom, 2); side.Children.Add(sideBottom);
        var sidebar = new Avalonia.Controls.Border
        {
            Background = Sidebar, BorderBrush = UiStyle.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = side
        };
        var heading = Column(6, title, subtitle);
        heading.Margin = new Thickness(30, 25, 30, 20);
        var scroll = new ScrollViewer
        {
            Name = "page-scroll", Content = pageHost, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(30, 0, 30, 24)
        };
        var save = UiStyle.Button("保存配置", "save", () => Save(), name: "save-config");
        save.Height = 44;
        var preview = UiStyle.Button("启动预览", "search", () => Run(PreviewAsync), name: "preview");
        preview.Height = 44;
        var actions = Grid("*,Auto,Auto,Auto", Column(6, saveStatus, message), preview, save, launchButton);
        actions.ColumnSpacing = 10;
        var footer = new Avalonia.Controls.Border
        {
            Background = Brushes.Transparent, BorderBrush = UiStyle.Border, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(28, 16), Child = actions
        };
        var main = new Avalonia.Controls.Grid { RowDefinitions = new("Auto,*,Auto") };
        main.Children.Add(heading);
        Avalonia.Controls.Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        Avalonia.Controls.Grid.SetRow(footer, 2); main.Children.Add(footer);
        var shell = Grid("208,*", sidebar, main);
        shell.ColumnSpacing = 0;
        shell.Background = Brushes.Transparent;
        Content = shell;
    }

    private void ShowPage(int page)
    {
        selectedPage = page;
        for (int i = 0; i < navigation.Count; i++)
        {
            navigation[i].Background = i == page ? AccentSurface : Brushes.Transparent;
            navigation[i].BorderBrush = i == page ? AccentBorder : Brushes.Transparent;
            navigation[i].BorderThickness = new Thickness(1);
        }
        (title.Text, subtitle.Text) = page switch
        {
            0 => ("启动游戏", "选择你的游玩方式，其他交给启动器。"),
            1 => ("触控画质", "只在触控会话中应用，退出游戏后恢复原设置。"),
            2 => ("输入与热键", "让触摸、键盘与鼠标各就其位。"),
            _ => ("设置", "启动环境、快捷入口和配置管理。")
        };
        graphicsRows = null;
        opacitySlider = null; opacityHint = null; materialStatus = null;
        pageHost.Content = page switch { 0 => LaunchPage(), 1 => GraphicsPage(), 2 => InputPage(), _ => SettingsPage() };
    }

    private void Changed()
    {
        dirty = true;
        saveStatus.Text = "有未保存的更改";
        saveStatus.Foreground = Warning;
    }

    private void UpdateMode()
    {
        launchLabel.Text = Configuration.Mode == "touch" ? "启动触控模式" : "启动普通模式";
        Avalonia.Automation.AutomationProperties.SetName(launchButton, launchLabel.Text);
    }

    private Control LaunchPage()
    {
        var modeRow = Grid("*,*", ModeCard("normal", "普通模式", "熟悉的键鼠体验，沿用常规游戏设置。", "input"),
            ModeCard("touch", "触控模式", "云端触控界面，可独立调整画质与输入。", "touch"));
        var sourceHeader = Grid("*,Auto", Row(10, UiStyle.Icon("folder"), Text("游戏与启动方式", 16, true)),
            UiStyle.Button(scanning ? "检测中…" : "重新检测", "refresh", () => Run(ScanAsync), name: "rescan"));
        if (sourceHeader.Children[1] is Button scan) scan.IsEnabled = !scanning;
        var source = Column(18, sourceHeader, OperatingSystem.IsWindows() ? WindowsSource() : LinuxSource());
        var profile = Configuration.Selected;
        resourceChoice = new ContentControl();
        RefreshResources();
        var resources = Labeled("资源画质", resourceChoice);
        var dx = Labeled("DirectX", Choice("directx", profile.DirectX,
            [new("auto", "沿用原启动配置"), new("dx11", "DirectX 11"), new("dx12", "DirectX 12")], v => { profile.DirectX = v; Changed(); }));
        var preset = Labeled("基础画质", Choice("quality", profile.Quality,
            [SettingsCatalog.Keep, new("1", "低"), new("2", "中"), new("3", "高"), new("4", "最高")], v => { profile.Quality = v; Changed(); }));
        Control launchOptions = ToggleRow("skip-splash", "跳过开场动画", "直接进入登录流程。", profile.SkipSplash,
            v => { profile.SkipSplash = v; Changed(); });
        if (Configuration.Mode == "touch")
            launchOptions = Grid("*,*", launchOptions,
                ToggleRow("adaptive-fullscreen", "自适应全屏", "按主屏幕分辨率启动独占全屏，覆盖手动显示设置。",
                    Configuration.TouchOnly.AdaptiveFullscreen,
                    v => { Configuration.TouchOnly.AdaptiveFullscreen = v; Changed(); }));
        var options = Column(18,
            Grid("*,Auto", Text("启动选项", 16, true), Badge(Configuration.Mode == "touch" ? "触控模式配置" : "普通模式配置", true)),
            Grid("*,*,*", resources, dx, preset),
            launchOptions,
            Hint("资源画质需要先在官方启动器下载对应资源。普通与触控模式的选项分别保存。"));
        return Column(20, modeRow, Panel(source), Panel(options));
    }

    private void RefreshResources()
    {
        if (resourceChoice is null) return;
        var profile = Configuration.Selected;
        resourceChoice.Content = Choice("resources", profile.Resources,
            ResourceCatalog.Choices(ResourceCatalog.ResolveGame(Configuration, DiscoveryState)),
            value => { profile.Resources = value; Changed(); });
    }

    private Control ModeCard(string mode, string label, string description, string icon)
    {
        bool active = Configuration.Mode == mode;
        var button = new Button
        {
            Name = "mode-" + mode, ButtonType = ButtonType.Text,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18), MinHeight = 112, Background = active ? AccentSurface : Surface,
            BorderBrush = active ? Brush.Parse("#5876AB") : UiStyle.Border, BorderThickness = new Thickness(1),
            Content = Column(12,
                Grid("Auto,*,Auto", UiStyle.Icon(icon, 23, active ? Accent : Muted), Text(label, 17, true),
                    active ? UiStyle.Icon("check", 18) : new Avalonia.Controls.Border { Width = 18 }), Hint(description))
        };
        button.Click += (_, _) =>
        {
            if (Configuration.Mode == mode) return;
            Configuration.Mode = mode; Changed(); UpdateMode(); ShowPage(selectedPage);
        };
        return button;
    }

    private Control WindowsSource()
    {
        var controls = new List<Control>();
        if (DiscoveryState.InstalledGames.Count > 0)
        {
            var choices = DiscoveryState.InstalledGames.Select(g => new SettingOption(g.Executable, $"{g.Name} · {g.Source}")).ToList();
            if (Configuration.GameExecutable.Length > 0 && !choices.Any(x => x.Value.Equals(Configuration.GameExecutable, StringComparison.OrdinalIgnoreCase)))
                choices.Insert(0, new(Configuration.GameExecutable, "手动选择的位置"));
            else if (Configuration.GameExecutable.Length == 0) choices.Insert(0, new("", "请选择一个安装"));
            controls.Add(Labeled("检测到的安装", Choice("windows-installation", Configuration.GameExecutable, choices.ToArray(),
                v => { Configuration.GameExecutable = v; Changed(); ShowPage(selectedPage); })));
        }
        controls.Add(PathField("game-path", "游戏程序", Configuration.GameExecutable,
            v => Configuration.GameExecutable = v, false, "选择鸣潮游戏程序", true));
        controls.Add(Hint(scanning ? "正在读取已安装程序、Steam 和 Epic 的安装记录…" : DiscoveryState.InstalledGames.Count == 0
            ? "未检测到已安装的鸣潮。请选择 Client-Win64-Shipping.exe，也可选择安装目录中的官方启动程序。"
            : $"发现 {DiscoveryState.InstalledGames.Count} 个安装。已保存的有效游戏位置会优先保留。"));
        return Column(14, controls.ToArray());
    }

    private Control LinuxSource()
    {
        var fields = new List<Control>
        {
            Labeled("配置来源", Choice("integration", Configuration.Linux.Integration,
                [new("auto", "自动检测 · 优先 Lutris"), new("lutris", "Lutris"), new("manual", "手动配置 Proton / Wine")],
                v => { Configuration.Linux.Integration = v; Changed(); ShowPage(selectedPage); }))
        };
        if (Configuration.Linux.Integration != "manual")
        {
            var choices = new List<SettingOption> { new("", "自动匹配鸣潮") };
            choices.AddRange(DiscoveryState.LutrisGames.Select(g => new SettingOption(g.ConfigPath, g.Name)));
            if (Configuration.Linux.LutrisConfig.Length > 0 && !choices.Any(x => x.Value == Configuration.Linux.LutrisConfig))
                choices.Add(new(Configuration.Linux.LutrisConfig, Path.GetFileNameWithoutExtension(Configuration.Linux.LutrisConfig)));
            fields.Add(Labeled("Lutris 条目", Choice("lutris-entry", Configuration.Linux.LutrisConfig, choices.ToArray(),
                v => { Configuration.Linux.LutrisConfig = v; Changed(); ShowPage(selectedPage); })));
        }
        if (FromLutris)
        {
            LutrisGame? game = null;
            try
            {
                game = Configuration.Linux.LutrisConfig.Length > 0 ? Discovery.ReadLutris(Configuration.Linux.LutrisConfig)
                    : LaunchSourcePolicy.MatchingLutrisGames(Configuration, DiscoveryState).FirstOrDefault();
            }
            catch (Exception ex) when (IsUserError(ex)) { fields.Add(Hint("所选条目暂时无法读取，请重新选择。")); }
            fields.Add(Badge("由 Lutris 管理", true));
            if (game is not null)
            {
                fields.Add(ReadOnlyDetail("游戏", game.Executable));
                fields.Add(ReadOnlyDetail("运行器", game.RunnerVersion.Length > 0 ? game.RunnerVersion : game.RunnerPath));
            }
            fields.Add(Hint("游戏路径、运行器、附加参数和环境变量均读取此条目。如需更改，请在 Lutris 中配置。"));
        }
        else
        {
            fields.Add(PathField("game-path", "游戏程序", Configuration.GameExecutable, v => Configuration.GameExecutable = v,
                false, "选择鸣潮游戏程序", true));
            var choices = new List<SettingOption> { new("", "请选择运行器") };
            choices.AddRange(DiscoveryState.Runners.Select(r => new SettingOption(r.Path, $"{r.Name} · {r.Kind}")));
            if (Configuration.Linux.Runner.Length > 0 && !choices.Any(x => x.Value == Configuration.Linux.Runner))
                choices.Add(new(Configuration.Linux.Runner, Path.GetFileName(Configuration.Linux.Runner)));
            fields.Add(Labeled("检测到的运行器", Choice("runner-choice", Configuration.Linux.Runner, choices.ToArray(),
                v => { Configuration.Linux.Runner = v; Changed(); ShowPage(selectedPage); })));
            fields.Add(PathField("runner-path", "运行器路径", Configuration.Linux.Runner, v => Configuration.Linux.Runner = v,
                false, "选择 proton 或 wine"));
            fields.Add(Grid("*,*", Labeled("运行器类型", Choice("runner-kind", Configuration.Linux.RunnerKind,
                    [new("auto", "自动判断"), new("proton", "Proton"), new("wine", "Wine")],
                    v => { Configuration.Linux.RunnerKind = v; Changed(); })),
                PathField("prefix-path", "前缀目录", Configuration.Linux.Prefix, v => Configuration.Linux.Prefix = v,
                    true, "选择 Wine / Proton 前缀")));
            fields.Add(Hint("Wine / UMU 使用包含 drive_c 的前缀；直接运行 Proton 时使用包含 pfx 的兼容数据目录。"));
        }
        return Column(14, fields.ToArray());
    }

    private Control GraphicsPage()
    {
        var memory = Row(22);
        foreach (var (mode, label) in new[] { ("file", "配置模式"), ("memory", "内存模式"), ("mixed", "混合模式") })
        {
            var radio = new AtomUI.Desktop.Controls.RadioButton
            {
                Name = "memory-mode-" + mode, Content = label, GroupName = "memory-application-mode",
                IsChecked = Configuration.TouchOnly.MemoryMode == mode
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true || Configuration.TouchOnly.MemoryMode == mode) return;
                Configuration.TouchOnly.MemoryMode = mode;
                Changed(); RefreshGraphics();
            };
            Avalonia.Automation.AutomationProperties.SetName(radio, label);
            memory.Children.Add(radio);
        }
        graphicsCount = Hint("");
        var categories = SettingsCatalog.TouchOptions.Select(x => x.Group).Distinct()
            .Select(g => new SettingOption(g, g)).ToArray();
        var search = new LineEdit { Name = "graphics-search", Text = graphicsSearch,
            PlaceholderText = "搜索设置，例如 阴影、光追、帧率", MinHeight = 38 };
        search.TextChanged += (_, _) => { graphicsSearch = search.Text ?? ""; RefreshGraphics(); };
        var controls = Grid("*,230", search, Choice("graphics-category", category, categories,
            v => { category = v; RefreshGraphics(); }));
        graphicsRows = new ContentControl();
        RefreshGraphics();
        var countRow = Grid("*,Auto", Row(10, Badge("仅触控模式", true), graphicsCount),
            UiStyle.Button("沿用此分类的设置", "refresh", () =>
            {
                foreach (var option in SettingsCatalog.TouchOptions.Where(x => x.Group == category))
                    Configuration.TouchOnly.Values[option.Key] = "keep";
                Changed(); RefreshGraphics();
            }, name: "reset-category"));
        return Column(18, Panel(Column(10, Text("图形应用方式", 15, true), memory,
                Hint("默认混合模式，全部使用配置预置；逐项勾选可补充运行时内存维护。沿用游戏设置的项目不会注入。"),
                Hint(MemoryGraphicsCatalog.Summary), Hint(MemoryGraphicsCatalog.ExperimentalWarning))), controls, countRow, graphicsRows,
            Hint("未指定的设置保持原值。部分效果可能受游戏云模式限制；帧率设置不保证解除云模式的原生帧率上限。"));
    }

    private void RefreshGraphics()
    {
        if (graphicsRows is null) return;
        int changed = Configuration.TouchOnly.Values.Count(x => x.Value != "keep");
        graphicsCount.Text = $"已调整 {changed} / {SettingsCatalog.TouchOptions.Length} 项";
        string query = graphicsSearch.Trim();
        var matches = SettingsCatalog.TouchOptions.Where(x => query.Length == 0 ? x.Group == category :
            (x.Label + " " + x.Key + " " + x.Help).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var rows = new List<Control>();
        foreach (var option in matches)
        {
            int bracket = option.Label.IndexOf('（');
            string label = bracket >= 0 ? option.Label[..bracket] : option.Label;
            string selected = Configuration.TouchOnly.Values.GetValueOrDefault(option.Key, "keep");
            bool eligible = MemoryGraphicsCatalog.Supports(option.Key);
            var heading = Row(10, Text(label, 14, true),
                Badge(eligible ? "可选内存注入" : "配置文件", eligible));
            var caption = Column(5, heading, Hint(option.Help));
            if (eligible && Configuration.TouchOnly.MemoryMode == "mixed")
            {
                var useMemory = new AtomUI.Desktop.Controls.CheckBox
                {
                    Name = "memory-setting-" + option.Key, Content = "内存注入（实验性）",
                    IsChecked = Configuration.TouchOnly.MemoryOptions.GetValueOrDefault(option.Key)
                };
                useMemory.IsCheckedChanged += (_, _) =>
                {
                    Configuration.TouchOnly.MemoryOptions[option.Key] = useMemory.IsChecked == true;
                    Changed();
                };
                caption.Children.Add(useMemory);
            }
            var input = Choice("setting-" + option.Key, selected, option.Choices,
                v => { Configuration.TouchOnly.Values[option.Key] = v; Changed(); });
            if (Configuration.TouchOnly.AdaptiveFullscreen && option.Key is "display-mode" or "resolution")
            {
                input.IsEnabled = false;
                caption.Children.Add(Hint("由首页的“自适应全屏”自动设置。"));
            }
            AtomUI.Desktop.Controls.ToolTip.SetTip(input, option.Label + "\n" + option.Key);
            var row = Grid("*,215", caption, input);
            row.Margin = new Thickness(20, 17);
            rows.Add(new Avalonia.Controls.Border
            {
                BorderBrush = UiStyle.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = row
            });
        }
        if (rows.Count == 0) rows.Add(new TextBlock { Text = "没有找到匹配的设置", Foreground = Muted, Margin = new Thickness(22) });
        graphicsRows.Content = Panel(Column(0, rows.ToArray()), 0);
    }

    private Control InputPage()
    {
        var settings = Configuration.TouchOnly;
        return Column(20,
            Panel(Column(20, Row(10, UiStyle.Icon("touch"), Text("触控输入", 16, true)),
                ToggleRow("mouse-filter", "启动时过滤鼠标", "仅在目标游戏处于前台时生效，保留触摸与键盘输入。", settings.MouseFilter,
                    v => { settings.MouseFilter = v; Changed(); }),
                Hint("切换到其他窗口后，鼠标会恢复正常。普通模式不启用过滤。"))),
            Panel(Column(20, Row(10, UiStyle.Icon("input"), Text("快捷键", 16, true)),
                ToggleRow("hotkey-enabled", "允许快捷键切换过滤", "按一次关闭，再按一次开启。", settings.HotkeyEnabled,
                    v => { settings.HotkeyEnabled = v; Changed(); }),
                Labeled("开关快捷键", Edit("hotkey", settings.Hotkey, "Ctrl+Alt+F10", v => { settings.Hotkey = v; Changed(); })),
                Hint("支持 Ctrl、Alt、Shift、Win 与 F1–F24 或字母数字组合。"))),
            Panel(Column(16, Text("当前游戏会话", 16, true), Hint("以下操作只作用于已经启动的游戏，不修改保存的配置。"),
                Row(10, UiStyle.Button("关闭鼠标过滤", "input", () => Action(() => global::Launcher.ReleaseMouse(), "已请求关闭鼠标过滤")),
                    UiStyle.Button("切换过滤状态", "refresh", () => Action(() => global::Launcher.ToggleMouse(), "已发送切换请求"))))));
    }

    private Control SettingsPage()
    {
        var sections = new List<Control> { AppearanceSection() };
        if (!FromLutris)
        {
            var profile = Configuration.Selected;
            sections.Add(Panel(Column(16, Grid("*,Auto", Text("附加启动参数", 16, true),
                    Badge(Configuration.Mode == "touch" ? "触控模式" : "普通模式", true)),
                Edit("custom-arguments", profile.CustomArguments, "例如 -windowed -ResX=1920 -ResY=1080",
                    v => { profile.CustomArguments = v; Changed(); }),
                Hint("按参数直接执行。画质、DirectX、启动模式和跳过动画请使用启动页的对应选项。"))));
        }
        if (OperatingSystem.IsLinux() && !FromLutris)
        {
            var environment = new TextArea
            {
                Name = "linux-environment", Text = environmentText, MinHeight = 120,
                PlaceholderText = "每行 NAME=值，例如 PROTON_USE_XALIA=0"
            };
            environment.TextChanged += (_, _) =>
            {
                string next = environment.Text ?? "";
                if (next == environmentText) return;
                environmentText = next; Changed();
            };
            sections.Add(Panel(Column(16, Text("Linux 运行环境", 16, true),
                Labeled("前置命令", Edit("linux-wrapper", Configuration.Linux.Wrapper, "例如 gamemoderun",
                    v => { Configuration.Linux.Wrapper = v; Changed(); })),
                PathField("umu-path", "UMU 路径（可选）", Configuration.Linux.Umu, v => Configuration.Linux.Umu = v,
                    false, "选择 umu-run"),
                Labeled("环境变量", environment),
                Hint("Proton 优先使用检测到的 UMU；没有 UMU 时直接运行所选 Proton。前置命令不经过 shell。"))));
        }
        if (FromLutris)
            sections.Add(Panel(Row(14, UiStyle.Icon("link", 24), Column(6,
                Text("启动环境由 Lutris 管理", 15, true), Hint("附加参数、前置命令和环境变量使用所选 Lutris 条目。切换为手动配置后可在这里编辑。")))));
        sections.Add(Panel(Column(16, Row(10, UiStyle.Icon("link"), Text("快捷启动", 16, true)),
            Hint(OperatingSystem.IsWindows() ? "在桌面为普通与触控模式各创建一个快捷方式。" : "在应用菜单中为普通与触控模式各创建一个入口。"),
            Row(10, UiStyle.Button("创建两个快捷入口", "link", () => CreateShortcuts(null), name: "create-shortcuts"),
                UiStyle.Button("选择保存位置", "folder", () => Run(async () =>
                {
                    var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "快捷方式保存目录", AllowMultiple = false });
                    if (selected.FirstOrDefault()?.TryGetLocalPath() is { } directory) CreateShortcuts(directory);
                    foreach (var item in selected) item.Dispose();
                }))), Hint("快捷入口直接启动游戏，并读取最新保存的配置。"))));
        sections.Add(Panel(Column(16, Row(10, UiStyle.Icon("file"), Text("配置与记录", 16, true)),
            ReadOnlyDetail("配置文件", configPath),
            Row(10, UiStyle.Button("打开配置目录", "folder", () => OpenDirectory(Path.GetDirectoryName(configPath)!)),
                UiStyle.Button("打开运行记录", "file", () => OpenDirectory(AppPaths.StateDirectory))),
            Hint("临时图形设置会在游戏退出后恢复。本程序不安装或管理 OptiScaler。"))));
        return Column(20, sections.ToArray());
    }

    private Control AppearanceSection()
    {
        var settings = Configuration.Appearance;
        var theme = Choice("appearance-theme", settings.Theme,
            [new("system", "随系统变化"), new("light", "日间模式"), new("dark", "暗夜模式")],
            value => { settings.Theme = value; Changed(); UpdateAppearance(); });
        var controls = new List<Control>
        {
            Row(10, UiStyle.Icon("appearance"), Text("窗口外观", 16, true)),
            Labeled("主题", theme)
        };
        if (OperatingSystem.IsWindows())
        {
            controls.Add(Labeled("窗口特效", Choice("windows-effect", SelectedEffect,
                [new("acrylic", "亚克力"), new("mica", "云母（Mica）"), new("none", "无特效")],
                value => { settings.WindowsEffect = value; Changed(); ApplySurface(settings); })));
            controls.Add(Hint("Windows 11 默认云母，Windows 10 默认亚克力。标题栏和正文使用统一背景，系统不支持时自动回退。"));
        }
        var percentage = Text(settings.BackgroundOpacity + "%", 12, color: Accent);
        percentage.Name = "appearance-opacity-value";
        opacitySlider = new Slider
        {
            Name = "appearance-opacity", Minimum = 30, Maximum = 100,
            Value = settings.BackgroundOpacity, TickFrequency = 1, IsSnapToTickEnabled = true,
            ValueFormatTemplate = "{0:0}%", HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 32
        };
        opacitySlider.PropertyChanged += (_, args) =>
        {
            if (args.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            int value = (int)Math.Round(((Slider)args.Sender!).Value);
            if (value == settings.BackgroundOpacity) return;
            settings.BackgroundOpacity = value; percentage.Text = value + "%";
            Changed(); ApplySurface(settings);
        };
        Avalonia.Automation.AutomationProperties.SetName(opacitySlider, "窗口背景不透明度");
        controls.Add(Column(8, Grid("*,Auto", Text("窗口背景不透明度", 12, color: Muted), percentage), opacitySlider));
        opacityHint = Hint("");
        materialStatus = Hint("");
        materialStatus.Name = "appearance-material-status";
        controls.Add(opacityHint); controls.Add(materialStatus);
        controls.Add(Hint("调整立即预览，保存配置后下次打开仍然生效。"));
        UpdateAppearanceControls();
        return Panel(Column(16, controls.ToArray()));
    }

    private void UpdateAppearanceControls()
    {
        if (opacitySlider is not null) opacitySlider.IsEnabled = !IsOpacityManagedBySystem;
        if (opacityHint is not null)
            opacityHint.Text = IsOpacityManagedBySystem
                ? "系统材质的透明度由 Windows 管理。选择“无特效”后恢复之前设置的背景不透明度。"
                : "仅调整窗口背景，文字和按钮保持清晰。100% 为不透明。";
        if (materialStatus is not null) materialStatus.Text = SurfaceDescription();
    }

    private void UpdateAppearance(PlatformThemeVariant? systemTheme = null)
    {
        var update = ((LauncherApplication)Application.Current!).ApplyAppearanceAsync(systemTheme);
        AppearanceUpdate = update;
        Run(() => update);
    }

    private void SystemColorsChanged(object? sender, PlatformColorValues colors) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!closed && Configuration.Appearance.Theme == "system") UpdateAppearance(colors.ThemeVariant);
        });

    private async Task ScanAsync()
    {
        if (scanning) return;
        scanning = true; SetMessage("正在检测安装信息…");
        if (selectedPage == 0) ShowPage(0);
        try
        {
            DiscoveryResult found = await Task.Run(Discovery.Scan);
            if (closed) return;
            DiscoveryState = found;
            if (OperatingSystem.IsWindows() && LaunchSourcePolicy.SelectSingleWindowsInstallation(Configuration, found)) Changed();
            SetMessage(OperatingSystem.IsWindows() ? found.InstalledGames.Count == 0 ? "请选择游戏程序" : $"找到 {found.InstalledGames.Count} 个安装"
                : found.LutrisGames.Count > 0 ? $"找到 {found.LutrisGames.Count} 个 Lutris 条目" : $"找到 {found.Runners.Count} 个运行器");
        }
        catch (Exception ex) when (IsUserError(ex)) { SetMessage("检测失败：" + ex.Message, true); }
        finally { scanning = false; if (!closed && selectedPage is 0 or 3) ShowPage(selectedPage); }
    }

    internal bool Save()
    {
        try
        {
            ReadApplicableEnvironment();
            ConfigStore.Save(configPath, Configuration);
            dirty = false; saveStatus.Text = "配置已保存"; saveStatus.Foreground = Muted;
            SetMessage("快捷入口将使用最新配置"); return true;
        }
        catch (Exception ex) when (IsUserError(ex)) { SetMessage("保存失败：" + ex.Message, true); return false; }
    }

    private void ReadApplicableEnvironment()
    {
        if (OperatingSystem.IsLinux() && !FromLutris)
            Configuration.Linux.Environment = CommandLine.ParseEnvironment(environmentText);
    }

    private async Task PreviewAsync()
    {
        ReadApplicableEnvironment();
        DisplayResolution? desktopResolution = AdaptiveScreenResolution();
        var plan = await Task.Run(() => PlanBuilder.Build(Configuration, desktopResolution: desktopResolution));
        string check;
        try { PlanBuilder.ValidateFiles(plan); check = "路径检查通过"; }
        catch (ArgumentException ex) { check = "需要处理：" + ex.Message; }
        var preview = new Window
        {
            Title = "启动预览", Width = 850, Height = 640, MinWidth = 650, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily
        };
        var details = new TextArea
        {
            Name = "preview-details", IsReadOnly = true, Text = check + "\n\n" + JsonSerializer.Serialize(plan, ProductJson.Default.LaunchPlan),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        var root = new Avalonia.Controls.Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16, Margin = new Thickness(24) };
        root.Children.Add(Column(6, Text("确认实际启动配置", 20, true), Hint("此窗口只预览命令，不启动游戏或修改文件。")));
        Avalonia.Controls.Grid.SetRow(details, 1); root.Children.Add(details);
        var close = UiStyle.Button("关闭", "close", () => preview.Close());
        close.HorizontalAlignment = HorizontalAlignment.Right;
        Avalonia.Controls.Grid.SetRow(close, 2); root.Children.Add(close);
        preview.Content = root;
        preview.ApplySurface(Configuration.Appearance);
        await preview.ShowDialog(this);
    }

    private async Task LaunchAsync()
    {
        if (launching || global::Launcher.SessionIsLive()) { SetMessage("已有游戏会话正在运行"); return; }
        ReadApplicableEnvironment();
        DisplayResolution? desktopResolution = AdaptiveScreenResolution();
        var plan = await Task.Run(() => PlanBuilder.Build(Configuration, desktopResolution: desktopResolution));
        PlanBuilder.ValidateFiles(plan);
        if (!Save()) return;
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位启动器。");
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
            throw new InvalidOperationException("请使用发布后的程序启动游戏。");
        var info = SessionStartInfo(executable, configPath, Configuration.Mode, desktopResolution);
        using var child = Process.Start(info) ?? throw new IOException("无法启动游戏会话。");
        BeginSessionTracking();
        try
        {
            Task<string> errorTask = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            string error = await errorTask;
            if (!closed) SetMessage(child.ExitCode == 0 ? "游戏已退出，临时设置已恢复" :
                "启动失败：" + (error.Length > 0 ? error.Trim() : $"退出码 {child.ExitCode}"), child.ExitCode != 0);
        }
        finally { EndSessionTracking(); }
    }

    internal string SessionMessage => message.Text ?? "";
    internal void BeginSessionTracking()
    {
        launching = true; waitingForSession = true; launchButton.IsEnabled = false;
        SetMessage("正在启动，可关闭配置窗口");
    }

    internal void EndSessionTracking()
    {
        launching = false; waitingForSession = false;
        if (!closed) launchButton.IsEnabled = true;
    }

    internal DisplayResolution? AdaptiveScreenResolution()
    {
        if (Configuration.Mode != "touch" || !Configuration.TouchOnly.AdaptiveFullscreen) return null;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return DesktopDisplay.ReadPrimary();
        var resolution = new DisplayResolution(screen.Bounds.Width, screen.Bounds.Height);
        resolution.Validate();
        return resolution;
    }

    internal static ProcessStartInfo SessionStartInfo(string executable, string path, string mode,
        DisplayResolution? desktopResolution = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (string arg in new[] { "launch", "--config", path, "--mode", mode, "--notify" }) info.ArgumentList.Add(arg);
        if (desktopResolution is { } screen)
        {
            screen.Validate();
            info.ArgumentList.Add("--desktop-resolution");
            info.ArgumentList.Add(screen.ToString());
        }
        return info;
    }

    private async Task ConfirmCloseAsync()
    {
        confirmingClose = true;
        try
        {
            var prompt = new Window
            {
                Name = "save-changes-dialog", Title = "保存更改", Width = 460,
                SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily
            };
            var actions = Row(10, UiStyle.Button("取消", "close", () => prompt.Close(0), name: "close-cancel"),
                UiStyle.Button("不保存", "arrow", () => prompt.Close(1), name: "close-discard"),
                UiStyle.Button("保存", "save", () => prompt.Close(2), true, name: "close-save"));
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 8, 0, 0);
            var content = Column(16, Text("保存这次调整？", 21, true),
                Hint("还有未保存的配置。保存后，快捷入口也会使用这些更改。"), actions);
            content.Name = "save-changes-content";
            content.Margin = new Thickness(24); prompt.Content = content;
            prompt.ApplySurface(Configuration.Appearance);
            int result = await prompt.ShowDialog<int>(this);
            if (result == 1 || result == 2 && Save()) { allowClose = true; Close(); }
        }
        finally { confirmingClose = false; }
    }

    internal void UpdateStatus()
    {
        var status = global::Launcher.CurrentStatus();
        bool live = global::Launcher.SessionIsLive();
        if (waitingForSession && live)
        {
            waitingForSession = false;
            SetMessage("游戏正在运行，可关闭配置窗口");
        }
        runtimeStatus.Text = live ? status!.Filtering ? "游戏中 · 过滤开启" : "游戏中 · 过滤关闭" : launching ? "启动中…" : "未运行";
        if (live && status!.FpsUnlockTarget > 0)
            runtimeStatus.Text += status.FpsUnlockState switch
            {
                "requested" => $" · 已发送 {status.FpsUnlockTarget} 帧请求",
                "applied" => $" · 已维护 {status.FpsUnlockTarget} 帧上限",
                "skipped" => " · 已跳过内存帧率请求",
                "failed" => $" · 解锁请求失败 ({status.FpsUnlockError})",
                _ => " · 等待帧率设置"
            };
        if (live && status!.MemoryGraphicsState != "disabled")
            runtimeStatus.Text += status.MemoryGraphicsState switch
            {
                "applied" => $" · 内存已应用 {status.MemoryGraphicsApplied} 项",
                "unavailable" => " · 内存请求无可用目标",
                "no-settings" => " · 无需注入",
                "unsupported-version" => " · 游戏版本暂不支持注入",
                "layout-mismatch" => " · 内存校验未通过，已停止注入",
                "failed" => $" · 内存应用失败 ({status.MemoryGraphicsError})",
                _ => " · 等待内存配置"
            };
        if (live && status!.MemoryGraphicsSkipped > 0)
            runtimeStatus.Text += $" · 跳过 {status.MemoryGraphicsSkipped} 项";
        if (live && status!.MemoryGraphicsState == "applied")
            runtimeStatus.Text += status.MemoryFrameGenerationState switch
            {
                "active" => " · 插帧设置已恢复",
                "waiting-scene" => " · 插帧等待场景恢复",
                "off" => " · 插帧已关闭",
                _ => ""
            };
        launchButton.IsEnabled = !live && !launching;
    }

    private void CreateShortcuts(string? directory)
    {
        if (!Save()) return;
        Action(() => { Shortcuts.CreateBoth(configPath, directory); return 0; }, "两个快捷入口已创建");
    }
    private void OpenDirectory(string path) => Action(() =>
    {
        Directory.CreateDirectory(path);
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(path); Process.Start(info)?.Dispose(); return 0;
    }, "已打开目录");
    private void Action(Func<int> action, string success)
    {
        try { SetMessage(action() == 0 ? success : "操作未完成，请查看运行记录"); }
        catch (Exception ex) when (IsUserError(ex)) { SetMessage(ex.Message, true); }
    }
    private void SetMessage(string text, bool error = false)
    {
        message.Text = text; message.Foreground = error ? Warning : Muted;
        message.MaxWidth = 340; message.MaxLines = 2;
        AtomUI.Desktop.Controls.ToolTip.SetTip(message, text);
    }
    private void Run(Func<Task> action)
    {
        operations.RemoveAll(t => t.IsCompleted);
        operations.Add(GuardAsync(action));
    }
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (IsUserError(ex)) { if (!closed) SetMessage(ex.Message, true); }
    }

    private static Control Labeled(string label, Control input) => Column(8, Text(label, 12, color: Muted), input);
    private static Control ReadOnlyDetail(string label, string value) => Grid("60,*", Hint(label), Text(value, 12));
    private static LineEdit Edit(string name, string value, string placeholder, Action<string> change)
    {
        var edit = new LineEdit { Name = name, Text = value, PlaceholderText = placeholder, MinHeight = 38, MaxLength = 8192 };
        edit.TextChanged += (_, _) =>
        {
            string next = edit.Text ?? "";
            if (next == value) return;
            value = next; change(next);
        };
        return edit;
    }
    private static Select Choice(string name, string value, SettingOption[] choices, Action<string> change)
    {
        var options = choices.Select(x => new SelectOption { Content = x.Label, Header = x.Label, IsEnabled = x.IsEnabled }).ToArray();
        var input = new TouchSelect
        {
            Name = name, OptionsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38, VerticalAlignment = VerticalAlignment.Center,
            Tag = choices.Select((c, i) => (c.Value, Option: (ISelectOption)options[i])).ToDictionary(c => c.Value, c => c.Option)
        };
        int selected = Array.FindIndex(choices, x => x.Value == value);
        input.SelectedOption = options[Math.Max(0, selected)];
        input.SelectionChanged += (_, _) =>
        {
            int index = Array.IndexOf(options, input.SelectedOption);
            if (index >= 0 && choices[index].IsEnabled && choices[index].Value != value)
            {
                value = choices[index].Value; change(value);
            }
        };
        return input;
    }
    private static Control ToggleRow(string name, string label, string hint, bool value, Action<bool> change)
    {
        var toggle = new AtomUI.Desktop.Controls.ToggleSwitch
        {
            Name = name, IsChecked = value, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            bool next = toggle.IsChecked == true;
            if (next == value) return;
            value = next; change(next);
        };
        Avalonia.Automation.AutomationProperties.SetName(toggle, label);
        return Grid("*,Auto", Column(5, Text(label, 14), Hint(hint)), toggle);
    }
    private Control PathField(string name, string label, string value, Action<string> change, bool folder, string title, bool game = false)
    {
        var box = Edit(name, value, folder ? "选择文件夹" : "选择文件", v => { change(v); Changed(); if (game) RefreshResources(); });
        var browse = UiStyle.Button("浏览", "folder", () => Run(async () =>
        {
            if (folder)
            {
                var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
                if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path) box.Text = path;
                foreach (var item in selected) item.Dispose();
            }
            else
            {
                var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title, AllowMultiple = false,
                    FileTypeFilter = game ? [new FilePickerFileType("Windows 程序") { Patterns = ["*.exe"] }, FilePickerFileTypes.All] : [FilePickerFileTypes.All]
                });
                if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    if (game)
                    {
                        string? resolved = WindowsDiscovery.FindGameExecutable(path);
                        if (resolved is null) throw new ArgumentException("此位置没有找到 Client-Win64-Shipping.exe，请选择完整的鸣潮游戏安装。");
                        path = resolved;
                    }
                    box.Text = path;
                }
                foreach (var item in selected) item.Dispose();
            }
        }), name: name + "-browse");
        return Labeled(label, Grid("*,Auto", box, browse));
    }
    internal void CloseAfterSmoke() { allowClose = true; Close(); }
    internal string Backend => backendStatus.Text ?? "";
    internal bool HasUnsavedChanges => dirty;
    private static bool IsUserError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or NotSupportedException;

    private sealed class EllipseDot : Avalonia.Controls.Shapes.Ellipse
    {
        public EllipseDot(IBrush brush) { Width = Height = 6; Fill = brush; VerticalAlignment = VerticalAlignment.Center; }
    }
}
