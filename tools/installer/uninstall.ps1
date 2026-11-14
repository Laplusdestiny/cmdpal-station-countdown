<#
.SYNOPSIS
    Station Countdown をアンインストールする。uninstall.cmd から呼ばれる。

.DESCRIPTION
    パッケージだけを外す。設定と時刻表（%LOCALAPPDATA%\StationCountdown）と、登録した証明書は残す。
    証明書も消す場合は、certlm.msc の「信頼されたユーザー」→「証明書」から CN=Laplusdestiny を削除する。
#>
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$existing = Get-AppxPackage -Name 'Laplusdestiny.StationCountdown'
if (-not $existing) {
    Write-Host 'Station Countdown はインストールされていません。'
    return
}

Remove-AppxPackage -Package $existing.PackageFullName
Write-Host "アンインストールしました: $($existing.PackageFullName)"
Write-Host "設定と時刻表は残っています: $env:LOCALAPPDATA\StationCountdown（不要なら削除してください）"
