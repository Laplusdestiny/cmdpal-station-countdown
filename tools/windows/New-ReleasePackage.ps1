<#
.SYNOPSIS
    配布用の zip を作る。x64 と ARM64 の MSIX をビルドして自己署名の証明書で署名し、インストール用のスクリプトと一緒にまとめる。

.DESCRIPTION
    GitHub Actions の release ワークフロー（.github/workflows/release.yml）から呼ばれる。手元でも同じように動く。
    MSIX は Release 構成（トリミングあり、.NET ランタイムを含む self-contained）。

    出力（-OutputDir）:
      StationCountdown-{Version}.zip        配布する zip（MSIX 2 つ、証明書、install.cmd など）
      StationCountdown-{Version}.zip.sha256 zip の SHA-256

.PARAMETER Version
    1.2.3 の形。MSIX のバージョンは 1.2.3.0 になる。

.PARAMETER CertificatePath
    署名に使う証明書（.pfx）。Subject は Package.appxmanifest の Publisher（CN=Laplusdestiny）と同じであること。

.EXAMPLE
    .\tools\windows\New-ReleasePackage.ps1 -Version 0.1.0 -CertificatePath C:\secure\signing.pfx -CertificatePassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $CertificatePath,

    [Parameter(Mandatory)]
    [securestring] $CertificatePassword,

    [string[]] $Platforms = @('x64', 'ARM64'),

    [string] $OutputDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'out\release')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$packageVersion = "$Version.0"
$password = [System.Net.NetworkCredential]::new('', $CertificatePassword).Password

# 証明書の確認（Publisher との一致）。公開用の .cer もここから書き出す。
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    (Resolve-Path $CertificatePath).Path, $password,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$manifestText = [System.IO.File]::ReadAllText($AppxManifestSource, $utf8NoBom)
$publisher = [regex]::Match($manifestText, '<Identity[^>]*\sPublisher="([^"]+)"').Groups[1].Value
if ($certificate.Subject -ne $publisher) {
    throw "証明書の Subject（$($certificate.Subject)）が、Package.appxmanifest の Publisher（$publisher）と一致しません。"
}
Write-Host "証明書: $($certificate.Subject)（拇印 $($certificate.Thumbprint)、期限 $($certificate.NotAfter.ToString('yyyy-MM-dd'))）"

$dotnet = Find-Dotnet10
$signtool = Find-SignTool $dotnet
Write-Host "dotnet: $dotnet"
Write-Host "signtool: $signtool"

$staging = Join-Path $OutputDir "StationCountdown-$Version"
$work = Join-Path $OutputDir 'work'
foreach ($dir in @($staging, $work)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# MSIX のバージョンは Package.appxmanifest の Identity から決まるので、ビルドの間だけ書き換える。
$patched = [regex]::Replace($manifestText, '(<Identity[^>]*\sVersion=")[^"]+(")', "`${1}$packageVersion`${2}")
[System.IO.File]::WriteAllText($AppxManifestSource, $patched, $utf8NoBom)
try {
    foreach ($platform in $Platforms) {
        Write-Host "`n=== $platform をビルドします（$packageVersion） ==="
        $packageDir = Join-Path $work $platform
        & $dotnet publish $ExtensionProject -c Release "-p:Platform=$platform" "-p:Version=$Version" -nologo `
            -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false "-p:AppxPackageDir=$packageDir\"
        if ($LASTEXITCODE -ne 0) {
            throw "$platform のビルドに失敗しました（終了コード $LASTEXITCODE）。"
        }

        $built = Get-ChildItem $packageDir -Recurse -Filter '*.msix' | Select-Object -First 1
        if (-not $built) {
            throw "$platform の MSIX が見つかりません: $packageDir"
        }

        $arch = $platform.ToLowerInvariant()
        $msix = Join-Path $staging "StationCountdown_$($Version)_$arch.msix"
        Copy-Item $built.FullName $msix

        & $signtool sign /fd SHA256 /f (Resolve-Path $CertificatePath).Path /p $password $msix
        if ($LASTEXITCODE -ne 0) {
            throw "$platform の MSIX の署名に失敗しました（終了コード $LASTEXITCODE）。"
        }
        $signature = Get-AuthenticodeSignature $msix
        if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "$platform の MSIX が、指定した証明書で署名されていません。"
        }
        Write-Host "署名しました: $(Split-Path $msix -Leaf)（$([math]::Round((Get-Item $msix).Length / 1MB, 1)) MB）"
    }
}
finally {
    [System.IO.File]::WriteAllText($AppxManifestSource, $manifestText, $utf8NoBom)
}

# 利用者が信頼する公開用の証明書（秘密鍵は含まない）と、インストール用のスクリプト。
[System.IO.File]::WriteAllBytes((Join-Path $staging 'StationCountdown.cer'),
    $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
Copy-Item (Join-Path $RepoRoot 'tools\release\*') $staging -Recurse

$zip = Join-Path $OutputDir "StationCountdown-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii

Write-Host "`n配布用の zip を作りました: $zip（$([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB）"
Write-Host "SHA-256: $hash"
Get-ChildItem $staging | ForEach-Object { Write-Host "  $($_.Name)" }
