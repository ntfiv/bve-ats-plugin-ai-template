# {{NAME}}

BVE5/BVE6 用の ATS プラグインです。[bve-ats-plugin-ai-template](https://github.com/ntfiv/bve-ats-plugin-ai-template) から作成しました。
(このプラグインの説明をここに書く)

## 機能

(ここに、このプラグインが何をするかを書く)

## ビルド

Visual Studio 2022 で `{{NAME}}.sln` を開き、**Release|x86 と Release|x64 の両方**をビルドします
(詳しくは `docs/KNOWHOW.md`)。実際に使うファイルは、次のサブフォルダにできます。

| ビルド設定 | 実際に使うファイル | 対象 | Vehicle.txt のキー |
|---|---|---|---|
| Release\|x86 | `src\{{NAME}}\bin\x86\Release\net48\x86\{{NAME}}.dll` | BVE5(32bit) | `Ats` / `Ats32` |
| Release\|x64 | `src\{{NAME}}\bin\x64\Release\net48\x64\{{NAME}}64.dll` | BVE6(64bit) | `Ats64` |

`tools\Build-Check.ps1` を実行すると、両方のビルドと、ビット数・16 個のエクスポートの確認まで行います
(`powershell -ExecutionPolicy Bypass -File tools\Build-Check.ps1`)。

## 配布物の構成

(車両フォルダのどこに、どのファイルを置くかを書く。外部パッケージを使うなら、必須の依存 DLL と、wav などのデータも。)
`tools\Build-Check.ps1 -Stage <出力先>` を実行すると、`{{NAME}}.dll`・`{{NAME}}64.dll`・依存 DLL・`{{NAME}}.ini.sample` が
1 つのフォルダに集まります。自分のデータと、パッケージのライセンス文は、手で足します。

何も起きないときは、ini を `[Log] Enabled = true` にして `%TEMP%\{{NAME}}.log`(BVE6 は `{{NAME}}64.log`)を見てください。

## Panel / Sound の番号

(このプラグインが使う番号の範囲を書く。`AtsLogic.PanelIndexMin/Max`・`SoundIndexMin/Max` と一致させる)

## 設定(ini)

`{{NAME}}.ini`(DLL と同じフォルダ)。配布物には、ログを止めた `{{NAME}}.ini.sample` を入れます。

## ライセンス

未定。(同梱の `LICENSE` は、テンプレートの MIT License のままで、著作権者名はテンプレートの作者です。
配布するときは、ライセンスと著作権者名を決めて、`LICENSE` と、ここを書き換えてください。)
