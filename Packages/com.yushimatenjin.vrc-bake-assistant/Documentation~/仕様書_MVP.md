# VRC Bake Assistant MVP仕様書

## コンセプト

VRChatワールド制作者が、ライトベイク、Light Probe、Reflection Probe、Static設定を迷わず進められる手順ナビ型エディター拡張。

## ターゲット

- Unity/VRChatワールド制作の初中級者
- ベイクの設定場所や順番で迷う制作者
- 練習シーンで基礎を覚えてから自分のワールドへ適用したい制作者

## 主要価値

- 診断結果を見ながら作業できる
- Practice / Quest / PC / Final のプリセットで試し焼きしやすい
- Probe不足、UV2不足、Static未設定を見つけられる
- 1つのEditorWindowで手順を完結できる

## 画面フロー

1. 練習シーン作成
2. シーン診断
3. Static / UV修正
4. Lighting Settingsプリセット適用
5. Light Probe / Reflection Probe生成
6. Bake開始
7. Lighting Dataクリア

## 将来拡張

- 既存Lighting Settingsのバックアップ/復元
- 部屋ごとのReflection Probe一括生成
- NavMeshやColliderから歩行可能範囲を推定したLight Probe配置
- Lightmap使用量のレポート出力
- VRChat SDK Build Panelの警告との連携
- Before/Afterスクリーンショット生成
- VPMパッケージ化
