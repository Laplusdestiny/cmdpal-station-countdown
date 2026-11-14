# 配布（リリース）の手順

拡張機能は、署名した MSIX をインストール用のスクリプトと一緒に zip にまとめ、GitHub Release で配布します。
ビルドと署名は GitHub Actions（`.github/workflows/release.yml`）で行います。

## しくみ

- Command Palette は、MSIX パッケージとして入っている拡張機能だけを見つけます（`AppExtensionCatalog`）。そのため、配布物は MSIX です
- MSIX を開発者モードなしで入れるには、PC が信頼する証明書での署名が必要です。費用をかけないため、**自己署名の証明書**で署名し、利用者には初回だけ証明書を信頼してもらいます（`install.cmd` が管理者の確認付きで行います）
- 証明書の Subject は `Package.appxmanifest` の Publisher（`CN=Laplusdestiny`）と同じにします。同じ Subject なら、証明書を作り直しても、同じパッケージとして更新できます

| ファイル | 役割 |
| --- | --- |
| `.github/workflows/release.yml` | `v1.2.3` のタグで起動し、テスト → x64 / ARM64 の MSIX のビルドと署名 → zip を Release に添付 |
| `tools/windows/New-ReleasePackage.ps1` | MSIX のビルド・署名と zip の作成（手元でも動く） |
| `tools/windows/New-SigningCertificate.ps1` | 署名用の自己署名証明書を作る |
| `tools/release/` | zip に入れるもの（`install.cmd`、`uninstall.cmd`、`README.txt` など） |
| `.github/release-notes.md` | Release の説明文の先頭に入れるインストール手順 |

zip の中身:

```
StationCountdown_1.2.3_x64.msix
StationCountdown_1.2.3_arm64.msix
StationCountdown.cer      公開用の証明書（秘密鍵は含まない）
install.cmd / install.ps1
uninstall.cmd / uninstall.ps1
README.txt
```

## 最初に 1 回だけ: 署名の証明書を用意する

Windows の PowerShell で、リポジトリのフォルダーから実行します（WSL から使っている場合は、`tools/windows/build-from-wsl.sh` が写した `%USERPROFILE%\dev\cmdpal-station-countdown` で実行します）。

```powershell
.\tools\windows\New-SigningCertificate.ps1
```

`%USERPROFILE%\StationCountdown-signing` に `signing.pfx`（秘密鍵）・`signing.pfx.base64.txt`・`password.txt`・`StationCountdown.cer` ができます。
証明書ストアからは秘密鍵ごと消すので、秘密鍵は `signing.pfx` にだけ残ります。

GitHub Secrets に登録します（`gh auth login` 済みの PowerShell で）:

```powershell
gh secret set SIGNING_CERT_PFX_BASE64 --repo Laplusdestiny/cmdpal-station-countdown --body (Get-Content -Raw "$env:USERPROFILE\StationCountdown-signing\signing.pfx.base64.txt")
gh secret set SIGNING_CERT_PASSWORD --repo Laplusdestiny/cmdpal-station-countdown --body (Get-Content -Raw "$env:USERPROFILE\StationCountdown-signing\password.txt")
```

登録したら、`signing.pfx`・`signing.pfx.base64.txt`・`password.txt` はパスワード マネージャーなどの安全な場所に保管するか、削除します（Secrets からは取り出せないので、作り直すときは新しく作ります）。

## リリースする

1. `main` にマージした状態で、バージョンのタグを付けて push する

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

2. Actions の「Release」が終わると、`v0.1.0` の Release に `StationCountdown-0.1.0.zip` と `.sha256` が付く

タグを付けずに試す場合は、Actions の「Release」→「Run workflow」でバージョンを入れて実行します（`main` にあるワークフローだけが選べます）。Release は作らず、zip を Artifacts に残します。

手元で zip を作る場合:

```powershell
.\tools\windows\New-ReleasePackage.ps1 -Version 0.1.0 -CertificatePath "$env:USERPROFILE\StationCountdown-signing\signing.pfx" -CertificatePassword (Read-Host -AsSecureString)
```

## バージョン

- タグ `v1.2.3` → MSIX のバージョン `1.2.3.0`。ビルドの間だけ `Package.appxmanifest` の Identity の Version を書き換える
- 更新は、利用者が新しい zip の `install.cmd` を実行するだけ（設定と時刻表は `%LOCALAPPDATA%\StationCountdown` にあり、消えない）

## 証明書の期限と作り直し

- `New-SigningCertificate.ps1` の既定の期限は 5 年です。期限が切れた証明書で署名した MSIX は、新しくはインストールできなくなります
- 作り直すときは、同じ手順で新しい証明書を作って Secrets を更新し、新しいバージョンをリリースします。利用者が新しい zip の `install.cmd` を実行すると、新しい証明書を信頼する手順（管理者の確認）がもう一度入ります
- 秘密鍵が漏れた場合も同じように作り直し、README と Release で古い証明書（拇印）を信頼しないよう案内します

## 制限

- 自己署名なので、利用者は初回に「信頼されたユーザー」へ証明書を登録する必要があります（管理者の確認が 1 回出る）。`install.cmd` 自体も署名されていないため、ダウンロードしたファイルを実行するときに Windows の警告が出ることがあります
- 有償のコード署名証明書や Microsoft Store（#4）に移ると、これらの手順はなくなります
