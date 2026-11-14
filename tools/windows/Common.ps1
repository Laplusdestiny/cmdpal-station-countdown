# tools/windows の各スクリプトで使う共通の関数。. "$PSScriptRoot\Common.ps1" で読み込む。

# WSL や VS Code のターミナル、GitHub Actions のログに日本語が化けずに出るよう、出力を UTF-8 にする。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:PackageName = 'Laplusdestiny.StationCountdown'
$script:ProcessName = 'StationCountdown.Extension'
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:ExtensionProject = Join-Path $RepoRoot 'src\StationCountdown.Extension\StationCountdown.Extension.csproj'
$script:AppxManifestSource = Join-Path $RepoRoot 'src\StationCountdown.Extension\Package.appxmanifest'

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

# Microsoft.Windows.SDK.BuildTools（NuGet）に入っている signtool.exe を探す。Windows SDK のインストールは不要。
function Find-SignTool([string] $Dotnet) {
    $line = & $Dotnet nuget locals global-packages --list | Select-Object -First 1
    $root = ($line -replace '^global-packages:\s*', '').Trim()
    $tool = Get-ChildItem (Join-Path $root 'microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) {
        throw "signtool.exe が見つかりません（$root\microsoft.windows.sdk.buildtools）。先に拡張機能を一度ビルドして、NuGet パッケージを復元してください。"
    }
    return $tool.FullName
}

function Stop-Extension {
    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "動いている拡張機能のプロセスを止めます（PID: $($running.Id -join ', ')）"
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
}
