param([string]$Binary = (Join-Path $PSScriptRoot "dist\win-native-dev\wuwa-touch-launcher.exe"))
$ErrorActionPreference = "Stop"
$Binary = [IO.Path]::GetFullPath($Binary)
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("wuwa-windows-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $temporary | Out-Null
$config = Join-Path $temporary "中文 profile.json"

function Invoke-Launcher([string[]]$CliArguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Binary)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $CliArguments) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(15000)) { $child.Kill(); throw "CLI 超时" }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($child.ExitCode -ne 0) { throw "CLI 失败：$errorText" }
        return $text
    } finally { $child.Dispose() }
}
function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
try {
    $game = Join-Path $temporary "游戏\Client\Binaries\Win64\Client-Win64-Shipping.exe"
    New-Item -ItemType Directory ([IO.Path]::GetDirectoryName($game)) -Force | Out-Null
    [IO.File]::WriteAllBytes($game, [byte[]](0, 1))
    Invoke-Launcher @("save", "--config", $config, "--game", $game, "--mode", "touch",
        "--resources", "keep", "--touch-setting", "fps=240", "--hotkey", "Ctrl+Shift+F9") | Out-Null
    $before = [IO.File]::ReadAllText($config)
    $touch = (Invoke-Launcher @("preview", "--config", $config)) | ConvertFrom-Json
    Assert-True ($touch.Mode -eq "touch") "saved_touch_profile: 模式未持久化"
    Assert-True ($touch.GameArguments -contains "-CloudGame") "saved_touch_profile: 缺少触控参数"
    Assert-True (@($touch.Environment.PSObject.Properties).Count -eq 0) "windows_platform: 不应注入 Linux 环境"
    Assert-True ($touch.Command[0].EndsWith("touch-bridge.exe")) "windows_platform: 未直接使用 Win32 辅助程序"
    Assert-True ($touch.IniOverrides.Count -gt 0) "touch_profile: 未请求临时画质覆盖"
    $normal = (Invoke-Launcher @("preview", "--config", $config, "--mode", "normal")) | ConvertFrom-Json
    Assert-True ($normal.IniOverrides.Count -eq 0) "normal_isolation: 不应应用触控覆盖"
    Assert-True (-not ($normal.GameArguments -contains "-CloudGame")) "normal_isolation: 不应附带触控模式"
    Assert-True ([IO.File]::ReadAllText($config) -eq $before) "ephemeral_override: 覆盖不应保存到配置"
    $links = Join-Path $temporary "shortcuts"
    Invoke-Launcher @("shortcuts", "--config", $config, "--shortcut-dir", $links) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    foreach ($pair in @(@("normal", "鸣潮 · 普通模式.lnk"), @("touch", "鸣潮 · 触控模式.lnk"))) {
        $link = $shell.CreateShortcut((Join-Path $links $pair[1]))
        Assert-True ($link.TargetPath -eq $Binary) "native_shortcut: 目标错误"
        Assert-True ($link.Arguments.Contains('"launch"')) "native_shortcut: 必须无界面启动"
        Assert-True ($link.Arguments.Contains('"' + $pair[0] + '"')) "native_shortcut: 模式错误"
        Assert-True ($link.Arguments.Contains($config)) "native_shortcut: 未关联保存配置"
    }
    Write-Host "Passed: 4, Failed: 0, Skipped: 0"
    Write-Host "saved_touch_profile; windows_platform; normal_isolation_and_ephemeral_override; native_shortcuts"
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
