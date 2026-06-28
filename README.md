# VRC Bake Assistant

VRChatワールド制作者向けの無料ライトベイク手順補助ツールです。Unity 2022.3.22f1 / Built-in Render Pipeline / VRChat Worlds SDK想定のエディター拡張として作っています。

Lightmap、Light Probe、Reflection Probe、Static設定、UV2チェック、Bake開始までを、Unityエディター内の1つのウィンドウから順番に進められるようにすることを目的にしています。

## Links

- GitHub Pages: `https://yushimatenjin.github.io/vrc-bake-assistant/`
- VPM Repository: `https://yushimatenjin.github.io/vrc-bake-assistant/vpm.json`
- Add to ALCOM / VCC: `vcc://vpm/addRepo?url=https%3A%2F%2Fyushimatenjin.github.io%2Fvrc-bake-assistant%2Fvpm.json`

## Install with ALCOM / VCC

1. 配布ページを開く: `https://yushimatenjin.github.io/vrc-bake-assistant/`
2. `ALCOM / VCC に追加` を押す
3. 反応しない場合は、ALCOMの `Packages > ADD REPOSITORY` に次のURLを追加する

```text
https://yushimatenjin.github.io/vrc-bake-assistant/vpm.json
```

4. 対象プロジェクトの `Manage Project` から `VRC Bake Assistant` を追加する
5. Unityで `Tools > VRC Bake Assistant > Open` を開く

## Repository layout

```text
Packages/com.yushimatenjin.vrc-bake-assistant/  # Unity / VPM package本体
Website/                                       # GitHub Pages用ページとVPM Repository JSON
Dist/                                          # ローカルビルド確認用のzip置き場
scripts/                                       # Release zip / vpm.json生成スクリプト
.github/workflows/                            # Release / Pages用GitHub Actions
```

## Initial publish

1. GitHubで `yushimatenjin/vrc-bake-assistant` をPublicリポジトリとして作成
2. このフォルダの中身をpush
3. `Settings > Pages > Build and deployment > Source` を `GitHub Actions` に設定
4. `Actions > Build Release` を手動実行して `com.yushimatenjin.vrc-bake-assistant-0.1.0.zip` をReleaseに作る
5. `Actions > Deploy GitHub Pages` を手動実行
6. `https://yushimatenjin.github.io/vrc-bake-assistant/` を開いて確認
7. ALCOMで `https://yushimatenjin.github.io/vrc-bake-assistant/vpm.json` を追加してインストール確認

## Local build

```bash
python scripts/package_release.py
python scripts/update_vpm_json.py
```

生成物:

```text
Dist/com.yushimatenjin.vrc-bake-assistant-0.1.0.zip
Website/vpm.json
Website/index.json
```

## License

MIT License. 詳細は `LICENSE.md` を参照してください。

## 注意

本ツールはベイク作業を補助するエディター拡張です。すべてのワールドで最適な見た目を自動保証するものではありません。使用前にプロジェクトのバックアップを推奨します。
