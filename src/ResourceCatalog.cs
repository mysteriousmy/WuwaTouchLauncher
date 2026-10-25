internal static class ResourceCatalog
{
    internal const string Missing = "未下载此资源，请使用官方安装对应资源";
    private static readonly SettingOption[] Tiers =
        [new("sd", "普通 SD"), new("hd", "高清 HD"), new("uhd", "极致 UHD")];

    internal static bool IsInstalled(string game, string tier)
    {
        if (!Tiers.Any(x => x.Value.Equals(tier, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(game)) return false;
        try
        {
            string directory = Path.Combine(PlanBuilder.ClientDirectory(PlanBuilder.ResolveGamePath(AppPaths.Expand(game))),
                "Content", tier.ToUpperInvariant());
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Any(x => Path.GetExtension(x).Equals(".pak", StringComparison.OrdinalIgnoreCase) && new FileInfo(x).Length > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal static string ResolveGame(ProductConfig config, DiscoveryResult discovery)
    {
        if (LaunchSourcePolicy.UsesLutris(config, discovery))
        {
            try
            {
                var games = config.Linux.LutrisConfig.Length > 0
                    ? new[] { Discovery.ReadLutris(AppPaths.Expand(config.Linux.LutrisConfig)) }
                    : LaunchSourcePolicy.MatchingLutrisGames(config, discovery);
                return games.Length == 1 ? games[0]?.Executable ?? "" : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
        }
        return config.GameExecutable;
    }

    internal static SettingOption[] Choices(string game) =>
        [new("keep", "沿用原启动配置"), .. Tiers.Select(x => IsInstalled(game, x.Value) ? x :
            x with { Label = $"{x.Label}（{Missing}）", IsEnabled = false })];
}
