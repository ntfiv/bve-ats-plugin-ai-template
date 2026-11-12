# bve-ats-plugin-ai-template

**AI(Claude Code・Codex など)に BVE5/BVE6 の ATS プラグインを作らせるための土台**です。
C# で書く、ネイティブの `atsplugin.h` 方式のプラグインを対象にしています。

自分でコードを書くためのひな形、というより、**AI にこのリポジトリを読ませて「こういうプラグインを作って」と頼む**
使い方を前提にしています。AI がつまずきやすい点(ビルドの落とし穴、複数プラグイン併用時の決まり)は、
あらかじめ [AGENTS.md](AGENTS.md) と [docs/KNOWHOW.md](docs/KNOWHOW.md) に書いてあり、
プロジェクトの作成はスクリプト([tools/New-Plugin.ps1](tools/New-Plugin.ps1))で確実に行えます。

> **非公式・自己責任です。** BVE の開発元とは関係ありません。AI が書いたコードも含め、実際の車両やシナリオに使う前に、
> 必ず手元の BVE で動作を確認してください。

## 使い方

### 用意するもの
- Windows、Visual Studio 2022 **17.8 以降**(C# 12 を使うため)。Visual Studio Installer で次を入れる:
  - 「.NET デスクトップ開発」ワークロードと「.NET Framework 4.8 targeting pack」
  - 「MSVC C++ ビルドツール」(例: 「MSVC v143 - VS 2022 C++ x64/x86 ビルドツール」)。`tools/Build-Check.ps1` がエクスポートの確認に使う `dumpbin.exe` が含まれる
- AI のコーディングツール(Claude Code、Codex など。リポジトリ内の `AGENTS.md` / `CLAUDE.md` を読む形式のもの)
- BVE5 または BVE6(動作確認用)

### 手順
1. このリポジトリをクローンし、そのフォルダを AI ツールで開く。
2. 作りたいプラグインを AI に頼む(下の「頼み方」)。AI は `AGENTS.md` と `docs/KNOWHOW.md` を読み、
   `tools/New-Plugin.ps1` で新しいプラグインのフォルダを作り、`AtsLogic.cs` に処理を書きます。
3. **ビルドと確認は Visual Studio 2022 で行う**(Release|x86 と Release|x64 の両方)。AI の環境で
   ビルドできない場合も、手順は AI が案内します。
4. **BVE で実際に動かして確かめる。** AI は BVE を動かせないので、ここだけは人間の役目です。
   不具合があれば、ログ(`%TEMP%\<プラグイン名>.log`)を AI に見せて直してもらいます。

### 頼み方の例

最初に AI に伝えるとよいこと:

- **何をするプラグインか**(例: 「速度が一定を超えたらブレーキをかけるATS」「ドアの状態を Panel に出す」)
- **併用するプラグイン**と、使っている Panel / Sound の番号(併用で番号が重ならないようにするため)
- **使う BVE のバージョン**(5 / 6 / 両方)

```text
このリポジトリの AGENTS.md と docs/KNOWHOW.md を読んで、BVE5/6 用の ATS プラグインを作ってください。
名前は SpeedGuard。速度が制限を超えたら常用ブレーキを最大まで掛けるだけのシンプルなものです。
ATS Plugin Multiplexer で他のプラグインと併用します。Panel は 200〜209 だけを使ってください。
ビルドは Visual Studio でやるので、コードを書いたら、ビルド手順と確認項目を教えてください。
```

```text
作った SpeedGuard が、制限速度を ini から読むようにしてください(シナリオを読み込み直したときに反映されれば十分です)。
ログは ini で止められるようにして、配布用の .ini.sample も作ってください。
```

AI は、次のことを守るように `AGENTS.md` に書いてあります。守られていないと感じたら、そこを指摘してください。

- 介入しないハンドル(Power/Brake/Reverser)は、受け取った値をそのまま返す
- Panel/Sound は宣言した範囲だけに書く(併用する他のプラグインと重ならないように)
- ビルドしたら 16 個のエクスポートを確認する
- 実機で確認できていないことは、「未確認」と分けて報告する

## このリポジトリの中身

```
bve-ats-plugin-ai-template/
├── AGENTS.md / CLAUDE.md             ← AI向けの指示(入口)
├── docs/KNOWHOW.md                   ← 落とし穴と、その理由(AIも人間も読む)
├── tools/
│   ├── New-Plugin.ps1               ← 新しいプラグインのプロジェクトを作るスクリプト
│   ├── Build-Check.ps1              ← 両ビット数のビルドと、16個のエクスポートの確認。-Stage で配布物を1フォルダに集める(新プラグインにもコピーされる)
│   ├── Smoke-Test.ps1               ← BVE なしで、配布の形のDLLを LoadLibrary で読み込んで呼ぶ(依存DLLの置き忘れの検出。新プラグインにもコピーされる)
│   ├── README.stub.md               ← 新しいプラグインに置く README の雛形
│   └── AGENTS.banner.md             ← 新しいプラグインの AGENTS.md の冒頭に入れる注記
├── BveAtsPlugin.sln
├── DllExport.bat                     ← DllExport(3F)の取得・復元用。削除しないこと
└── src/BveAtsPlugin/                 ← テンプレートのプロジェクト本体
    ├── BveAtsPlugin.csproj           ← x86/x64、DllExport の設定
    ├── AtsInterop.cs                 ← atsplugin.h と一致する定数・構造体、範囲チェック付き NativeIntArray
    ├── AtsExports.cs                 ← 16個のネイティブエクスポート(全例外を捕まえて AtsLogic へ委譲)
    ├── AtsLogic.cs                   ← ★処理を書く場所。現状は何もしない透過的なパススルー
    ├── AtsDebug.cs                   ← ファイルログ(iniの [Log] Enabled で止められる)
    ├── AtsConfig.cs                  ← 独自の設定(ini)を読む部品(ReadDouble / ReadInt / ReadBool / ReadString)
    ├── BveAtsPlugin.ini.sample       ← 配布用のサンプルini(ログを止めた状態)
    └── DllExportCompileCheckShim.cs  ← `dotnet build` での簡易コンパイルチェック用スタブ
```

## 想定している併用: ATS Plugin Multiplexer / DetailManager

ここから作るプラグインは、単独で完結させるのではなく、
[ATS Plugin Multiplexer](https://github.com/magicant/bve-plugin-multiplexer) または
[DetailManager](https://github.com/mikangogo/DetailManager) で複数まとめて1両に組み込むことを想定しています
(Multiplexer は DetailManager の Rust による書き直しです)。こうした「プラグインをまとめて呼び出すプラグイン」の経由だと、
**ハンドル値の扱い**と **Panel/Sound の番号**に守るべき決まりがあります。テンプレートはそれに合わせてあり、
AI にもそう指示してあります(詳しくは [docs/KNOWHOW.md](docs/KNOWHOW.md))。単独で使う場合も、そのまま動きます。

## 新しいプラグインのプロジェクトを作る(スクリプト)

AI に頼むと AI が実行しますが、自分で実行することもできます。

```powershell
powershell -ExecutionPolicy Bypass -File tools/New-Plugin.ps1 -Name MyAts
```

このリポジトリの隣に `MyAts` フォルダができます(`-Destination` で場所を指定可能)。
名前空間・アセンブリ名(x86 は `MyAts.dll`、x64 は `MyAts64.dll`)・`.sln`・`.csproj`・`.ini.sample` が
`MyAts` に置き換わり、`DllExportIdent` とプロジェクトの GUID は新しく発行されます。
`AtsLogic.cs` に処理を書き、Panel/Sound の範囲(`AtsLogic.PanelIndexMin/Max`・`SoundIndexMin/Max`)を決めれば完成です。
名前の末尾を `64` にすることはできません(x64 版の DLL 名が `<名前>64` になり、ini の名前の決め方とぶつかるため)。
できたフォルダの `AGENTS.md` の冒頭には「ここは作成済みのプラグイン」という注記が入り、`tools/` には `Build-Check.ps1` と `Smoke-Test.ps1` だけが入ります。

### 外部パッケージ(NAudio など)を使うプラグイン

`.csproj` に `PackageReference` を足すだけで使えます。依存 DLL を探す仕組みは `AtsExports.cs` に最初から入っています。
依存 DLL は本体 DLL とは**別の場所**に出るので、配布物は `tools/Build-Check.ps1 -Stage <出力先>` で 1 つのフォルダに集め、
`tools/Smoke-Test.ps1` で、BVE なしに「依存 DLL を見つけて動くか」を確かめられます(BVE の中での確認ではありません)。
詳しくは [docs/KNOWHOW.md](docs/KNOWHOW.md) の 4-2。

## ビルド

**Visual Studio 2022 でビルドしてください。** `.sln` を開き、**Release|x86 と Release|x64 の両方**をビルドします
(ビルド > バッチビルド で両方にチェック)。初回は `DllExport.bat` が DllExport パッケージを NuGet から取得します(ネットワークが必要)。

コマンドラインでは、**`tools/Build-Check.ps1`** が、ビルドとエクスポートの確認までをまとめて行います
(`vswhere` で Visual Studio の MSBuild と `dumpbin` を自分で見つけるので、PATH の設定は要りません)。

```powershell
powershell -ExecutionPolicy Bypass -File tools/Build-Check.ps1
```

`RESULT: OK` と出れば、2 つの DLL のビット数と 16 個のエクスポートが確認できています(BVE での動作の確認ではありません)。
手で行う場合は、VS 付属の MSBuild.exe(場所は `vswhere` で調べます)で、`/restore` を付けて両方をビルドします。

```powershell
MSBuild.exe BveAtsPlugin.sln /restore /p:Configuration=Release /p:Platform=x86
MSBuild.exe BveAtsPlugin.sln /restore /p:Configuration=Release /p:Platform=x64
```

`dotnet build` はコンパイルチェックにしかなりません(ネイティブエクスポートの埋め込みが行われず、
BVE に渡せる DLL にならない)。AI の環境が Visual Studio を使えないときは、`dotnet build` で文法だけ確認し、
本ビルドは人間が Visual Studio で行います。

### 実際に使うファイルの場所

DllExport は 1 回のビルドで x86 版・x64 版の両方を IL 単位で生成し、`src\BveAtsPlugin\bin\<Platform>\Release\net48\` の
**直下ではなく**、その下の `x86\` / `x64\` サブフォルダに出力します(直下に残るのはエクスポートの無い素のマネージドDLLです)。
パスはリポジトリ直下からの相対パスです。

| ビルド設定 | 実際に使うファイル | 対象 | Vehicle.txt のキー |
|---|---|---|---|
| Release\|x86 | `src\BveAtsPlugin\bin\x86\Release\net48\x86\BveAtsPlugin.dll` | BVE5(32bit) | `Ats` / `Ats32` |
| Release\|x64 | `src\BveAtsPlugin\bin\x64\Release\net48\x64\BveAtsPlugin64.dll` | BVE6(64bit) | `Ats64` |

### エクスポートが付いているかの確認

エクスポートが 1 つも無い DLL でも BVE にはエラーなく読み込まれ、何も起きません。必ず確認します。
`tools/Build-Check.ps1` が確認まで行います。手で行うなら:

```powershell
dumpbin /headers /exports src\BveAtsPlugin\bin\x86\Release\net48\x86\BveAtsPlugin.dll
dumpbin /headers /exports src\BveAtsPlugin\bin\x64\Release\net48\x64\BveAtsPlugin64.dll
```

`/headers` で `14C machine (x86)` / `8664 machine (x64)` が意図どおりか、`/exports` で次の 16 個が
出ているかを**両方の DLL で**確認します: `Load` `Dispose` `GetPluginVersion` `SetVehicleSpec`
`Initialize` `Elapse` `SetPower` `SetBrake` `SetReverser` `KeyDown` `KeyUp` `HornBlow` `DoorOpen`
`DoorClose` `SetSignal` `SetBeaconData`。`dumpbin` は Visual Studio 付属ですが PATH には入っておらず、
実体は `<VS のインストール先>\VC\Tools\MSVC\<版>\bin\Hostx64\x64\dumpbin.exe` です(「Developer PowerShell」からなら直接呼べます)。

ビルドが通らないときは [docs/KNOWHOW.md](docs/KNOWHOW.md) の「ビルド(DllExport)の落とし穴」を見てください(AI にも読ませてください)。

## 車両への組み込み(テスト用)

ビルドした DLL を車両フォルダに置き、`Vehicle.txt` から相対パスで参照します。BVE5 専用なら:

```
Ats = BveAtsPlugin.dll
```

BVE5(32bit の x86 版)と BVE6(64bit の x64 版)の両方で使うなら、ビット数別のキーを使います。

```
Ats32 = BveAtsPlugin.dll
Ats64 = BveAtsPlugin64.dll
```

他のプラグインと併用するときは、`Ats32`/`Ats64` にまとめ役のプラグイン(Multiplexer または DetailManager)の DLL を指定し、
そちらの設定ファイルに、このプラグインへの相対パスを他のプラグインと並べて書きます。

- ATS Plugin Multiplexer: DLL 名に連動した `pluginmultiplexer32.txt` / `pluginmultiplexer64.txt`(DLL をリネームしたら txt も同じ名前に)。
- DetailManager: DLL と同じフォルダの `detailmodules.txt`。

何も起きないときは `%TEMP%\BveAtsPlugin.log`(エクスプローラーのアドレスバーに `%TEMP%` と入力すると開けます。
BVE6 は `BveAtsPlugin64.log`)を見てください。

## 関連リポジトリ

- [KTY_MARKER](https://github.com/ntfiv/KTY_MARKER) — このひな形の流儀で作った、音声マーカー検知プラグイン
- [BveRefViewer](https://github.com/ntfiv/BveRefViewer) — シナリオ/車両ファイルから参照先をたどって表示する確認用ツール
  (参照切れ・`Ats32`/`Ats64` のビット数違い・エクスポート漏れの確認に使えます)

## 謝辞

このテンプレートとノウハウは、Muse・ChatGPT・Gemini にレビューしてもらいました。

## ライセンス

MIT License([LICENSE](LICENSE))。依存する第三者のソフトウェアについては [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
