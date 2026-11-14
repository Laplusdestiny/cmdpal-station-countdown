Station Countdown（PowerToys Command Palette の拡張機能）

Command Palette の Dock に、登録した駅・方面ごとに「家を出るまであと何分か」を表示します。
https://github.com/Laplusdestiny/cmdpal-station-countdown

■ 必要なもの
- Windows 10 2004 以降（x64 または ARM64）
- PowerToys 0.98 以降（Command Palette の Dock を有効にしておく）

■ インストール・更新
1. zip をすべて展開する
2. install.cmd をダブルクリックする
   - 初回だけ、署名の証明書（CN=Laplusdestiny）を「信頼されたユーザー」に登録するため、管理者の確認が表示されます
   - この証明書は開発者が自分で作ったもの（自己署名）です。発行元を信頼できる場合だけ進めてください
3. Command Palette で「Reload」（拡張機能の再読み込み）を実行する
4. 「駅カウントダウン」→「＋ 阪急の駅を追加」などで駅を追加し、Dock の編集モードでバンドをピン留めする

更新するときも、新しい zip を展開して install.cmd を実行します。設定と時刻表は残ります。

■ アンインストール
uninstall.cmd をダブルクリックします。設定と時刻表（%LOCALAPPDATA%\StationCountdown）は残ります。

■ データの取り扱い
- 時刻表の著作権は各事業者に帰属します。取得したデータは私的利用の範囲でお使いください
- 祝日データ 出典：内閣府『国民の祝日について』（公共データ利用規約 第1.0版）
