# VRC Bake Assistant

VRChatワールド制作者向けの無料ライトベイク手順補助ツールです。Unity 2022.3.22f1 / Built-in Render Pipeline / VRChat Worlds SDK想定のエディター拡張として作っています。

Lightmap、Light Probe、Reflection Probe、Static設定、UV2チェック、Bake開始までを、Unityエディター内の1つのウィンドウから順番に進められるようにすることを目的にしています。

## Links

- Package source: `https://github.com/yushimatenjin/vrc-bake-assistant`
- VPM Repository: `https://yushimatenjin.github.io/yushimatenjin-vpm/vpm.json`
- Add to ALCOM / VCC: `vcc://vpm/addRepo?url=https%3A%2F%2Fyushimatenjin.github.io%2Fyushimatenjin-vpm%2Fvpm.json`

VPM配布は共通リポジトリ `yushimatenjin-vpm` から行います。このリポジトリは `VRC Bake Assistant` のソースとパッケージ生成用です。

## Install with ALCOM / VCC

1. 配布ページを開く: `https://yushimatenjin.github.io/yushimatenjin-vpm/`
2. `ALCOM / VCC に追加` を押す
3. 反応しない場合は、ALCOMの `Packages > ADD REPOSITORY` に次のURLを追加する

```text
https://yushimatenjin.github.io/yushimatenjin-vpm/vpm.json
```

4. 対象プロジェクトの `Manage Project` から `VRC Bake Assistant` を追加する
5. Unityで `Tools > YushimaTenjin > VRC Bake Assistant` を開く

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
4. ローカルまたはActionsで `python scripts/package_release.py` と `python scripts/update_vpm_json.py` を実行する
5. `Actions > Deploy GitHub Pages` を手動実行
6. `https://yushimatenjin.github.io/yushimatenjin-vpm/` を開いて確認
7. `https://yushimatenjin.github.io/yushimatenjin-vpm/packages/com.yushimatenjin.vrc-bake-assistant-0.1.7.zip` がダウンロードできるか確認
8. ALCOMで `https://yushimatenjin.github.io/yushimatenjin-vpm/vpm.json` を追加してインストール確認

## Local build

```bash
python scripts/package_release.py
python scripts/update_vpm_json.py
```

生成物:

```text
Dist/com.yushimatenjin.vrc-bake-assistant-0.1.7.zip
Website/vpm.json
Website/index.json
```

共通VPMリポジトリへ公開するときは、生成したzipを `yushimatenjin-vpm/packages/` に追加して、そちらの `scripts/build_repository.py` で listing を再生成します。

## License

MIT License. 詳細は `LICENSE.md` を参照してください。

## 注意

本ツールはベイク作業を補助するエディター拡張です。すべてのワールドで最適な見た目を自動保証するものではありません。使用前にプロジェクトのバックアップを推奨します。


## 404対策メモ

この版では、ALCOM/VCCがダウンロードするpackage zipをGitHub ReleasesではなくGitHub Pagesの `packages/` 以下に置きます。Release未作成による404を避けるためです。


## ALCOM/VPMのSHA256不一致について

開発初期は同じversionのzipを作り直すことが多いため、この版ではRepository Listingの `zipSHA256` を省略しています。安定版に入ってから、versionを必ず上げる運用とセットで `zipSHA256` を戻すのがおすすめです。


## 0.1.6 コンパイル修正

Unity 2022.3.22f1で `EditorGUILayout.IntPopup` の引数型が合わずコンパイルできない問題を修正しました。

## 0.1.3 見える化改善

練習シーンHierarchy整理、RendererのBake参加リスト、Light一覧、前回GI Bake情報、新規ベイク候補、Light Probe手動サイズ、Probe範囲ガイドを追加しました。

## 0.1.2 UI改善

プリセット説明、練習シーンのProbe/Reflection Probe自動配置、Skybox OFF、基本ライトセット、操作フィードバックを追加しました.

## 0.1.7: Bake対応マテリアル複製/置換

GLB/glTFastや購入アセットのMaterialで、ライトベイク後に見た目が馴染まない場合の確認用機能です。

- Material一覧を手動更新で取得
- Unlit / glTF / 特殊Shaderを「要確認」として表示
- 元Materialは削除せず、`Assets/VRCBakeAssistant/GeneratedMaterials` にStandard Shader版の複製を作成
- Renderer側のMaterial割り当てだけを置換
- BaseColor / MainTex / Normal / Metallic / Smoothness / Emissionの一部をコピー
- EmissionをBaked GIとして扱う設定を追加

透明、Toon、特殊Shaderは見た目が変わりやすいです。まずはHierarchyで対象を選択し、「選択Rendererだけ置換」から試してください。
