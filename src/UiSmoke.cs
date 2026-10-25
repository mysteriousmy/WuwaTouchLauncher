using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AtomUI;
using AtomUI.Theme;
using AtomUI.Desktop.Controls;
using Button = AtomUI.Desktop.Controls.Button;
using Select = AtomUI.Desktop.Controls.Select;
using Slider = AtomUI.Desktop.Controls.Slider;

/// <summary>Exercises actual presented controls against a copied profile; never clicks Launch.</summary>
internal static class UiSmoke
{
    internal static async Task RunAsync(LauncherWindow window, string directory)
    {
        var result = new UiSmokeResult { Backend = window.Backend };
        bool requireWindowsGpu = OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("WUWA_TEST_REQUIRE_GPU") == "1";
        Directory.CreateDirectory(directory);
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "native-aot");
            await window.InitialScan;
            await Task.Delay(500);
            var shell = (Panel)window.Content!;
            bool windows = OperatingSystem.IsWindows();
            var probe = new RenderBackendProbe { Width = 1, Height = 1, IsHitTestVisible = false };
            shell.Children.Add(probe);
            try
            {
                result.Renderer = await probe.Backend.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(result.Renderer is "Gpu" or "Software", "live-skia-render-context");
                if (!windows)
                    Require(result.Renderer == "Software", "software-renderer-without-gpu-context");
                else
                {
                    if (requireWindowsGpu)
                        Require(result.Renderer == "Gpu", "windows-gpu-required");
                    result.Checks.Add(result.Renderer == "Gpu" ? "windows-gpu-renderer" : "windows-software-fallback");
                }
            }
            finally { shell.Children.Remove(probe); }
            Capture("launch");
            CheckSessionProgress();
            Require(window.Bounds.Width >= 900 && window.Bounds.Height >= 600, "window-layout");
            if (!windows) Require(!window.HasUnsavedChanges, "initial-render-does-not-change-profile");
            if (windows)
            {
                Require(FindOptional<Select>("integration") is null && FindOptional<LineEdit>("runner-path") is null &&
                    FindOptional<Select>("lutris-entry") is null, "windows-hides-linux-launch-options");
                Require(FindOptional<LineEdit>("game-path") is not null &&
                    FindOptional<Button>("game-path-browse") is not null, "windows-manual-picker-available");
                await CheckSessionErrorsAsync();
                await CheckCaptionButtonsAsync();
                CheckWindowsFonts();
            }
            string originalIntegration = window.Configuration.Linux.Integration;
            if (!windows)
            {
                if (originalIntegration == "auto" && window.DiscoveryState.LutrisGames.Any(g =>
                    g.Executable == window.Configuration.GameExecutable))
                {
                    Require(FindOptional<LineEdit>("game-path") is null &&
                        FindOptional<LineEdit>("runner-path") is null, "auto-lutris-hides-manual-source");
                    Click("nav-3"); await Step();
                    Require(FindOptional<LineEdit>("custom-arguments") is null &&
                        FindOptional<LineEdit>("linux-wrapper") is null &&
                        FindOptional<TextArea>("linux-environment") is null, "auto-lutris-hides-manual-overrides");
                    Capture("auto-lutris-settings");
                    Click("nav-0"); await Step();
                }
                Choose("integration", "lutris"); await Step();
                Click("nav-3"); await Step();
                Require(FindOptional<LineEdit>("custom-arguments") is null &&
                    FindOptional<LineEdit>("linux-wrapper") is null &&
                    FindOptional<TextArea>("linux-environment") is null, "lutris-hides-manual-overrides");
                Capture("lutris-settings");
                Click("nav-0"); await Step(); Choose("integration", "manual"); await Step();
            }
            Click("mode-touch"); await Step();
            await CheckTouchSelectionAsync();
            var adaptive = Find<AtomUI.Desktop.Controls.ToggleSwitch>("adaptive-fullscreen");
            Require(adaptive.IsChecked != true && !window.Configuration.TouchOnly.AdaptiveFullscreen,
                "adaptive-fullscreen-default-off");
            adaptive.IsChecked = true; await Step();
            Require(window.Configuration.TouchOnly.AdaptiveFullscreen, "adaptive-fullscreen-home-toggle");
            DisplayResolution desktop = window.AdaptiveScreenResolution()
                ?? throw new InvalidOperationException("Adaptive fullscreen did not read a screen.");
            var primary = window.Screens.Primary ?? window.Screens.All.First();
            Require(desktop.Width == primary.Bounds.Width && desktop.Height == primary.Bounds.Height,
                "adaptive-fullscreen-physical-pixels");
            var sessionInfo = LauncherWindow.SessionStartInfo(Environment.ProcessPath!,
                Path.Combine(directory, "profiles-test.json"), "touch", desktop);
            int snapshotIndex = sessionInfo.ArgumentList.IndexOf("--desktop-resolution");
            Require(snapshotIndex >= 0 && sessionInfo.ArgumentList[snapshotIndex + 1] == desktop.ToString(),
                "adaptive-fullscreen-session-snapshot");
            if (windows)
            {
                Require(DesktopDisplay.ReadPrimary() == desktop, "windows-display-api-physical-pixels");
                var elevation = WindowsElevation.StartInfo(Environment.ProcessPath!,
                    Path.Combine(directory, "profiles-test.json"), sessionInfo.ArgumentList.Skip(3), Guid.NewGuid().ToString("N"));
                int elevationIndex = elevation.ArgumentList.IndexOf("--desktop-resolution");
                Require(elevationIndex >= 0 && elevation.ArgumentList[elevationIndex + 1] == desktop.ToString(),
                    "adaptive-fullscreen-elevation-snapshot");
            }
            Capture("adaptive-fullscreen");
            Click("nav-1"); await Step();
            var mixedMode = Find<AtomUI.Desktop.Controls.RadioButton>("memory-mode-mixed");
            Require(mixedMode.IsChecked == true && window.Configuration.TouchOnly.MemoryMode == "mixed" &&
                !window.Configuration.TouchOnly.MemoryOptions.Any(x => x.Value), "memory-mixed-default-all-file");
            Require(!Find<AtomUI.Desktop.Controls.CheckBox>("memory-setting-shadow").IsChecked.GetValueOrDefault(),
                "memory-mixed-checkbox-default-off");
            Find<LineEdit>("graphics-search").Text = "native-fg"; await Step();
            Find<AtomUI.Desktop.Controls.CheckBox>("memory-setting-native-fg").IsChecked = true; await Step();
            Require(window.Configuration.TouchOnly.MemoryOptions["native-fg"], "memory-mixed-checkbox-updates-profile");
            Find<AtomUI.Desktop.Controls.RadioButton>("memory-mode-memory").IsChecked = true; await Step();
            Require(window.Configuration.TouchOnly.MemoryMode == "memory" &&
                FindOptional<AtomUI.Desktop.Controls.CheckBox>("memory-setting-native-fg") is null,
                "memory-all-mode-hides-individual-checkboxes");
            Choose("setting-native-fg", "5"); await Step();
            Require(window.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Any(x => x.Text == "可选内存注入"),
                "memory-experimental-all-frame-generation-levels");
            Find<AtomUI.Desktop.Controls.RadioButton>("memory-mode-file").IsChecked = true; await Step();
            Require(window.Configuration.TouchOnly.MemoryMode == "file" &&
                !window.Configuration.TouchOnly.UsesMemory("native-fg"), "memory-file-mode-disables-runtime");
            mixedMode.IsChecked = true; await Step();
            Require(Find<AtomUI.Desktop.Controls.CheckBox>("memory-setting-native-fg").IsChecked == true &&
                !Find<AtomUI.Desktop.Controls.RadioButton>("memory-mode-file").IsChecked.GetValueOrDefault() &&
                !Find<AtomUI.Desktop.Controls.RadioButton>("memory-mode-memory").IsChecked.GetValueOrDefault(),
                "memory-radio-exclusive-and-selections-retained");
            Capture("memory-injection");
            Choose("setting-native-fg", "keep");
            Find<LineEdit>("graphics-search").Text = ""; await Step();
            int total = 0, memoryChoices = 0;
            foreach (string group in SettingsCatalog.TouchOptions.Select(o => o.Group).Distinct())
            {
                Choose("graphics-category", group); await Step();
                int count = window.GetLogicalDescendants().OfType<Select>().Count(c => c.Name?.StartsWith("setting-", StringComparison.Ordinal) == true);
                Require(count == SettingsCatalog.TouchOptions.Count(o => o.Group == group), "graphics-category:" + group);
                total += count;
                memoryChoices += window.GetLogicalDescendants().OfType<AtomUI.Desktop.Controls.CheckBox>()
                    .Count(c => c.Name?.StartsWith("memory-setting-", StringComparison.Ordinal) == true);
            }
            Require(memoryChoices == 54 && !MemoryGraphicsCatalog.Supports("display-mode") &&
                !MemoryGraphicsCatalog.Supports("resolution"), "memory-all-54-options-except-display-and-resolution");
            Require(total == SettingsCatalog.TouchOptions.Length && total == 56, "all-56-graphics-options");
            Choose("graphics-category", "显示与帧率"); await Step();
            Require(!Find<Select>("setting-display-mode").IsEnabled && !Find<Select>("setting-resolution").IsEnabled,
                "adaptive-fullscreen-owns-display-settings");
            Click("nav-0"); await Step();
            Find<AtomUI.Desktop.Controls.ToggleSwitch>("adaptive-fullscreen").IsChecked = false; await Step();
            Require(!window.Configuration.TouchOnly.AdaptiveFullscreen && window.AdaptiveScreenResolution() is null,
                "adaptive-fullscreen-can-be-disabled");
            Click("nav-1"); await Step();
            Require(Find<Select>("setting-display-mode").IsEnabled && Find<Select>("setting-resolution").IsEnabled,
                "adaptive-fullscreen-off-enables-manual-settings");
            Click("nav-0"); await Step();
            Find<AtomUI.Desktop.Controls.ToggleSwitch>("adaptive-fullscreen").IsChecked = true; await Step();
            Click("nav-1"); await Step();
            Choose("setting-unlock-fps", "240"); await Step();
            Capture("fps-unlock");
            Choose("graphics-category", "画质与场景"); await Step(); Choose("setting-shadow", "3"); await Step();
            Find<LineEdit>("graphics-search").Text = "光追"; await Step();
            Require(FindOptional<Select>("setting-ray-quality") is not null, "graphics-search-across-categories");
            Find<LineEdit>("graphics-search").Text = ""; await Step();
            Choose("graphics-category", "光线追踪"); await Step();
            Choose("setting-ray-quality", "3"); await Step();
            Capture("graphics");
            Click("nav-0"); await Step(); Click("mode-normal"); await Step();
            Require(FindOptional<AtomUI.Desktop.Controls.ToggleSwitch>("adaptive-fullscreen") is null &&
                window.AdaptiveScreenResolution() is null, "adaptive-fullscreen-hidden-in-normal-mode");
            Choose("resources", "keep"); await Step();
            Click("nav-3"); await Step();
            await CheckAppearanceAsync();
            Find<LineEdit>("custom-arguments").Text = "-windowed"; await Step();
            if (!windows)
            {
                Find<LineEdit>("linux-wrapper").Text = "测试 龘𠮷 --launcher-check"; await Step();
            }
            else
                Require(FindOptional<LineEdit>("linux-wrapper") is null && FindOptional<TextArea>("linux-environment") is null &&
                    FindOptional<LineEdit>("umu-path") is null, "windows-hides-linux-advanced-options");
            Capture("settings");
            Click("nav-2"); await Step();
            Find<LineEdit>("hotkey").Text = "Ctrl+Shift+F9"; await Step();
            Capture("input");
            Click("nav-0"); await Step(); Click("mode-touch"); await Step();
            Click("nav-1"); await Step();
            Require(window.Configuration.Normal.CustomArguments == "-windowed" &&
                window.Configuration.Normal.Resources == "keep" &&
                window.Configuration.TouchOnly.Values["shadow"] == "3" &&
                window.Configuration.TouchOnly.Values["ray-quality"] == "3" &&
                window.Configuration.TouchOnly.Values["unlock-fps"] == "240" &&
                window.Configuration.TouchOnly.AdaptiveFullscreen &&
                window.Configuration.TouchOnly.MemoryMode == "mixed" &&
                window.Configuration.TouchOnly.MemoryOptions["native-fg"] &&
                window.HasUnsavedChanges, "edits-survive-navigation-and-mode-switch");
            window.Close();
            await Task.Delay(350);
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            var prompt = lifetime.Windows.Single(w => w.Name == "save-changes-dialog");
            prompt.UpdateLayout();
            foreach (string name in new[] { "close-cancel", "close-discard", "close-save" })
            {
                var button = prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name);
                Point position = button.TranslatePoint(new Point(0, 0), prompt)
                    ?? throw new InvalidOperationException("Dialog button has no visual position.");
                Require(button.Bounds.Width > 0 && button.Bounds.Height >= 30 &&
                    position.X >= 0 && position.Y >= 0 &&
                    position.X + button.Bounds.Width <= prompt.ClientSize.Width &&
                    position.Y + button.Bounds.Height + 20 <= prompt.ClientSize.Height,
                    "close-dialog-button-visible:" + name);
            }
            CheckUnifiedSurface((AppearanceWindow)prompt, "close-dialog");
            Capture("close-dialog", prompt);
            prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "close-cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Step();
            Require(window.IsVisible && window.HasUnsavedChanges &&
                !lifetime.Windows.Any(w => w.Name == "save-changes-dialog"), "close-cancel-preserves-draft");
            Click("save-config"); await Step();
            Require(!window.HasUnsavedChanges, "save-succeeds");
            if (!windows)
            {
                Click("nav-0"); await Step(); Choose("integration", "lutris"); await Step();
                Click("nav-3"); await Step();
                Require(FindOptional<LineEdit>("linux-wrapper") is null &&
                    window.Configuration.Linux.Wrapper == "测试 龘𠮷 --launcher-check", "hidden-manual-values-retained");
                Click("nav-0"); await Step(); Choose("integration", originalIntegration); await Step();
                Click("save-config"); await Step();
            }
            Click("nav-0"); await Step(); Capture("launch-final");
            if (windows)
            {
                Require(window.MemoryReclaimer.Attempts == 1, "windows-startup-working-set-request");
                int attempts = window.MemoryReclaimer.Attempts;
                window.WindowState = WindowState.Minimized;
                await Task.Delay(650);
                Require(window.MemoryReclaimer.Attempts == attempts + 1 &&
                    window.MemoryReclaimer.LastSucceeded, "windows-minimize-working-set-request");
                window.WindowState = WindowState.Normal;
                await Task.Delay(650);
                Require(window.MemoryReclaimer.Attempts == attempts + 1, "working-set-trim-is-not-periodic");
                result.MemoryTrimAttempts = window.MemoryReclaimer.Attempts;
                result.WorkingSetBefore = window.MemoryReclaimer.WorkingSetBefore;
                result.WorkingSetAfter = window.MemoryReclaimer.WorkingSetAfter;
            }
            else Require(window.MemoryReclaimer.Attempts == 0, "linux-does-not-call-windows-memory-api");
            result.Passed = true;
        }
        catch (Exception ex) { result.Error = ex.ToString(); }
        finally
        {
            result.Backend = window.Backend;
            File.WriteAllText(Path.Combine(directory, "result.json"),
                JsonSerializer.Serialize(result, UiSmokeJson.Default.UiSmokeResult));
            window.CloseAfterSmoke();
            if (!result.Passed && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown(1);
        }

        T? FindOptional<T>(string name) where T : Control =>
            window.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
        T Find<T>(string name) where T : Control =>
            FindOptional<T>(name) ?? throw new InvalidOperationException("Missing control: " + name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Choose(string name, string value)
        {
            var input = Find<Select>(name);
            var options = (Dictionary<string, ISelectOption>)input.Tag!;
            input.SelectedOption = options[value];
        }
        static Task Step() => Task.Delay(90);
        void CheckSessionProgress()
        {
            Require(!global::Launcher.SessionIsLive(), "session-progress-fixture-isolated");
            string status = Path.Combine(AppPaths.StateDirectory, "status.json");
            Directory.CreateDirectory(AppPaths.StateDirectory);
            byte[]? previous = File.Exists(status) ? File.ReadAllBytes(status) : null;
            DateTime previousTime = File.GetLastWriteTimeUtc(status);
            try
            {
                window.BeginSessionTracking();
                Require(window.SessionMessage.StartsWith("正在启动"), "session-footer-starting");
                File.WriteAllText(status, "{\"phase\":\"active\",\"targetCount\":1,\"filtering\":false}");
                window.UpdateStatus();
                Require(window.SessionMessage == "游戏正在运行，可关闭配置窗口" && !Find<Button>("launch-game").IsEnabled,
                    "session-footer-switches-to-running");
                File.WriteAllText(status, "{\"phase\":\"game-exited\",\"filtering\":false}");
                window.EndSessionTracking(); window.UpdateStatus();
                Require(Find<Button>("launch-game").IsEnabled, "session-exit-enables-launch");
            }
            finally
            {
                window.EndSessionTracking();
                if (previous is null) File.Delete(status);
                else { File.WriteAllBytes(status, previous); File.SetLastWriteTimeUtc(status, previousTime); }
            }
        }
        async Task CheckTouchSelectionAsync()
        {
            var select = Find<Select>("directx");
            var options = (Dictionary<string, ISelectOption>)select.Tag!;
            select.SelectedOption = options["auto"];
            await Tap(select);
            Require(select.IsDropDownOpen, "touch-opens-dropdown");
            Control popup = ((TouchSelect)select).PopupContent ?? throw new InvalidOperationException("Dropdown popup is missing");
            AtomUI.Desktop.Controls.ListViewItem Item(string label) => popup.GetVisualDescendants()
                .OfType<AtomUI.Desktop.Controls.ListViewItem>().Single(x => x.DataContext is ISelectOption o && Equals(o.Header, label));
            await Tap(Item("DirectX 11"));
            Require(!select.IsDropDownOpen && window.Configuration.Touch.DirectX == "dx11",
                "touch-tap-commits-dropdown-option");
            await Tap(select);
            await Tap(Item("沿用原启动配置"), drag: true);
            Require(select.IsDropDownOpen && window.Configuration.Touch.DirectX == "dx11",
                "touch-drag-does-not-select-option");
            select.IsDropDownOpen = false;
            select.Focus();
            select.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            select.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            select.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Require(window.Configuration.Touch.DirectX == "dx12", "keyboard-dropdown-selection-preserved");

            await Tap(select, pointerType: PointerType.Mouse);
            await Tap(Item("DirectX 11"), pointerType: PointerType.Mouse);
            Require(window.Configuration.Touch.DirectX == "dx11" && !select.IsDropDownOpen,
                "mouse-dropdown-selection-preserved");
            select.SelectedOption = options["dx12"];

            var resource = Find<Select>("resources");
            var tiers = (Dictionary<string, ISelectOption>)resource.Tag!;
            Require(tiers.ContainsKey("sd") && tiers["keep"].IsEnabled &&
                !tiers["sd"].IsEnabled && tiers["sd"].Content!.ToString()!.Contains("（" + ResourceCatalog.Missing + "）"),
                "missing-resource-option-disabled-with-hint");
            var before = resource.SelectedOption;
            await Tap(resource);
            var resourcePopup = ((TouchSelect)resource).PopupContent!;
            var disabled = resourcePopup.GetVisualDescendants().OfType<AtomUI.Desktop.Controls.ListViewItem>()
                .Single(x => x.DataContext == tiers["sd"]);
            await Tap(disabled);
            Require(ReferenceEquals(resource.SelectedOption, before), "touch-cannot-select-missing-resource");
            resource.IsDropDownOpen = false;

            var fixture = Path.Combine(directory, "resource-fixture", "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fixture)!);
            File.WriteAllText(fixture, "fixture");
            string pack = Path.Combine(PlanBuilder.ClientDirectory(fixture), "Content", "HD", "pack.pak");
            Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
            File.WriteAllText(pack, "fixture");
            var installed = ResourceCatalog.Choices(fixture);
            Require(installed.Single(x => x.Value == "hd").IsEnabled &&
                !installed.Single(x => x.Value == "sd").IsEnabled && !installed.Single(x => x.Value == "uhd").IsEnabled,
                "resource-detection-separates-installed-tiers");
            Require(MemoryGraphicsCatalog.Summary == "内存注入和帧率解锁一样有一定风险，请酌情使用" &&
                MemoryGraphicsCatalog.ExperimentalWarning == "不保证所有选项内存注入均可用，请自行实验", "memory-injection-risk-hint");

            async Task Tap(Control control, bool drag = false, PointerType pointerType = PointerType.Touch)
            {
                var top = TopLevel.GetTopLevel(control)!;
                Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                using var pointer = new Avalonia.Input.Pointer(991, pointerType, true);
                ulong timestamp = (ulong)Environment.TickCount64;
                var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
                var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
                control.RaiseEvent(new PointerPressedEventArgs(control, pointer, top, point, timestamp, pressed, KeyModifiers.None, 1));
                if (drag)
                {
                    for (int i = 1; i <= 5; i++)
                        control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, control, pointer, top,
                            point + new Vector(0, i * 12), timestamp + (ulong)i * 20, pressed, KeyModifiers.None));
                    point += new Vector(0, 60);
                }
                control.RaiseEvent(new PointerReleasedEventArgs(control, pointer, top, point, timestamp + 120,
                    released, KeyModifiers.None, MouseButton.Left));
                await Task.Delay(250);
            }
        }
        async Task CheckAppearanceAsync()
        {
            var app = (LauncherApplication)Application.Current!;
            Require(AppearanceWindow.ResolveEffect("auto", false) == "acrylic" &&
                AppearanceWindow.ResolveEffect("auto", true) == "mica", "windows-version-material-defaults");
            if (OperatingSystem.IsWindows())
            {
                var effect = Find<Select>("windows-effect");
                var options = (Dictionary<string, ISelectOption>)effect.Tag!;
                Require(options.Count == 3 && options.ContainsKey("acrylic") && options.ContainsKey("mica") &&
                    options.ContainsKey("none") && effect.SelectedOption == options[window.SelectedEffect],
                    "windows-three-material-choices-and-default");
            }
            else Require(FindOptional<Select>("windows-effect") is null, "linux-hides-windows-material-options");
            foreach (string theme in new[] { "light", "dark" })
            {
                Choose("appearance-theme", theme);
                await window.AppearanceUpdate;
                await Step();
                bool dark = theme == "dark";
                Require(app.ActualThemeVariant == (dark ? ThemeVariant.Dark : ThemeVariant.Light) &&
                    app.GetThemeManager()!.CurrentTheme!.Appearance == (dark ? ThemeAppearance.Dark : ThemeAppearance.Light) &&
                    (UiStyle.Background.Color.R < 100) == dark &&
                    (UiStyle.Foreground.Color.R > 150) == dark, "theme-controls-and-palette:" + theme);
                CheckUnifiedSurface(window, theme);
                Capture("appearance-" + theme);
            }
            await app.ApplyAppearanceAsync(PlatformThemeVariant.Light);
            Require(app.ActualThemeVariant == ThemeVariant.Dark, "explicit-theme-ignores-system-change");
            Choose("appearance-theme", "system");
            await window.AppearanceUpdate;
            foreach (var system in new[] { PlatformThemeVariant.Light, PlatformThemeVariant.Dark })
            {
                await app.ApplyAppearanceAsync(system);
                Require(window.Configuration.Appearance.Theme == "system" &&
                    (app.ActualThemeVariant == ThemeVariant.Dark) == (system == PlatformThemeVariant.Dark) &&
                    (UiStyle.Background.Color.R < 100) == (system == PlatformThemeVariant.Dark),
                    "system-theme-change:" + system);
            }
            // Return to the real desktop preference after simulating its change events.
            await app.ApplyAppearanceAsync();
            if (OperatingSystem.IsWindows()) Choose("windows-effect", "none");
            var opacity = Find<Slider>("appearance-opacity");
            foreach (int value in new[] { 30, 100, 72 })
            {
                opacity.Value = value;
                await Step();
                byte expected = window.ActualTransparencyLevel == WindowTransparencyLevel.None
                    ? (byte)255 : (byte)Math.Round(value * 2.55);
                Require(window.Configuration.Appearance.BackgroundOpacity == value && window.FrameColor.A == expected &&
                    window.Opacity == 1, "background-opacity-and-clear-content:" + value);
                CheckUnifiedSurface(window, "opacity-" + value);
            }
            if (OperatingSystem.IsWindows())
            {
                Choose("windows-effect", "mica");
                await Step();
                Require(!opacity.IsEnabled && window.Configuration.Appearance.BackgroundOpacity == 72 &&
                    window.TransparencyLevelHint[0] == (AppearanceWindow.SupportsMica
                        ? WindowTransparencyLevel.Mica : WindowTransparencyLevel.AcrylicBlur),
                    "mica-disables-opacity-and-keeps-value");
                CheckUnifiedSurface(window, "mica");
                if (requireWindowsGpu && AppearanceWindow.SupportsMica)
                    Require(window.FrameColor.A == 0 &&
                        window.ActualTransparencyLevel == WindowTransparencyLevel.Mica &&
                        window.SurfaceDescription().Contains("当前显示：云母"), "windows-avalonia-mica-material");
                Capture("appearance-mica");
                Choose("windows-effect", "acrylic");
                await Step();
                Require(opacity.IsEnabled == !window.IsOpacityManagedBySystem && opacity.Value == 72 &&
                    window.TransparencyLevelHint.Contains(WindowTransparencyLevel.AcrylicBlur),
                    "acrylic-preserves-stored-opacity");
                CheckUnifiedSurface(window, "acrylic");
                if (requireWindowsGpu && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
                    Require(window.FrameColor.A == 0 && !opacity.IsEnabled &&
                        window.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur &&
                        window.SurfaceDescription().Contains("当前显示：亚克力"), "windows-avalonia-acrylic-material");
                Capture("appearance-acrylic");
                Choose("windows-effect", "none"); await Step();
                Require(opacity.IsEnabled && opacity.Value == 72 && window.Configuration.Appearance.BackgroundOpacity == 72,
                    "disabling-material-restores-background-opacity");
                Require(window.ActualTransparencyLevel != WindowTransparencyLevel.Mica &&
                    window.ActualTransparencyLevel != WindowTransparencyLevel.AcrylicBlur,
                    "windows-disabling-material-clears-backdrop");
            }
        }
        async Task CheckCaptionButtonsAsync()
        {
            CheckCaptionLayout("normal");
            var maximize = CaptionButton("PART_MaximizeButton");
            Require(maximize.Command?.CanExecute(maximize.CommandParameter) == true,
                "windows-maximize-command-available");
            maximize.Command!.Execute(maximize.CommandParameter);
            await Task.Delay(250);
            Require(window.WindowState == WindowState.Maximized, "windows-caption-maximizes");
            CheckCaptionLayout("maximized");
            maximize.Command.Execute(maximize.CommandParameter);
            await Task.Delay(250);
            Require(window.WindowState == WindowState.Normal, "windows-caption-restores");
            CheckCaptionLayout("restored");
        }
        Avalonia.Controls.Button CaptionButton(string name) =>
            (window.GetVisualAncestors().LastOrDefault() ?? window)
                .GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                .Single(b => b.Name == name && b.IsEffectivelyVisible);
        void CheckCaptionLayout(string stage)
        {
            window.UpdateLayout();
            var title = (window.GetVisualAncestors().LastOrDefault() ?? window)
                .GetVisualDescendants().OfType<WindowTitleBar>().Single();
            var rectangles = new[] { "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton" }
                .Select(name =>
                {
                    var button = CaptionButton(name);
                    var point = button.TranslatePoint(default, title) ?? throw new InvalidOperationException("Caption not attached");
                    return new Rect(point, button.Bounds.Size);
                }).ToArray();
            Require(rectangles.All(r => r.Width >= 20 && r.Height >= 20 && r.X >= 0 &&
                r.Right <= title.Bounds.Width + 1) &&
                rectangles[0].Right <= rectangles[1].Left && rectangles[1].Right <= rectangles[2].Left,
                "windows-caption-buttons-do-not-overlap:" + stage);
        }
        void CheckWindowsFonts()
        {
            Require(TextOptions.GetTextRenderingMode(window) == TextRenderingMode.Antialias,
                "windows-text-grayscale-antialias");
            foreach (int character in new[] { (int)'鸣', (int)'潮', (int)'启', (int)'动', (int)'器', (int)'A', (int)'0' })
            {
                Require(FontManager.Current.TryMatchCharacter(character, FontStyle.Normal, FontWeight.Normal,
                    FontStretch.Normal, new FontFamily(UiStyle.Fonts), null, out var matched),
                    "windows-ui-font-glyph:" + character);
                result.FontFamilies.Add(matched.GlyphTypeface.FamilyName);
            }
        }
        async Task CheckSessionErrorsAsync()
        {
            var invalid = ConfigStore.Clone(window.Configuration);
            invalid.GameExecutable = Path.Combine(directory, "不存在的游戏", "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
            invalid.Normal.Resources = "keep";
            string path = Path.Combine(directory, "error-profile.json");
            ConfigStore.Save(path, invalid);
            using var child = Process.Start(LauncherWindow.SessionStartInfo(Environment.ProcessPath!, path, "normal"))
                ?? throw new InvalidOperationException("Cannot start the diagnostic child.");
            try
            {
                Task<string> text = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                string error = await text;
                Require(child.ExitCode == 1 && error.Contains("鸣潮启动器：找不到游戏程序：") &&
                    error.Contains("不存在的游戏") && !error.Contains('\uFFFD'), "windows-session-error-pipe-preserves-chinese");
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
        void CheckUnifiedSurface(AppearanceWindow target, string stage)
        {
            Require(target.Background is ISolidColorBrush background && background.Color.A == 0 &&
                target.ContentFrameBackground is ISolidColorBrush content &&
                target.TitleBarFrameBackground is ISolidColorBrush title && content.Color == title.Color,
                "unified-window-frame:" + stage);
            if (OperatingSystem.IsLinux())
            {
                var visualRoot = target.GetVisualAncestors().LastOrDefault() ?? target;
                var frame = visualRoot.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                    .SingleOrDefault(control => control.Name == "PART_WindowFrame");
                Require(frame?.Background is ISolidColorBrush fill && fill.Color.A == 0,
                    "linux-csd-underlay-is-transparent:" + stage);
            }
            if (!OperatingSystem.IsWindows()) return;
            var root = target.GetVisualAncestors().LastOrDefault() ?? target;
            var titlebar = root.GetVisualDescendants().OfType<WindowTitleBar>().Single();
            Require(titlebar.Background is ISolidColorBrush brush && brush.Color == target.FrameColor,
                "windows-title-bar-matches-content:" + stage);
        }
        void Require(bool condition, string behavior)
        {
            if (!condition) throw new InvalidOperationException("UI contract failed: " + behavior);
            result.Checks.Add(behavior);
        }
        void Capture(string name, Control? visual = null)
        {
            window.UpdateLayout();
            var content = visual ?? window;
            using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(content.Bounds.Width),
                (int)Math.Ceiling(content.Bounds.Height)), new Vector(96, 96));
            image.Render(content);
            image.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
        }
    }
}

// Sample the live window's drawing context, not an offscreen bitmap that would
// use software rendering even when the window itself is GPU accelerated.
internal sealed class RenderBackendProbe : Control
{
    internal TaskCompletionSource<string> Backend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override void Render(DrawingContext context) =>
        context.Custom(new ProbeOperation(Backend));

    private sealed class ProbeOperation(TaskCompletionSource<string> result) : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, 1, 1);
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            result.TrySetResult(lease is null ? "Unavailable" : lease.GrContext is null ? "Software" : "Gpu");
        }
    }
}

internal sealed class UiSmokeResult
{
    public bool Passed { get; set; }
    public string Backend { get; set; } = "";
    public string Renderer { get; set; } = "";
    public List<string> FontFamilies { get; set; } = [];
    public List<string> Checks { get; set; } = [];
    public string? Error { get; set; }
    public int MemoryTrimAttempts { get; set; }
    public long WorkingSetBefore { get; set; }
    public long WorkingSetAfter { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UiSmokeResult))]
internal partial class UiSmokeJson : JsonSerializerContext;
