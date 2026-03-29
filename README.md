# MornPsdConverter

<p align="center">
  <img src="Editor/MornPsdConverter.png" alt="MornPsdConverter" width="640" />
</p>

<p align="center">
  <img src="https://img.shields.io/github/license/TsukumiStudio/MornPsdConverter" alt="License" />
</p>

## 概要

PSD ファイルのレイヤーを PNG エクスポート・Canvas 配下に UI Image として配置するエディタツールです。
PSD を直接パースしてレイヤー座標を取得し、正確な位置に配置します。

## 導入方法

Unity Package Manager で以下の Git URL を追加:

```
https://github.com/TsukumiStudio/MornPsdConverter.git
```

### 依存パッケージ

- [2D PSD Importer](https://docs.unity3d.com/Packages/com.unity.2d.psdimporter@latest) (`com.unity.2d.psdimporter`)

### PSD ファイルの準備

1. PSD ファイルを Assets にインポート
2. Inspector で **PSD Importer** として認識されていることを確認
3. 非表示レイヤーも配置したい場合は **Include Hidden Layers** を ON にする

## 使い方

Project ウィンドウで PSD ファイルを右クリック → `Assets > PSD to UI` から選択:

| メニュー | 説明 |
|---|---|
| **レイヤーをPNGエクスポート** | 各レイヤーを個別 PNG として書き出し |
| **レイヤーをPNGエクスポート + UIに展開** | PNG エクスポート後、Canvas 配下に Image として配置 |
| **レイヤーをPNGエクスポート + UIに展開 (階層あり)** | 上記 + PSD のフォルダ構造を GameObject 階層として再現 |
| **UIに展開** | PSD のサブスプライトを直接参照して配置 |
| **UIに展開 (階層あり)** | 上記 + PSD のフォルダ構造を GameObject 階層として再現 |

### 注意事項

- PSD Importer がスプライトを生成したレイヤーのみ配置されます
- メニューは PSD Importer でインポートされた PSD ファイルを選択時のみ有効です

## ライセンス

[The Unlicense](LICENSE)
