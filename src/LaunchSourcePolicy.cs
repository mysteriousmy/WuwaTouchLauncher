internal static class LaunchSourcePolicy
{
    public static LutrisGame[] MatchingLutrisGames(ProductConfig config, DiscoveryResult discovery) =>
        discovery.LutrisGames.Where(x => config.GameExecutable.Length == 0 ||
            x.Executable == config.GameExecutable).ToArray();

    public static bool UsesLutris(ProductConfig config, DiscoveryResult discovery) =>
        OperatingSystem.IsLinux() && config.Linux.Integration != "manual" &&
        (config.Linux.Integration == "lutris" || config.Linux.LutrisConfig.Length > 0 ||
            MatchingLutrisGames(config, discovery).Length > 0);

    public static bool SelectSingleWindowsInstallation(ProductConfig config, DiscoveryResult discovery)
    {
        // Re-scanning never replaces a valid selection, including a manually chosen installation.
        if (WindowsDiscovery.FindGameExecutable(config.GameExecutable) is not null ||
            discovery.InstalledGames.Count != 1) return false;
        config.GameExecutable = discovery.InstalledGames[0].Executable;
        return true;
    }
}
