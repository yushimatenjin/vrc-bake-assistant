# VRC Bake Assistant

VRChatワールド制作者向けの無料ライトベイク手順化ツールです。
Unityのメニューから `Tools > VRC Bake Assistant > Open` を開いて使います。

## 主な機能

- 練習用シーンの自動生成
- Renderer / Light / Light Probe / Reflection Probe の診断
- Contribute GI と Reflection Probe Static の設定補助
- UV2不足モデルの Generate Lightmap UVs 有効化補助
- Lighting Settings プリセット適用
- Light Probe Grid 生成
- Reflection Probe 生成・個別Bake
- Lightmapping.BakeAsync によるBake開始

## 対応想定

- Unity 2022.3 系
- VRChat Worldsプロジェクト
- Built-in Render Pipeline想定

## 使い方

1. `Tools > VRC Bake Assistant > Open` を開く
2. まずは `練習用シーンを新規作成` で流れを試す
3. 自分のワールドではバックアップ後に `現在のシーンを診断`
4. プリセット、Light Probe、Reflection Probeを設定
5. 軽いプリセットで試し焼きしてから本番Bake

## 無料配布について

このパッケージは無料で公開する想定です。VPM Repository URLをALCOM / VCCに追加して導入できます。

```text
https://yushimatenjin.github.io/vrc-bake-assistant/vpm.json
```

## 注意

本ツールはベイク作業を補助するエディター拡張です。ワールド構造、シェーダー、ライト配置、PC/Quest対応により最適値は変わります。使用前にプロジェクトのバックアップを推奨します。

## License

MIT License.
