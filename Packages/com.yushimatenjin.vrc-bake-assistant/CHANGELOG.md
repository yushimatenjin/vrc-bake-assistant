# Changelog

## 0.1.4 - Compile Fix

- Unity 2022.3.22f1で `EditorGUILayout.IntPopup` の `GUIContent` ラベル付き呼び出しがコンパイルエラーになる問題を修正しました。
- Reflection Probe解像度欄は `PrefixLabel + IntPopup` の構成に変更しました。
- UdonSharpのscene upgrade停止は、このC#コンパイルエラーの連鎖なので、本修正後に解消する想定です。

## 0.1.3

- 練習シーンのHierarchyを `01_Geometry` / `03_Lights` / `04_LightProbes` / `05_ReflectionProbes` などに整理しました。
- Renderer一覧を追加し、Bake参加 / 未参加を個別に切り替えられるようにしました。
- Light一覧を追加し、Baked / Mixed / Realtime の状態、ライト種類、前回GI Bake情報、新規ベイク候補を確認できるようにしました。
- Light Probeの手動サイズ指定、Probe範囲コピー、黄色いProbe範囲ガイドを追加しました。
- Realtime LightやMixed Lightの扱いをUI内で説明するようにしました。

## 0.1.2

- プリセット名を日本語UIに変更し、用途説明を追加しました。
- GUI描画中に新規シーン作成などを直接実行しないようにし、`EndLayoutGroup: BeginLayoutGroup must be called first` が出にくい構造へ修正しました。
- 練習シーン作成時に Light Probe Group と Reflection Probe を自動配置するようにしました。
- Skybox OFF、原点軸の追加、基本ライトセット追加/更新、Light全削除を追加しました。
- Static / UV チェックの説明を初心者向けに変更しました。
- 各操作後にウィンドウ上部へ完了・注意・エラーのフィードバックを表示するようにしました。

## 0.1.1

- VPMのダウンロードURLをGitHub Pages上のpackage zipに変更。
- 開発中のSHA256不一致を避けるため、Repository ListingのzipSHA256を一時的に省略。
- ALCOM/VCCで同じ0.1.0のキャッシュに当たらないよう、バージョンを0.1.1へ更新。

## 0.1.0

- Initial free VPM package.
