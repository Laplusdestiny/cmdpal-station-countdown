<#
.SYNOPSIS
    Station Countdown の拡張機能をビルドし、-Register を付けると Command Palette 用に登録（開発用の配置）する。

.DESCRIPTION
    Visual Studio の「配置」の代わりに使う。VS Code のタスク（.vscode/tasks.json）から呼ばれる。
    Visual Studio と Windows SDK は不要。必要なのは .NET 10 SDK と、登録する場合は Windows の開発者モード。

    登録は Add-AppxPackage -Register で行う。パッケージはビルド出力のフォルダーをそのまま指すので、
    拡張機能が動いているとファイルがロックされる。そのため、ビルドの前に拡張機能のプロセスを止める。
    配布用の MSIX（リリース）が入っている場合など、別の場所のパッケージが登録されていれば、先に外す
    （同じ名前のパッケージは 1 つしか入らず、そのままでは登録が反映されないため）。
    設定と時刻表は %LOCALAPPDATA%\StationCountdown にあり、パッケージを外しても消えない。

.EXAMPLE
    .\tools\windows\Build-Extension.ps1 -Register
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [switch] $Register
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

function Test-DeveloperMode {
    $value = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
        -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
    return $value -and $value.AllowDevelopmentWithoutDevLicense -eq 1
}

if ($Register -and -not (Test-DeveloperMode)) {
    throw '開発者モードがオフのため、登録できません。設定 → システム → 開発者向け で「開発者モード」をオンにしてから、もう一度実行してください（PowerShell で start ms-settings:developers を実行すると開きます）。'
}

$dotnet = Find-Dotnet10
Write-Host "dotnet: $dotnet"

# 動いている拡張機能が、ビルド出力（= 登録済みパッケージの中身）をロックしているので止める。
Stop-Extension

& $dotnet build $ExtensionProject -c $Configuration "-p:Platform=$Platform" -nologo
if ($LASTEXITCODE -ne 0) {
    throw "ビルドに失敗しました（終了コード $LASTEXITCODE）。"
}

$rid = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$layout = Join-Path $RepoRoot "src\StationCountdown.Extension\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid"
$manifest = Join-Path $layout 'AppxManifest.xml'
if (-not (Test-Path $manifest)) {
    throw "AppxManifest.xml が見つかりません: $manifest"
}
Write-Host "ビルドできました: $manifest"

if (-not $Register) {
    return
}

$existing = Get-AppxPackage -Name $PackageName
if ($existing -and ($existing.InstallLocation.TrimEnd('\') -ne $layout.TrimEnd('\'))) {
    Write-Host "別の場所のパッケージを外します: $($existing.PackageFullName)（$($existing.InstallLocation)）"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppxPackage -Register $manifest -ForceApplicationShutdown
$package = Get-AppxPackage -Name $PackageName
if (-not $package -or $package.InstallLocation.TrimEnd('\') -ne $layout.TrimEnd('\')) {
    throw "登録が反映されていません（登録されている場所: $($package.InstallLocation)）。"
}
Write-Host "登録できました: $($package.PackageFullName)"
Write-Host "  場所: $($package.InstallLocation)"
Write-Host 'Command Palette で「Reload」（拡張機能の再読み込み）を実行すると、新しいビルドが読み込まれ、Dock のバンドも表示し直されます。'
