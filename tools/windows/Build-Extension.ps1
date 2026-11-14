<#
.SYNOPSIS
    Station Countdown の拡張機能をビルドし、-Register を付けると Command Palette 用に登録（開発用の配置）する。

.DESCRIPTION
    Visual Studio の「配置」の代わりに使う。VS Code のタスク（.vscode/tasks.json）から呼ばれる。
    Visual Studio と Windows SDK は不要。必要なのは .NET 10 SDK と、登録する場合は Windows の開発者モード。

    登録は Add-AppxPackage -Register で行う。パッケージはビルド出力のフォルダーをそのまま指すので、
    拡張機能が動いているとファイルがロックされる。そのため、ビルドの前に拡張機能のプロセスを止める。

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

# WSL や VS Code のターミナルに日本語が化けずに出るよう、出力を UTF-8 にする。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$PackageName = 'Laplusdestiny.StationCountdown'
$ProcessName = 'StationCountdown.Extension'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Project = Join-Path $RepoRoot 'src\StationCountdown.Extension\StationCountdown.Extension.csproj'

function Find-Dotnet10 {
    $candidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (& $candidate --list-sdks 2>$null | Select-String -Pattern '^10\.' -Quiet) {
            return $candidate
        }
    }
    throw '.NET 10 SDK が見つかりません。https://dotnet.microsoft.com/download/dotnet/10.0 から入れるか、dotnet-install.ps1 -Channel 10.0 で %LOCALAPPDATA%\Microsoft\dotnet に入れてください。'
}

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
$running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "動いている拡張機能のプロセスを止めます（PID: $($running.Id -join ', ')）"
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

& $dotnet build $Project -c $Configuration "-p:Platform=$Platform" -nologo
if ($LASTEXITCODE -ne 0) {
    throw "ビルドに失敗しました（終了コード $LASTEXITCODE）。"
}

$rid = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$manifest = Join-Path $RepoRoot "src\StationCountdown.Extension\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid\AppxManifest.xml"
if (-not (Test-Path $manifest)) {
    throw "AppxManifest.xml が見つかりません: $manifest"
}
Write-Host "ビルドできました: $manifest"

if (-not $Register) {
    return
}

Add-AppxPackage -Register $manifest -ForceApplicationShutdown
$package = Get-AppxPackage -Name $PackageName
if (-not $package) {
    throw "登録したはずのパッケージ $PackageName が見つかりません。"
}
Write-Host "登録できました: $($package.PackageFullName)"
Write-Host "  場所: $($package.InstallLocation)"
Write-Host 'Command Palette で「Reload」（拡張機能の再読み込み）を実行すると、新しいビルドが読み込まれます。'
