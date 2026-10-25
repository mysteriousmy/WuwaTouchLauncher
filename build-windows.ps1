# Native Windows development build; use build-windows-cross.sh for the single-file static release.
param([string]$Output = (Join-Path $PSScriptRoot "dist\win-native-dev"))
$ErrorActionPreference = "Stop"
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output, (Join-Path $PSScriptRoot "artifacts") | Out-Null

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "请安装 .NET 10 SDK、Visual Studio C++ 桌面开发工具及 Windows SDK。"
    }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw "未找到 Visual C++ x64 构建工具。" }
    $vcvars = Join-Path $installation "VC\Auxiliary\Build\vcvars64.bat"
    # Import only the environment produced by the trusted Visual Studio setup script.
    & $env:ComSpec /d /s /c "`"$vcvars`" >nul && set" | ForEach-Object {
        $at = $_.IndexOf('=')
        if ($at -gt 0) {
            [Environment]::SetEnvironmentVariable($_.Substring(0, $at), $_.Substring($at + 1), "Process")
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "无法初始化 Visual C++ 构建环境。" }
}

$object = Join-Path $PSScriptRoot "artifacts\touch-bridge-win.obj"
& cl.exe /nologo /std:c11 /O2 /Oi- /GS- /W4 /WX /c (Join-Path $PSScriptRoot "native\touch-bridge.c") "/Fo$object"
if ($LASTEXITCODE -ne 0) { throw "原生辅助程序编译失败。" }
& link.exe /nologo /entry:mainCRTStartup /subsystem:windows /nodefaultlib /Brepro "/out:$Output\touch-bridge.exe" $object kernel32.lib user32.lib shell32.lib ole32.lib bcrypt.lib
if ($LASTEXITCODE -ne 0) { throw "原生辅助程序链接失败。" }
Copy-Item (Join-Path $Output "touch-bridge.exe") (Join-Path $PSScriptRoot "assets\touch-bridge.exe") -Force
$devSource = Join-Path $PSScriptRoot "artifacts\windows-native-development-source"
New-Item -ItemType Directory -Force (Join-Path $devSource "src") | Out-Null
Copy-Item (Join-Path $PSScriptRoot "src\*.cs"), (Join-Path $PSScriptRoot "src\*.csproj") (Join-Path $devSource "src") -Force
Copy-Item (Join-Path $PSScriptRoot "assets") $devSource -Recurse -Force
$devProject = Join-Path $devSource "src\WuwaTouchLauncher.csproj"
[IO.File]::WriteAllText((Join-Path $devSource "NuGet.Config"), '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
# The native development profile uses official dynamic graphics packages. The
# static release's patched backends remain isolated in the release publisher.
[IO.File]::WriteAllText($devProject, [IO.File]::ReadAllText($devProject).Replace('[12.1.4.1]', '[12.1.4]'))
& dotnet publish $devProject -c Release -r win-x64 --self-contained -p:PublishAot=true -p:StaticGraphics=false -o $Output
if ($LASTEXITCODE -ne 0) { throw "NativeAOT 发布失败。" }

Copy-Item (Join-Path $PSScriptRoot "README.md"), (Join-Path $PSScriptRoot "LICENSE"), (Join-Path $PSScriptRoot "config.example.json") $Output
Copy-Item (Join-Path $PSScriptRoot "licenses") $Output -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $Output "docs") | Out-Null
Get-ChildItem (Join-Path $PSScriptRoot "docs") -File | Where-Object { $_.Name -ne "LOCAL-HISTORY.md" } | Copy-Item -Destination (Join-Path $Output "docs")
$releaseDir = Join-Path $PSScriptRoot "releases"
New-Item -ItemType Directory -Force $releaseDir | Out-Null
[xml]$projectXml = Get-Content (Join-Path $PSScriptRoot "src\WuwaTouchLauncher.csproj")
$version = $projectXml.SelectSingleNode("//Version").InnerText
$zip = Join-Path $releaseDir "wuwa-launcher-$version-win-x64-development.zip"
$files = Get-ChildItem $Output | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object { $_.FullName }
Compress-Archive -Path $files -DestinationPath $zip -Force
Write-Host "NativeAOT 开发包（含图形 DLL）：$zip"
