# cmdpal-station-countdown

PowerToys Command Palette の **Dock** に、最寄り駅の「次の電車まであと何分」を常時表示する拡張機能（開発中）。

## ステータス

設計フェーズ。

- 技術調査: [`docs/research/`](docs/research/)
- 設計書（MVP）: [`docs/superpowers/specs/`](docs/superpowers/specs/)
- 将来の機能: [Issues（`future` ラベル）](https://github.com/Laplusdestiny/cmdpal-station-countdown/issues?q=label%3Afuture)

## サンプル駅

| 事業者 | 路線 | 駅 | 想定データソース |
| --- | --- | --- | --- |
| 阪急電鉄 | 宝塚線 | 大阪梅田（宝塚方面） | 公式サイト時刻表 HTML（ユーザー PC 上で取得） |
| 東急電鉄 | 田園都市線 | 宮前平（渋谷方面 / 中央林間方面） | 利用者が用意する YAML ファイル |

## データの取り扱い

鉄道事業者の時刻表データそのものはこのリポジトリに含めません。
各データソースの利用規約・ライセンスに従い、利用者自身の環境で取得・保存する方針です。

## ライセンス

[MIT](LICENSE)
