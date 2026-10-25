using System.Globalization;

internal sealed class LaunchPlan
{
    public string Mode { get; set; } = "";
    public string Source { get; set; } = "";
    public string GameExecutable { get; set; } = "";
    public string[] GameArguments { get; set; } = [];
    public string[] Command { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public IniOverride[] IniOverrides { get; set; } = [];
    public string Database { get; set; } = "";
    public int? ImageQuality { get; set; }
    public Dictionary<string, string> DatabaseOverrides { get; set; } = [];
    public int FpsUnlockTarget { get; set; }
    public bool MemoryGraphicsEnabled { get; set; }
    public Dictionary<string, decimal> MemoryGraphicsOverrides { get; set; } = [];
    public string[] MemoryGraphicsUnavailable { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

internal static class PlanBuilder
{
    public static LaunchPlan Build(ProductConfig config, DiscoveryResult? discovered = null,
        DisplayResolution? desktopResolution = null)
    {
        SettingsCatalog.Validate(config);
        bool touch = config.Mode == "touch";
        DisplayResolution? adaptiveResolution = touch && config.TouchOnly.AdaptiveFullscreen
            ? desktopResolution ?? DesktopDisplay.ReadPrimary() : null;
        adaptiveResolution?.Validate();
        var profile = config.Selected;
        var plan = new LaunchPlan { Mode = config.Mode, Source = "Windows 原生" };
        var linux = config.Linux;
        LutrisGame? lutris = null;
        if (OperatingSystem.IsLinux())
        {
            discovered ??= Discovery.Scan();
            if (linux.Integration != "manual")
            {
                if (!string.IsNullOrWhiteSpace(linux.LutrisConfig))
                    lutris = Discovery.ReadLutris(AppPaths.Expand(linux.LutrisConfig));
                else
                {
                    var matches = LaunchSourcePolicy.MatchingLutrisGames(config, discovered);
                    if (matches.Length > 1) throw new ArgumentException("检测到多个鸣潮 Lutris 条目，请在启动设置中选择一个。");
                    lutris = matches.SingleOrDefault();
                }
                if (lutris is null && linux.Integration == "lutris")
                    throw new ArgumentException("未找到对应的 Lutris 配置，请选择条目或改用手动运行器。");
            }
            plan.Source = lutris is null ? "手动 Proton / Wine" : $"读取 Lutris：{lutris.Name}";
        }
        string game = lutris?.Executable ?? config.GameExecutable;
        if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(game))
        {
            discovered ??= Discovery.Scan();
            if (discovered.InstalledGames.Count == 1) game = discovered.InstalledGames[0].Executable;
            else throw new ArgumentException(discovered.InstalledGames.Count > 1
                ? "检测到多个鸣潮安装，请在启动器中选择要运行的版本。"
                : "未检测到已安装的鸣潮，请选择 Client-Win64-Shipping.exe。");
        }
        if (string.IsNullOrWhiteSpace(game)) throw new ArgumentException("请先选择游戏程序，或使用检测到的 Lutris 条目。");
        plan.GameExecutable = ResolveGamePath(AppPaths.Expand(game));
        string client = ClientDirectory(plan.GameExecutable);
        var gameArguments = new List<string>();
        if (lutris is not null)
        {
            gameArguments.AddRange(CommandLine.Split(lutris.Arguments).Where(x => !IsManagedArgument(x)));
            var original = CommandLine.Split(lutris.Arguments);
            if (profile.Resources == "keep")
                gameArguments.AddRange(original.Where(x => x.StartsWith("-krqlv=", StringComparison.OrdinalIgnoreCase)));
            if (profile.DirectX == "auto")
                gameArguments.AddRange(original.Where(x => x.ToLowerInvariant() is "-dx11" or "-dx12" or "-d3d11" or "-d3d12"));
        }
        if (profile.DirectX != "auto") gameArguments.Add("-" + profile.DirectX);
        if (profile.Resources != "keep") gameArguments.Add("-krqlv=" + profile.Resources);
        if (profile.SkipSplash) gameArguments.Add("-SkipSplash");
        string[] extra = lutris is null ? CommandLine.Split(profile.CustomArguments) : [];
        if (extra.Any(IsManagedArgument))
            throw new ArgumentException("自定义参数中包含已由界面管理的模式、DX、画质或跳过动画参数，请改用对应选项。");
        gameArguments.AddRange(extra);
        if (touch)
        {
            if (config.TouchOnly.MemoryMode == "memory" ||
                config.TouchOnly.MemoryMode == "mixed" && config.TouchOnly.MemoryOptions.Any(x => x.Value))
            {
                plan.MemoryGraphicsEnabled = true;
                plan.MemoryGraphicsOverrides = MemoryGraphicsCatalog.Build(config.TouchOnly);
                plan.MemoryGraphicsUnavailable = MemoryGraphicsCatalog.Unavailable(config.TouchOnly);
                plan.Notes.Add("内存模式在配置预置基础上补充运行时维护；混合模式只维护勾选项，沿用项不注入。" + MemoryGraphicsCatalog.ExperimentalWarning + "。版本或内存布局不匹配时跳过注入，游戏仍正常启动。");
                if (plan.MemoryGraphicsUnavailable.Length > 0)
                    plan.Notes.Add("这些内存请求尚无可靠目标，跳过内存写入并保留配置预置：" + string.Join("、", plan.MemoryGraphicsUnavailable));
                if (new[] { "native-fg", "fsr-fg", "xess-fg" }.Any(key => config.TouchOnly.UsesMemory(key) &&
                    config.TouchOnly.Values.GetValueOrDefault(key, "keep") is not ("keep" or "0")))
                    plan.Notes.Add("DLSS／FSR／XeSS 插帧各自维护：登录、加载和暂停菜单中关闭，角色加载且场景计时恢复后重新开启。显卡支持、渲染链路及多帧生成倍数不保证生效。");
            }
            if (config.TouchOnly.Values.GetValueOrDefault("unlock-fps", "keep") is var unlock && unlock != "keep")
            {
                plan.FpsUnlockTarget = int.Parse(unlock, CultureInfo.InvariantCulture);
                plan.Notes.Add($"实验性帧率插件：请求 {unlock} 帧，覆盖帧率上限并关闭垂直同步；选用内存注入后补充运行时维护。请求发送成功不代表实际帧率已解除限制。");
            }
            gameArguments.AddRange(["-CloudGame", "-CloudGamePlatform",
                "-ini:Engine:[MobileSlateUI]:bTouchFallbackToMouse=False",
                "-ini:Input:[/Script/Engine.InputSettings]:bUseMouseForTouch=False"]);
            if (adaptiveResolution is { } screen)
            {
                gameArguments.RemoveAll(IsDisplayArgument);
                gameArguments.AddRange(["-fullscreen", $"-ResX={screen.Width}", $"-ResY={screen.Height}"]);
                plan.Notes.Add($"自适应全屏：主屏幕 {screen.Width}×{screen.Height}，使用独占全屏；优先于手动显示模式和输出分辨率，退出后恢复游戏原配置。");
            }
            plan.IniOverrides = TouchOverrides(config.TouchOnly, client, adaptiveResolution);
            foreach (var option in SettingsCatalog.TouchOptions.Where(x => x.DatabaseKey is not null))
            {
                string value = config.TouchOnly.Values.GetValueOrDefault(option.Key, "keep");
                if (value != "keep") plan.DatabaseOverrides[option.DatabaseKey!] = value;
            }
            if (config.TouchOnly.Values.GetValueOrDefault("ray-tracing") == "0")
                plan.DatabaseOverrides["RayTracing"] = "0";
            if (config.TouchOnly.Values.GetValueOrDefault("anisotropy", "keep") is var aniso && aniso != "keep")
                plan.DatabaseOverrides["AnisoLevel"] = aniso switch { "1" => "0", "2" => "1", "4" => "2", "8" => "3", _ => "4" };
            if (plan.DatabaseOverrides.Count > 0)
                plan.Notes.Add("逐项图形配置使用自定义模式。未指定的细项沿用游戏原值；画质预设只提供基础等级，不会重置全部细项。");
            if (config.TouchOnly.Values.Any(x => x.Key.StartsWith("ray-", StringComparison.Ordinal) && x.Value != "keep"))
            {
                plan.Notes.Add("光追需要 DX12、硬件和场景支持；云触控模式尚未证实实际硬件光追渲染。");
                if (profile.DirectX == "dx11" && config.TouchOnly.Values.Any(x =>
                    x.Key.StartsWith("ray-", StringComparison.Ordinal) && x.Value is not ("keep" or "0")))
                    throw new ArgumentException("光追配置需要 DX12，请更改 DirectX 模式或将光追项设为沿用。");
            }
        }
        plan.GameArguments = gameArguments.ToArray();
        plan.Database = Path.Combine(client, "Saved", "LocalStorage", "LocalStorage.db");
        plan.ImageQuality = profile.Quality == "keep" ? null : int.Parse(profile.Quality, CultureInfo.InvariantCulture);
        if (touch && profile.Quality == "4")
            plan.Notes.Add("触控云模式此前实测会将最高预设回退至高；不保证最高档生效。");
        if (touch && plan.FpsUnlockTarget == 0 && config.TouchOnly.Values.GetValueOrDefault("fps", "keep") is not ("keep" or "30" or "60"))
            plan.Notes.Add("当前游戏云模式可能仍限制为 60 原生帧，此选项仅设置请求上限。");
        var hotkey = Hotkey.Parse(config.TouchOnly.Hotkey);
        var command = new List<string>();
        if (OperatingSystem.IsLinux())
        {
            discovered ??= Discovery.Scan();
            plan.Environment = lutris is null ? [] : new(lutris.Environment);
            if (lutris is null)
                foreach (var pair in linux.Environment) plan.Environment[pair.Key] = pair.Value;
            string runner = AppPaths.Expand(linux.Runner);
            string prefix = AppPaths.Expand(linux.Prefix);
            if (lutris is not null)
            {
                prefix = lutris.Prefix;
                string? detected = lutris.RunnerPath.Length > 0 ? lutris.RunnerPath :
                    discovered.Runners.FirstOrDefault(x => x.Name == lutris.RunnerVersion)?.Path;
                runner = detected ?? "";
            }
            if (runner.Length == 0)
            {
                if (lutris is not null)
                    throw new ArgumentException("找不到 Lutris 条目指定的运行器，请先在 Lutris 中安装或选择有效运行器。");
                if (discovered.Runners.Count == 1) runner = discovered.Runners[0].Path;
                else throw new ArgumentException("请选择 Proton / Wine 运行器。");
            }
            if (prefix.Length == 0) throw new ArgumentException("请指定 Wine / Proton 前缀目录，避免误用其他游戏环境。");
            if (!Directory.Exists(prefix)) throw new ArgumentException($"前缀目录不存在：{prefix}");
            string runnerKind = lutris is null ? linux.RunnerKind : "auto";
            bool proton = runnerKind == "proton" ||
                (runnerKind == "auto" && (File.Exists(Path.Combine(runner, "proton")) || Path.GetFileName(runner) == "proton"));
            string wrapper = lutris is null ? linux.Wrapper : lutris.Wrapper;
            command.AddRange(CommandLine.Split(wrapper));
            if (proton)
            {
                string protonRoot = File.Exists(runner) ? Path.GetDirectoryName(runner)! : runner;
                string umu = lutris is null && linux.Umu.Length > 0 ? AppPaths.Expand(linux.Umu) : discovered.Umu;
                if (umu.Length > 0)
                {
                    if (Discovery.FindOnPath(umu) is null) throw new ArgumentException($"找不到 UMU：{umu}");
                    command.Add(umu);
                    plan.Environment["PROTONPATH"] = protonRoot;
                    plan.Environment["WINEPREFIX"] = prefix;
                    plan.Environment.TryAdd("GAMEID", "umu-default");
                    plan.Environment.TryAdd("PROTON_VERB", "runinprefix");
                }
                else
                {
                    command.Add(Path.Combine(protonRoot, "proton"));
                    command.Add("run");
                    // Proton's compatibility data directory contains pfx; UMU takes WINEPREFIX directly.
                    string compatibility = Path.GetFileName(prefix) == "pfx" ? Path.GetDirectoryName(prefix)! : prefix;
                    if (Directory.Exists(Path.Combine(prefix, "drive_c")) && Path.GetFileName(prefix) != "pfx")
                        throw new ArgumentException("此目录是 Wine 前缀；直接运行 Proton 需要含 pfx 的兼容数据目录。请安装/选择 UMU，或选择正确目录。");
                    plan.Environment["STEAM_COMPAT_DATA_PATH"] = compatibility;
                    plan.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = SteamRoot();
                    plan.Environment["WINEPREFIX"] = Path.Combine(compatibility, "pfx");
                }
                plan.Environment.TryAdd("PROTON_USE_XALIA", "0");
                if (!File.Exists(Path.Combine(protonRoot, "proton")))
                    throw new ArgumentException($"该目录不是 Proton：{protonRoot}");
            }
            else
            {
                string wine = Directory.Exists(runner) ? Path.Combine(runner, "bin", "wine") : runner;
                if (Discovery.FindOnPath(wine) is null) throw new ArgumentException($"找不到 Wine：{wine}");
                command.Add(wine);
                plan.Environment["WINEPREFIX"] = prefix;
            }
            plan.Environment["STEAM_COMPAT_INSTALL_PATH"] = Path.GetDirectoryName(plan.GameExecutable)!;
        }
        command.Add(AppPaths.Bridge);
        if (plan.MemoryGraphicsEnabled)
        {
            command.AddRange(["--memory-graphics", "1"]);
            foreach (var (name, value) in plan.MemoryGraphicsOverrides)
                command.AddRange(["--graphics-set", name + "=" + value.ToString(CultureInfo.InvariantCulture)]);
            foreach (string key in plan.MemoryGraphicsUnavailable)
                command.AddRange(["--graphics-unavailable", key]);
        }
        if (plan.FpsUnlockTarget != 0)
            command.AddRange(["--fps-target", plan.FpsUnlockTarget.ToString(CultureInfo.InvariantCulture),
                "--fps-plugin", NativePath(EmbeddedBridge.FpsPluginPath)]);
        command.AddRange(["--status", NativePath(Path.Combine(AppPaths.StateDirectory, "status.json")),
            "--stop", NativePath(Path.Combine(AppPaths.StateDirectory, "stop")),
            "--allow-filter", touch ? "1" : "0",
            "--filter", touch && config.TouchOnly.MouseFilter ? "1" : "0",
            "--hotkey-enabled", touch && config.TouchOnly.HotkeyEnabled ? "1" : "0",
            "--hotkey-modifiers", hotkey.Modifiers.ToString(CultureInfo.InvariantCulture),
            "--hotkey-key", hotkey.Key.ToString(CultureInfo.InvariantCulture),
            "--game", NativePath(plan.GameExecutable), "--"]);
        command.AddRange(plan.GameArguments);
        plan.Command = command.ToArray();
        return plan;
    }

    public static void ValidateFiles(LaunchPlan plan)
    {
        if (!Path.IsPathFullyQualified(plan.GameExecutable) || !File.Exists(plan.GameExecutable))
            throw new ArgumentException($"找不到游戏程序：{plan.GameExecutable}");
        if (!File.Exists(AppPaths.Bridge)) throw new ArgumentException($"缺少触控辅助程序：{AppPaths.Bridge}；请完整解压发布包。");
        if (plan.Command.Length == 0 || Discovery.FindOnPath(plan.Command[0]) is null)
            throw new ArgumentException($"找不到启动命令：{plan.Command.FirstOrDefault()}");
        if ((plan.ImageQuality is not null || plan.DatabaseOverrides.Count > 0 || plan.FpsUnlockTarget != 0) && !File.Exists(plan.Database))
            throw new ArgumentException("游戏尚未生成画质数据库，请先选择“沿用游戏设置”启动一次。");
        string? resources = plan.GameArguments.FirstOrDefault(x => x.StartsWith("-krqlv=", StringComparison.OrdinalIgnoreCase));
        if (resources is not null)
        {
            string tier = resources["-krqlv=".Length..].ToUpperInvariant();
            string directory = Path.Combine(ClientDirectory(plan.GameExecutable), "Content", tier);
            if (!ResourceCatalog.IsInstalled(plan.GameExecutable, tier))
                throw new ArgumentException($"尚未安装 {tier} 资源：{directory}。请先在官方启动器完成下载，或选择已安装画质。");
        }
    }

    public static string ResolveGamePath(string path)
    {
        if (WindowsDiscovery.FindGameExecutable(path) is { } resolved) return resolved;
        path = Path.GetFullPath(path);
        string root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        foreach (string candidate in new[]
        {
            Path.Combine(root, "Client/Binaries/Win64/Client-Win64-Shipping.exe"),
            Path.Combine(root, "Binaries/Win64/Client-Win64-Shipping.exe")
        }) if (File.Exists(candidate)) return candidate;
        return path;
    }

    internal static string ClientDirectory(string executable) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
    private static string NativePath(string path) =>
        OperatingSystem.IsWindows() ? Path.GetFullPath(path) : "Z:" + Path.GetFullPath(path).Replace('/', '\\');
    private static string SteamRoot() =>
        new[] { "~/.local/share/Steam", "~/.steam/steam", "~/.var/app/com.valvesoftware.Steam/data/Steam" }
            .Select(AppPaths.Expand).FirstOrDefault(Directory.Exists) ?? "";
    internal static bool IsManagedArgument(string value) =>
        value.Equals("-dx11", StringComparison.OrdinalIgnoreCase) || value.Equals("-dx12", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("-d3d11", StringComparison.OrdinalIgnoreCase) || value.Equals("-d3d12", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("-SkipSplash", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("-krqlv=", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("-CloudGame", StringComparison.OrdinalIgnoreCase) || value.StartsWith("-CloudGamePlatform", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("-ini:Engine:[MobileSlateUI]", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("-ini:Input:[/Script/Engine.InputSettings]:bUseMouseForTouch", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisplayArgument(string value) => value.ToLowerInvariant() is
        "-windowed" or "-fullscreen" or "-windowedfullscreen" or "-borderless" ||
        value.StartsWith("-ResX=", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("-ResY=", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("-Res=", StringComparison.OrdinalIgnoreCase);

    private static IniOverride[] TouchOverrides(TouchSettings settings, string client,
        DisplayResolution? adaptiveResolution)
    {
        var engine = new Dictionary<string, string>();
        var frame = new Dictionary<string, string>();
        var user = new Dictionary<string, string>();
        var scalability = new Dictionary<string, string>();
        foreach (var pair in settings.Values.Where(x => x.Value != "keep"))
        {
            string v = pair.Value;
            switch (pair.Key)
            {
                case "fps": engine["t.MaxFPS"] = v; user["FrameRateLimit"] = v + ".000000"; break;
                case "vsync": engine["r.VSync"] = v; engine["rhi.SyncInterval"] = v; user["bUseVSync"] = v == "1" ? "True" : "False"; break;
                case "smooth-fps": frame["bSmoothFrameRate"] = v == "1" ? "True" : "False"; break;
                case "fixed-fps": frame["bUseFixedFrameRate"] = v == "1" ? "True" : "False"; break;
                case "anisotropy": engine["r.MaxAnisotropy"] = v; break;
                case "lighting": engine["r.Kuro.GlobalLightQuality"] = v; break;
                case "dlss": engine["r.NGX.DLSS.Enable"] = v; break;
                case "dlss-quality":
                    engine["r.NGX.DLSS.Quality.Auto"] = v == "99" ? "1" : "0";
                    if (v != "99") engine["r.NGX.DLSS.Quality"] = v;
                    break;
                case "native-fg": engine["r.Streamline.DLSSG.Enable"] = v == "0" ? "0" : "1"; break;
                case "ray-tracing": engine["r.RayTracing.Enable"] = v; break;
                case "ray-quality":
                    engine["sg.RayTracingQuality"] = v; scalability["sg.RayTracingQuality"] = v;
                    engine["r.RayTracing.Enable"] = v == "0" ? "0" : "1"; break;
                case "ray-shadows": engine["r.RayTracing.Shadows"] = v; break;
                case "display-mode": user["FullscreenMode"] = v; user["LastConfirmedFullscreenMode"] = v; break;
                case "resolution":
                    string[] size = v.Split('x');
                    user["ResolutionSizeX"] = size[0]; user["ResolutionSizeY"] = size[1];
                    user["LastUserConfirmedResolutionSizeX"] = size[0]; user["LastUserConfirmedResolutionSizeY"] = size[1];
                    break;
                case "render-scale": engine["r.ScreenPercentage"] = v; scalability["sg.ResolutionQuality"] = v; break;
                case "texture": engine["sg.TextureQuality"] = v; scalability["sg.TextureQuality"] = v; break;
            }
        }
        if (adaptiveResolution is { } screen)
        {
            engine["r.FullScreenMode"] = "0";
            user["FullscreenMode"] = "0";
            user["LastConfirmedFullscreenMode"] = "0";
            user["PreferredFullscreenMode"] = "0";
            user["ResolutionSizeX"] = screen.Width.ToString(CultureInfo.InvariantCulture);
            user["ResolutionSizeY"] = screen.Height.ToString(CultureInfo.InvariantCulture);
            user["LastUserConfirmedResolutionSizeX"] = user["ResolutionSizeX"];
            user["LastUserConfirmedResolutionSizeY"] = user["ResolutionSizeY"];
        }
        if (settings.Values.GetValueOrDefault("unlock-fps", "keep") is var unlock && unlock != "keep")
        {
            engine["t.MaxFPS"] = unlock;
            engine["r.VSync"] = "0";
            engine["rhi.SyncInterval"] = "0";
            user["FrameRateLimit"] = unlock + ".000000";
            user["bUseVSync"] = "False";
        }
        string directory = Path.Combine(client, "Saved", "Config", "WindowsNoEditor");
        var result = new List<IniOverride>();
        void Add(string file, string section, Dictionary<string, string> values)
        {
            if (values.Count != 0) result.Add(new IniOverride { Path = Path.Combine(directory, file), Section = section, Values = values });
        }
        Add("Engine.ini", "SystemSettings", engine);
        Add("Engine.ini", "/Script/Engine.Engine", frame);
        Add("GameUserSettings.ini", "/Script/Engine.GameUserSettings", user);
        Add("GameUserSettings.ini", "ScalabilityGroups", scalability);
        return result.ToArray();
    }
}
