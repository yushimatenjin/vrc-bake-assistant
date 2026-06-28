# VRC Bake Assistant

VRChatワールド制作者向けの無料ライトベイク手順化ツールです。  
Unityのメニューから `Tools > YushimaTenjin > VRC Bake Assistant` を開いて使います。旧メニューの `Tools > VRC Bake Assistant > Open` も残しています。

## できること

- 0から練習シーンを作成
- 練習シーンのHierarchyを Guide / BakeTargets / NotBaked / Lights / Probes / Camera に整理
- 練習シーンに基本ライト、Light Probe Group、Reflection Probe、原点軸、Probe範囲ガイドを自動配置
- Skybox OFFでライトの効果を見やすくする
- シーン診断
- ライト数、ライト種類、Baked / Mixed / Realtime、前回Bakeメモの見える化
- Renderer一覧から、Bake参加 / 未参加を個別に切り替え
- 床・壁・動かない家具をライトマップ対象にする
- UV2不足のFBXでGenerate Lightmap UVsをON
- 日本語説明付きのベイク品質プリセット適用
- Light Probe / Reflection Probeの作成
- Light Probeの手動サイズ指定と、黄色い範囲ガイド表示
- ライトマップベイク開始
- Reflection Probe単体ベイク
- Lighting Data Assetクリア

## プリセット

| 表示名 | 用途 |
|---|---|
| 試し焼き（早い） | 光の方向、色、大まかな明るさを短時間で確認する低品質設定です。 |
| Quest向け軽量 | Quest/Android向けにLightmap容量と負荷を抑える設定です。 |
| PC向け標準 | PCワールド向けのバランス設定です。迷ったらここから始めます。 |
| 仕上げ確認（重い） | 公開前の最終確認向けです。時間と容量が増えやすいので最後だけ使います。 |

## 基本の流れ

1. `Tools > YushimaTenjin > VRC Bake Assistant` を開く
2. `0から練習シーンを作る`
3. `現在のシーンを診断`
4. `試し焼き（早い）` をシーンに適用
5. 基本ライト、Light Probe、Reflection Probeを確認
6. `ライトマップをベイク開始`
7. 見た目が良ければ `PC向け標準` や `仕上げ確認（重い）` に上げる

## 0.1.4 の主な改善

- Unity 2022.3.22f1でReflection Probe解像度欄がコンパイルエラーになる問題を修正。


- 練習シーンのHierarchy名を整理し、Guide / BakeTargets / NotBaked / Lights / Probes / Camera でまとまるようにしました。
- Renderer一覧を追加し、Bake参加 / 未参加、UV2確認、動的っぽい名前の目安を見ながら切り替えられるようにしました。
- Light一覧を追加し、Baked / Mixed / Realtime、ライト種類、前回Bakeメモとの差分、新規/変更ベイク候補を見える化しました。
- Light Probeの手動サイズ指定と、黄色い範囲ガイドを追加しました。
- Realtime Lightの意味や、BakedではないLightがワールド実行時に影響しやすいことをUI内に説明しました。

## 0.1.2 の主な改善

- プリセットを日本語表示にし、用途説明を追加しました。
- 練習シーンに Light Probe Group と Reflection Probe を最初から配置しました。
- Skybox OFF、原点軸、基本ライトセット追加/更新、Light全削除を追加しました。
- Static / UV チェックの説明を初心者向けに変更しました。
- 各操作後にウィンドウ上部の「フィードバック」へ完了内容を表示します。
- 新規シーン作成などはGUI描画後に実行し、IMGUIのLayoutGroupエラーを避ける構造にしました。

## 注意

このツールはベイク作業を補助するエディター拡張です。すべてのワールドで最適な見た目を自動保証するものではありません。既存ワールドで使う前にバックアップをおすすめします。
