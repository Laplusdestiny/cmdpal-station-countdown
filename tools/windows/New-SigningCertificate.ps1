<#
.SYNOPSIS
    配布用の MSIX に署名する、自己署名のコード署名証明書を作る（最初の 1 回と、期限が切れる前の作り直しのとき）。

.DESCRIPTION
    Subject は Package.appxmanifest の Publisher（CN=Laplusdestiny）と同じにする。
    -OutputDir に次のファイルを書き出し、証明書ストアからは秘密鍵ごと消す（秘密鍵は .pfx にだけ残る）。
      signing.pfx            秘密鍵を含む。外に出さない。GitHub Secrets に登録したら、安全な場所に保管するか削除する
      signing.pfx.base64.txt signing.pfx を Base64 にしたもの（Secrets の SIGNING_CERT_PFX_BASE64 に登録する）
      password.txt           signing.pfx のパスワード（Secrets の SIGNING_CERT_PASSWORD に登録する）
      StationCountdown.cer   公開用の証明書（配布する zip にも入る）
    登録の手順は docs/release.md を参照。

.EXAMPLE
    .\tools\windows\New-SigningCertificate.ps1 -OutputDir $env:USERPROFILE\StationCountdown-signing
#>
[CmdletBinding()]
param(
    [string] $OutputDir = (Join-Path $env:USERPROFILE 'StationCountdown-signing'),

    [ValidateRange(1, 10)]
    [int] $Years = 5
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$manifestText = [System.IO.File]::ReadAllText($AppxManifestSource)
$publisher = [regex]::Match($manifestText, '<Identity[^>]*\sPublisher="([^"]+)"').Groups[1].Value
if (-not $publisher) {
    throw "Package.appxmanifest から Publisher を読めませんでした: $AppxManifestSource"
}

$pfx = Join-Path $OutputDir 'signing.pfx'
if (Test-Path $pfx) {
    throw "すでに $pfx があります。作り直す場合は、別の -OutputDir を指定するか、古いファイルを移してください。"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# パスワードは十分に長いランダムな文字列にする。
$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = [Convert]::ToBase64String($bytes)
$secure = ConvertTo-SecureString $password -AsPlainText -Force

$certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher `
    -FriendlyName 'Station Countdown code signing' `
    -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years) `
    -CertStoreLocation Cert:\CurrentUser\My `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
try {
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $secure | Out-Null
    Export-Certificate -Cert $certificate -FilePath (Join-Path $OutputDir 'StationCountdown.cer') | Out-Null
}
finally {
    # 秘密鍵は .pfx にだけ残し、この PC の証明書ストアからは消す。
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey
}

[System.IO.File]::WriteAllText((Join-Path $OutputDir 'signing.pfx.base64.txt'), [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($pfx)))
[System.IO.File]::WriteAllText((Join-Path $OutputDir 'password.txt'), $password)

Write-Host "証明書を作りました: $publisher"
Write-Host "  拇印: $($certificate.Thumbprint)"
Write-Host "  期限: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "  出力: $OutputDir"
Write-Host ''
Write-Host '次に、GitHub Secrets に登録します（docs/release.md）:'
Write-Host "  gh secret set SIGNING_CERT_PFX_BASE64 --body (Get-Content -Raw `"$OutputDir\signing.pfx.base64.txt`")"
Write-Host "  gh secret set SIGNING_CERT_PASSWORD --body (Get-Content -Raw `"$OutputDir\password.txt`")"
Write-Host '登録したら、signing.pfx・signing.pfx.base64.txt・password.txt を安全な場所に保管するか削除してください。'
