# Changelog

## 0.1.6

- Windowを開いた瞬間にRenderer/Light一覧を自動取得しない安全モードへ変更。
- Renderer一覧とLight一覧は折りたたみ状態で開始し、「一覧を更新」を押した時だけ取得するように変更。
- Renderer一覧に取得上限を追加し、大きいワールドでOOMになりにくくしました。
- UV2確認で `mesh.uv2` 配列を読まず、`Mesh.HasVertexAttribute(VertexAttribute.TexCoord1)` を使うように変更。
- シーン内コンポーネント検索を `Resources.FindObjectsOfTypeAll` からActive SceneのRoot探索へ変更。
- Window上に「重複コピーを確認」と「一覧キャッシュを空にする」を追加。
- Toolsメニューは `Tools > YushimaTenjin > VRC Bake Assistant` の1つに整理。

## 0.1.5 - Menu cleanup

- `Tools` メニューの入口を `Tools > YushimaTenjin > VRC Bake Assistant` に一本化しました。
- 互換用に残していた `Tools > VRC Bake Assistant > Open` と `Tools > VRC Bake Assistant > ライトベイク手順を開く` を削除しました。
- 古い手動コピー版や別IDパッケージが残っている場合の確認メモを追加しました。


## 0.1.4

- Unity 2022.3.22f1で `EditorGUILayout.IntPopup` の引数型が合わずコンパイルエラーになる問題を修正しました。
- Reflection Probe解像度のUIを、ラベル表示とIntPopup本体に分けました。
- 機能追加は最小限で、0.1.3の見える化機能をそのまま維持しています。

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
