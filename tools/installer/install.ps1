<#
.SYNOPSIS
    Station Countdown（PowerToys Command Palette の拡張機能）をインストールする。install.cmd から呼ばれる。

.DESCRIPTION
    1. この PC のアーキテクチャ（x64 / ARM64）に合う MSIX を選ぶ
    2. 初回だけ、署名の証明書（StationCountdown.cer）を「信頼されたユーザー」（ローカル コンピューター）に登録する。
       ここだけ管理者の確認が表示される
    3. 開発用に登録したパッケージがあれば外し、MSIX をインストールする（更新も同じ手順）
    設定と時刻表（%LOCALAPPDATA%\StationCountdown）は、インストールや更新で消えない。
#>
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$PackageName = 'Laplusdestiny.StationCountdown'
$here = $PSScriptRoot

$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'x64' }
    'ARM64' { 'arm64' }
    default { throw "この PC のアーキテクチャ（$env:PROCESSOR_ARCHITECTURE）には対応していません。x64 か ARM64 の Windows で実行してください。" }
}
$msix = Get-ChildItem $here -Filter "StationCountdown_*_$arch.msix" | Select-Object -First 1
if (-not $msix) {
    throw "この PC 用の MSIX（StationCountdown_*_$arch.msix）が見つかりません。zip をすべて展開してから実行してください。"
}

# ダウンロードした zip から展開したファイルには「インターネットから来た」印が付くので外す。
Get-ChildItem $here -File | Unblock-File

$cerPath = Join-Path $here 'StationCountdown.cer'
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $certificate.Thumbprint
if (-not $trusted) {
    Write-Host '初回だけ、拡張機能の署名に使っている証明書を、この PC の「信頼されたユーザー」に登録します。'
    Write-Host "  発行者: $($certificate.Subject)"
    Write-Host "  拇印:   $($certificate.Thumbprint)"
    Write-Host '管理者の確認（ユーザー アカウント制御）が表示されたら「はい」を選んでください。'

    $quoted = $cerPath.Replace("'", "''")
    $command = "Import-Certificate -FilePath '$quoted' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    try {
        $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    }
    catch {
        throw '証明書を登録できませんでした（管理者の確認が取り消されました）。もう一度 install.cmd を実行してください。'
    }
    if ($process.ExitCode -ne 0 -or -not (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $certificate.Thumbprint)) {
        throw "証明書を登録できませんでした（終了コード $($process.ExitCode)）。"
    }
    Write-Host '証明書を登録しました。'
}

# 開発用に登録したパッケージ（Build-Extension.ps1 -Register）が入っていると、同じ名前の MSIX を入れられないので外す。
$existing = Get-AppxPackage -Name $PackageName
if ($existing -and $existing.IsDevelopmentMode) {
    Write-Host "開発用に登録されているパッケージを外します: $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Write-Host "インストールします: $($msix.Name)"
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion

$installed = Get-AppxPackage -Name $PackageName
Write-Host ''
Write-Host "インストールしました: $($installed.Name) $($installed.Version)"
Write-Host '次の手順:'
Write-Host '  1. Command Palette を開き、「Reload」（拡張機能の再読み込み）を実行する'
Write-Host '  2. 「駅カウントダウン」→「＋ 阪急の駅を追加」などで駅を追加する'
Write-Host '  3. Dock の編集モードで、駅のバンドをピン留めする'
