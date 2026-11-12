# 開発ノウハウ(BVE5/6 ATSプラグインを C# で作る)

このひな形を作り、実際にプラグインを作る過程で踏んだ落とし穴と、その対処をまとめたものです。
「実機で確認済み」と書いたものは、Windows 11・Visual Studio 2022・.NET Framework 4.8 の環境で
BVE5/BVE6 に読み込ませて確かめた内容です。環境やバージョンが違うと結果が変わることがあります。

目次

1. [ATS Plugin Multiplexer / DetailManager で併用するときの決まり](#1-ats-plugin-multiplexer--detailmanager-で併用するときの決まり)
2. [ビルド(DllExport)の落とし穴](#2-ビルドdllexportの落とし穴)
3. [C# の書き方で気をつけること](#3-c-の書き方で気をつけること)
4. [実行時の落とし穴](#4-実行時の落とし穴)
5. [ログのオン/オフ](#5-ログのオンオフ)
6. [Panel / Sound の番号の決め方](#6-panel--sound-の番号の決め方)

---

## 1. ATS Plugin Multiplexer / DetailManager で併用するときの決まり

このひな形のプラグインは、まとめ役のプラグイン経由で併用されることを想定しています。対象は次の 2 つです。

- [ATS Plugin Multiplexer](https://github.com/magicant/bve-plugin-multiplexer)(DetailManager の Rust による書き直し)。
  自身が `Ats` / `Ats32` / `Ats64` として登録され、設定ファイル(`pluginmultiplexer32.txt` /
  `pluginmultiplexer64.txt`。DLL をリネームしたら txt も同じ名前に)に 1 行ずつ書いた子プラグインを順番に呼び出します。
- [DetailManager](https://github.com/mikangogo/DetailManager)(Unlicense)。DLL と同じフォルダの `detailmodules.txt` に
  子プラグインのパスを 1 行ずつ書き、上の行から順に呼び出します。パスの基準はカレントディレクトリではなく、
  DetailManager 自身の DLL のあるフォルダです。

以下の制約は、両者のソース(Multiplexer は `src/lib.rs`、DetailManager は `DetailManager/source/dllmain.cpp`)を読んで
分かったもので、**どちらも同じ作りです**(DetailManager の方は、ソースを読み取った内容に基づきます。
公開されているのが最新版かどうかは分かりません)。

- 子プラグインを順番に呼び、`Elapse` の戻り値(ハンドル値)を**マージせず**に、後ろの子の戻り値で置き換えていく。
- `Elapse` の前に、直前までの結果を `SetPower` / `SetBrake` / `SetReverser` で子に伝える(変化したときだけ。
  DetailManager は最初の `Elapse` のときだけ全員に伝える)。
- `panel` / `sound` は、全子プラグインに**同じポインタ**をそのまま渡す(マージなし)。
- `Initialize` は全子にそのまま転送するだけで、`SetPower` などを送り直さない。

違いが分かっている点: DetailManager は子の `GetPluginVersion` を呼びません。また、`detailmodules.txt` に書いた DLL が
存在しないと、DetailManager はクラッシュする作りになっています(Multiplexer は読み込み失敗でエラーダイアログを出します)。
`ConstantSpeed` の合成(Multiplexer は最大値)は、DetailManager では確認していません。
**どちらの経由でも壊れないよう、以下を守る書き方を基準にしてください。**

### 1-1. Power / Brake / Reverser は「上書きしない限り、最後に受け取った値をそのまま返す」

まとめ役は子プラグインを順に呼ぶとき、直前までの結果を `SetPower` / `SetBrake` / `SetReverser` として
子に伝えてから `Elapse` を呼び、**`Elapse` の戻り値でマージせずにそのまま置き換えます**
(例外: Multiplexer では `ConstantSpeed` だけは最大値が採用されます)。

- 介入しない項目は、受け取った値をそのまま返すこと。固定値(0 など)を返すと、運転士の操作や、前のプラグイン
  (ATC のブレーキなど)の判断を、黙って消してしまいます。単体で試したときは動いて見えるのに、併用した瞬間に壊れます。
- ひな形の `AtsLogic` は、`OnPowerChanged` / `OnBrakeChanged` / `OnReverserChanged` で受け取った値を保存し、
  `OnElapse` はそれをそのまま返します。ロジックを書くときは、**介入したい項目だけ**を書き換えて返します。
- **`OnInitialize`(シナリオ開始・駅へのジャンプ)で値を 0 に戻さないこと。** まとめ役(Multiplexer も DetailManager も)は `Set*` を、
  子に前回渡した値と違うときだけ送ります。`Initialize` のたびに送り直しはしません。BVE 本体も、`Initialize` のたびに
  現在のハンドル位置を送り直してくれるとは限りません。戻してしまうと、運転士がハンドルを動かすまで「レバーサ中立・力行 0・ブレーキ緩解」を
  返し続けます。ひな形は項目ごとに「`Load` してから一度でも受け取ったか」(`_powerReceived` など)を覚え、
  **まだ受け取っていない項目だけ**初期値を入れます。受け取った印は `OnLoad` で消します(読み込み直しでは、
  Multiplexer が最初の `Elapse` の前に全部送り直すため)。
- 非常ブレーキを指示するときは、`AtsHandles.Brake` に `int.MaxValue` のような範囲外の値を入れて BVE の丸めに期待しては
  いけません(実機で「インデックスが配列の境界外です」のエラーになりました)。`OnVehicleSpec` で受け取った
  `BrakeNotches` を保存し、非常ブレーキには **`BrakeNotches + 1`** を返します(ひな形はそうなっています)。
- **介入するときも、受け取った値より弱くしない。** たとえばブレーキに介入するなら、`Math.Max(受け取った値, 自分の値)` にします。
  前のプラグインが非常ブレーキ(`BrakeNotches + 1`)を指示していたら、それを保つためです。
- **介入した値を `_lastHandles` に書き戻さない。** 「受け取った値」として残り続けてしまいます(まとめ役は `SetBrake` を、値が変わった
  ときしか送り直さないので、介入をやめても元に戻りません)。`_lastHandles` の複製(構造体なのでそのまま代入すれば複製されます)を
  書き換えて返します。書き方の例は `AtsLogic.OnElapse` のコメントにあります。
- **並び順の注意**: 後ろのプラグインの戻り値が前のものを置き換えます。ただし後ろのプラグインがこの決まりを守っていれば、
  受け取った値(=前の介入の結果)をそのまま返すので、前の介入は保たれます。決まりを守らないプラグインが後ろにいると、
  並べ替えても他の介入を消してしまうので、そのプラグイン側を直してください。

### 1-2. Panel / Sound 配列は全プラグインが同じメモリを直接共有する

`Elapse` に渡される `panel` / `sound` のポインタは、Multiplexer が各子プラグインへそのまま渡します。マージはありません。
複数のプラグインが同じ番号に書くと、後から呼ばれた方の値で上書きされます。

- ひな形の `AtsLogic.PanelIndexMin/Max`・`SoundIndexMin/Max` で、プラグインごとに書いてよい範囲を宣言します。
  **既定は空の範囲(`Min = 1`・`Max = 0`)**で、設定しないと何も書き込めません。設定し忘れたときに、他のプラグインの番号を
  黙って上書きするのではなく、「何も出ない」ことで気づけるようにするためです。`NativeIntArray` は範囲外への書き込みをログに警告して無視します。
- **読む番号も宣言します。** ほかのプラグインが書いた値を読むだけの番号は `PanelReadIndexMin/Max`・`SoundReadIndexMin/Max` に
  書きます(書き込みの範囲は読むのにも使えます)。ホストはバッファの大きさを教えてくれないので、範囲外のメモリに触れると、例外では済まずに
  BVE ごと落ちるおそれがあります。宣言していない番号を読むと、警告を出して 0 を返します(警告は「panel/sound・番号・読み書きの別」ごとに 1 回だけ記録し、
  `Load` のたびにリセットします)。
- **番号の上限**: 通常の BVE 環境では、Panel / Sound の番号は原則として 0〜255 です。
  [AtsArrayExtender](https://www.okaoka-depot.com/contents/bve/AtsArrayExtender/)(入力デバイスプラグイン。単体で動作し、
  AtsEX 1.0-RC5 以降には拡張機能版が同梱)を入れた環境では、現行の公開版で 0〜1023 まで使えます。ATS プラグイン側の読み書きの方法は
  通常と同じです。ただし対応する BVE の版に制限があります(BVE5 は 5.8.7554.391 で本体の変更が必要、BVE6 は 6.0.7554.619。
  一部の RC 版では動作しない)。256 以上の番号は、導入する環境と BVE の版を確かめてから使い、拡張環境があるか分からないときは
  0〜255 にとどめます(出典: 上記リンク先)。このため、テンプレートのソースは範囲を一律 0〜255 に制限せず、プラグインごとの宣言範囲で縛っています。
  `tools/Smoke-Test.ps1` で 256 以上を宣言したプラグインを試すときは、`-PanelCapacity 1024 -SoundCapacity 1024` を付けます。
  使う番号は、車両の `panel.cfg` / `sound.cfg` を見て決めます。
  この範囲は「書いてよい上限」の宣言で、書く義務ではありません。Panel または Sound を**全く使わない**なら、
  既定の空の範囲(`Min = 1`・`Max = 0`)のままにします。
- 併用するプラグインと範囲が重ならないよう、車両の `panel.cfg` / `sound.cfg` を書く人と決めておきます。
  決め方は [6. Panel / Sound の番号の決め方](#6-panel--sound-の番号の決め方)。
- Panel の値は、`Elapse` のたびに(毎フレーム)書くのを標準にします。BVE が配列を毎フレーム初期化するのかどうかは確認していないので、
  値が変わったときだけ書く実装は、確かめるまで避けます(安全側の判断で、確認済みの事実ではありません)。

### 1-3. プラグイン自身が持つ状態の初期化

ドアの開閉やタイマーのような、プラグイン自身が持つ状態は、**`OnLoad` で初期化**します(読み込み直しで必ず初期状態に戻る)。
`OnInitialize`(シナリオ開始・駅へのジャンプ)では、BVE からの通知がまだ来ていない状態だけを初期化し、通知で更新される状態
(`DoorOpen` / `DoorClose` で更新するドアの開閉など)は**戻しません**。`DoorOpen` などの通知が `Initialize` の後に
送り直されるかどうかは、確認していません(確認していない BVE の挙動を、コードのコメントや README に断定で書かないでください)。

---

## 2. ビルド(DllExport)の落とし穴

C# のメソッドをネイティブ DLL のエクスポートにするため、ビルド時に IL を書き換える方法(DllExport)を使います。

### 2-1. 使うのは `DllExport`(3F 版)。`UnmanagedExports` は使わない

元祖の `UnmanagedExports`(`RGiesecke.DllExport`)は、.NET Framework 4.8 では `DllExportAppDomainIsolatedTask` が
`Version48` の `ArgumentException` で失敗しました(実機で確認済み)。メンテナンスされている
[3F/DllExport](https://github.com/3F/DllExport)(NuGet の id は `DllExport`)を使います。このひな形は最初からそうなっています。

- `DllExport.bat` をソリューションと同じフォルダに置き、ビルド時に `-action Restore` でパッケージを取得させる構成です。
- `[DllExport]` 属性は、`.csproj` の `DllExportNamespace` に書いた名前空間へ**自動生成**されます。
  そのため `using RGiesecke.DllExport;` は不要です(書くとコンパイルエラー)。
- 新しいプラグインを作るときは、`DllExportNamespace` を新しい名前空間に変え、`DllExportIdent` を新しい GUID にします。
- 別プラグインから `packages/` フォルダごとコピーして流用する場合は、`packages\DllExport.<版>\gcache\` を**削除**してください。
  残っていると、コピー元の名前空間向けのキャッシュを再利用して、`[DllExport]` 属性の型が見つからずコンパイルエラーになります。
- `DllExport.bat` のウィザード(`-action Configure` など)を再実行すると、`.csproj` の DllExport 関連ブロックが再生成され、
  下の `MSBuildRuntimeType` 条件が消えることがあります。消えたら付け直してください。

### 2-2. `<DebugType>none</DebugType>` が必須

SDK 形式の既定(ポータブル PDB)は、DllExport の ilasm 処理が読めず、`Export warning EXP0022: RunIlAsm: Possibly invalid PDB`
が出ます。**ビルドは成功するのに、エクスポートが 1 つも付かない DLL ができます**(BVE には読み込まれるが何も起きない)。
`.csproj` に `<DebugType>none</DebugType>` を入れると解消します(ひな形は設定済み)。

### 2-3. 実際に使う DLL は `x86\` / `x64\` のサブフォルダにある

1 回のビルドで両方のビット数が生成され、`src\<名前>\bin\<Platform>\Release\net48\` の直下ではなく、その下の `x86\` / `x64\` に
出力されます(直下にあるのはエクスポートの無い素のマネージド DLL)。使うのは次の 2 つです(リポジトリ直下からの相対パス)。

| 設定 | 使うファイル |
|---|---|
| Release\|x86 | `src\<名前>\bin\x86\Release\net48\x86\<名前>.dll` |
| Release\|x64 | `src\<名前>\bin\x64\Release\net48\x64\<名前>64.dll` |

(`Release|x86` の `...\x64\<名前>.dll` は中身は x64 ですが、ファイル名が `64` 付きでないので使いません。)
必ず `dumpbin /headers /exports <DLL>` で、ビット数(`14C machine (x86)` / `8664 machine (x64)`)と、次の 16 個のエクスポートを
確認します: `Load` `Dispose` `GetPluginVersion` `SetVehicleSpec` `Initialize` `Elapse` `SetPower` `SetBrake` `SetReverser`
`KeyDown` `KeyUp` `HornBlow` `DoorOpen` `DoorClose` `SetSignal` `SetBeaconData`。

**外部 NuGet パッケージの依存 DLL は、これらとは別の場所(`net48\` の直下)に出ます**([4-2](#4-2-naudio-など外部-nuget-パッケージを使う))。

`tools\Build-Check.ps1` が、ここまで(`vswhere` による MSBuild と `dumpbin` の発見、2 種類のビルド、ビット数と 16 個のエクスポートの確認)を
まとめて行います。`dumpbin` は Visual Studio 付属ですが PATH には入っていないので、手で呼ぶなら
`<VS のインストール先>\VC\Tools\MSVC\<版>\bin\Hostx64\x64\dumpbin.exe` を指定します(スクリプトは自分で探します)。

### 2-4. `dotnet build` は簡易チェック専用

`dotnet build` は Core 版の MSBuild で動くため、DllExport のタスクを読み込めません。ひな形の `.csproj` は、DllExport 関連の
`ImportGroup` / `Target` に `Condition="'$(MSBuildRuntimeType)' == 'Full'"` を付けて、`dotnet build` のときは丸ごと
スキップします。ただしそれだけだと `[DllExport]` 属性の型が見つからないので、`DllExportCompileCheckShim.cs`(スタブ属性)を
`dotnet build` のときだけコンパイル対象にしています。**BVE に渡す DLL は、必ず Visual Studio(または VS 付属の MSBuild.exe)でビルド**します。

### 2-5. その他のエラー

- `MSB1008` / `SDK "Microsoft.NET.Sdk" を解決できませんでした` — Visual Studio Installer で「.NET デスクトップ開発」を追加。
- `MSB3644: reference assemblies for .NETFramework,Version=v4.8 were not found` — 「個別のコンポーネント」から
  「.NET Framework 4.8 targeting pack」を追加。

---

## 3. C# の書き方で気をつけること

C# のコンパイルは通るのに、**エクスポートを埋め込む段階(ildasm → ilasm)で失敗する**書き方があります。

- **`double.NaN` の定数を書かない**(実機で確認済み)。`ldc.r8 -nan(ind)` を ilasm が読めません。「値なし」の印には
  `double?` などを使います。念のため `double.NegativeInfinity` などの無限大の定数も避けます。
- **ラムダの引数を `(_, _) =>` と書かない**(実機で確認済み)。同じ名前 `_` の引数が 2 つあるメソッドになり、ilasm が
  `Duplicate param name` で失敗します。`(sender, e) =>` のように名前を付けます。

---

## 4. 実行時の落とし穴

### 4-1. 例外を BVE に漏らさない

エクスポートした関数から例外が漏れると、プラグインだけでなく BVE 本体がクラッシュします。ひな形の `AtsExports` は
全エクスポートで例外を捕まえてログに書き、`AtsLogic` に処理を委譲します。この形を崩さないでください。

`Elapse` が例外になったときに返す値にも注意します。`default`(ブレーキ 0・力行 0・レバーサ 0)を返すと、`AtsLogic` が毎フレーム例外を
出す状態になったとき、運転士のハンドルや前のプラグインのブレーキを、毎フレーム消し続けることになります。ひな形の `AtsExports` は、
`SetPower` / `SetBrake` / `SetReverser` で受け取った値を `AtsLogic` とは別に覚えておき、例外のときはそれをそのまま返します(=介入しない)。
`SetBrake` がまだ来ていないうちは、`Initialize` の初期位置に合わせます(非常位置で始まるなら `BrakeNotches + 1`。段数は `SetVehicleSpec` で
`AtsExports` も受け取っています)。

**これは「パススルー」で、保安装置としての「フェイルセーフ」ではありません。** `AtsExports` の例外処理は、ネイティブのホスト(BVE 本体)へ
例外を漏らさないためのものです。例外のときに返すのは「受け取ったハンドルの値」なので、プラグインが例外の直前までかけていた
ブレーキの介入は外れます(安全な状態を保つ動作ではありません)。実際の ATS の機能を作るときは、例外のときにブレーキの介入を
**外す / 保つ / 非常ブレーキを指令する**のどれにするかを、そのプラグインの仕様としてユーザーと決めます。AI はこの方針を独断で変えないでください。

### 4-2. NAudio など外部 NuGet パッケージを使う

**なぜ特別な処理が要るか(BVE の実機で確認済み)**: プラグイン DLL はネイティブ側から `LoadLibrary` で読み込まれるため、
.NET の依存アセンブリの探索は「BVE 本体の実行ファイルのあるフォルダ」が基準になります。依存 DLL をプラグインと同じフォルダ
(車両フォルダ)に置いても、`System.IO.FileNotFoundException` で落ちます。

**ひな形には、その対策が最初から入っています。** `AtsExports.cs` の `RegisterResolver` が `AssemblyResolve` を登録し、
プラグイン自身のあるフォルダから依存 DLL を探します(依存パッケージが無いプラグインでは何も起きません)。
`AtsLogic` を作るより前に登録する必要があるため、静的フィールドの宣言順(上から順に初期化される)を使っています。
**`ResolverRegistered` と `Logic` の順番を入れ替えない**でください。依存 DLL を `lib` のようなサブフォルダにまとめたいときは、
`RegisterResolver` の探索先にそのフォルダも加えます。

**パッケージの足し方**: `.csproj` に `PackageReference` を足すだけです(「編集するのは `AtsLogic.cs` だけ」の例外です)。

```xml
<ItemGroup>
  <PackageReference Include="NAudio" Version="2.2.1" />
</ItemGroup>
```

DllExport とは共存でき、`tools\Build-Check.ps1`(`/restore` を付けてビルドする)でそのまま取得・ビルドできました(ネットワークが必要)。
メタパッケージの `NAudio` は 10 個ほどの DLL を出力しますが、`WaveOutEvent` と `WaveFileReader` を使う程度なら、実際に読み込まれたのは
`NAudio.Core` と `NAudio.WinMM` の 2 つだけでした。配布物を小さくしたいなら、必要な個別パッケージだけを参照する手もあります。

**外部パッケージの型は、1 つの専用クラスに閉じ込める**ことをお勧めします(例: `AlertPlayer`)。`AtsLogic` のフィールドや
メソッドに NAudio の型を直接出さないと、依存 DLL が無いときの失敗がそのクラスを使う場面だけに限られ、原因も追いやすくなります。

**依存 DLL の出力先と配布**: 依存 DLL は `src\<名前>\bin\<Platform>\Release\net48\` の**直下**に出ます。エクスポート付きの本体 DLL は
その下の `x86\` / `x64\` サブフォルダなので、**別々の場所に出る**ことになります。本体 DLL のあるフォルダに依存 DLL が無いと、
`AssemblyResolve` が見つけられません。配布物は `tools\Build-Check.ps1 -Stage <出力先>` で、1 つのフォルダに集められます
(`<名前>.dll`、`<名前>64.dll`、依存 DLL、`<名前>.ini.sample`)。wav など自分のデータと、パッケージのライセンス文は、手で足します。
古い依存 DLL が残らないよう、`-Stage` は毎回フォルダを空にしてから集めます。空にするのは `-Stage` が作ったフォルダ
(隠しファイル `.staged-by-Build-Check` があるもの)だけで、それ以外の空でないフォルダを指定すると、何も消さずに失敗します。
`.staged-by-Build-Check` は配布物には要りません。
配布物の構成(どのファイルを車両フォルダのどこに置くか)は、README に書いてください。

**依存 DLL が見つからないときの症状**: BVE には何のエラーも出ず、そのプラグインの機能だけが動きません。ログが有効なら、
`[WARN] 依存DLLが見つかりません: <アセンブリ名>` と、`[ERROR] <メソッド名>: System.IO.FileNotFoundException ...` が `%TEMP%\<名前>.log` に残ります
(配布版のようにログを止めていると、何も残りません。5 を参照)。

**BVE を使わずに確かめる**: `tools\Smoke-Test.ps1` が、配布の形に置いた DLL を BVE と同じように `LoadLibrary` で読み込み、
`Load` / `KeyDown` / `Elapse` / `Dispose` などを呼んで、依存 DLL が本体のフォルダから読み込まれたかを表示します
(x86 は 32bit の PowerShell、x64 は 64bit の PowerShell で行います)。PowerShell のフォルダが BVE のフォルダの代わりになるので、
依存 DLL を `AssemblyResolve` なしには見つけられない状況を再現できます。**これは BVE の中での確認ではありません。**

```bash
powershell -ExecutionPolicy Bypass -File tools\Build-Check.ps1 -Stage dist
powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1 -Dir dist -KeyDown 0 -Require NAudio.Core
```

`-KeyDown` に渡した ATS キーの番号を `KeyDown` / `KeyUp` で送り、`-Require` に挙げたアセンブリが本体のフォルダから読み込まれていなければ失敗にします。
`Elapse` に渡す Panel / Sound の配列は 256 個(0〜255)です。`AtsLogic` で宣言した範囲(読むだけの範囲も含む)がこれを超えると、
`Elapse` を呼ばずに失敗にします。**範囲を確かめられないとき**(`AtsLogic` や範囲の定数 8 個のどれかが見つからない)も、
同じく `Elapse` を呼ばずに失敗にします(確かめられないものは通さない)。空の範囲(`Min > Max`)は確認済みとして扱います。256 以上の番号を使うプラグイン(1-2 の AtsArrayExtender の環境向け)は、`-PanelCapacity 1024 -SoundCapacity 1024` を付けて試します。

### 4-3. `Dispose` は複数回・複数スレッドから呼ばれうる(実機で確認済み)

BVE5 は、シナリオ終了時とアプリケーション終了時の少なくとも 2 回 `Dispose` を呼ぶことがあり、ログでは
「1 回目の処理が終わる前に 2 回目が始まっていた」ことがありました。`WaveOutEvent`(NAudio)のような
スレッドセーフでないリソースを持つクラスは、複数回・複数スレッドからの `Dispose` / `Stop` に耐えるよう、
生成・使用・破棄をすべて同じロックの下で行い、破棄済みなら何もしないようにします。守らないと、BVE 終了時に
「オブジェクト参照がオブジェクト インスタンスに設定されていません」のようなエラーで落ちます。

```csharp
private readonly object _gate = new();
private WaveOutEvent _output;   // 生成・使用・破棄は必ず _gate の中で

// 再生する。再生中なら、止めて最初から鳴らし直す(生成も破棄も、同じロックの中で行う)。
public void Play(string path)
{
    lock (_gate)
    {
        StopLocked();
        _output = new WaveOutEvent();
        _output.Init(new WaveFileReader(path));
        _output.Play();
    }
}

public void Stop()
{
    lock (_gate) { StopLocked(); }
}

public void Dispose()
{
    lock (_gate) { StopLocked(); }   // 2 回目以降は何もしない(StopLocked が null を見て戻る)
}

private void StopLocked()   // _gate を取った状態で呼ぶこと
{
    if (_output == null) return;
    _output.Stop();
    _output.Dispose();
    _output = null;
}
```

- `Dispose` の後に、同じプロセスで `Load` が呼ばれることがあるのかは確認していません。`Dispose` の後も(`Load` で)使い直せる形にしておくと安全です
  (上の例は `_output` が `null` に戻るだけなので、そのまま再利用できます)。
- 再生機器の「再生終了」などのコールバックは別のスレッドで呼ばれうるので、その中で同じロックを取って後始末をするのは避けるのが無難です
  (ロックの取り合いになる恐れがある、という推測で、試してはいません)。

### 4-4. ログの置き場所

BVE は `Program Files` 配下にあることが多く、標準ユーザーはそこに書き込めません。ひな形の `AtsDebug` は
`%TEMP%` 直下に `<アセンブリ名>.log` を書きます(実機で確認済み)。

### 4-5. ATS キーが BVE のどのキーか

`KeyDown` / `KeyUp` には ATS キーの番号が渡されます(`AtsConstants.KeyS = 0`、`KeyA1 = 1`、…)。この番号が BVE のキーボードの
どのキーに割り当たるかは、BVE の設定によるのかを含めて確認していません。キー入力を使うプラグインの README には、
「どのキーかは BVE の設定による(未確認)」のように、確認していないと分かる書き方をしてください。

### 4-6. 車両の状態の単位(圧力は kPa)

`AtsVehicleState` の単位は、BVE 公式の[保安装置プラグインの構造体の仕様](https://bvets.net/jp/edit/formats/vehicle/atsstructs.html)に
従います: 位置 m、速度 km/h、時刻 ms、圧力(BC・MR・ER・BP・SAP)kPa、電流 A。公開ヘッダ(`atsplugin.h`)の圧力のコメントは Pa と
書かれていますが、構造体の仕様は kPa です。Pa からの換算などはしないでください(実際の値の大きさは、このリポジトリでは測っていません)。

- **速度の符号**: 後退したときの符号は確認していません。速度の大きさだけが要る判定(速度超過など)では `Math.Abs` を使って構いません。
  前進か後退かの区別が要る機能では、符号を不用意に捨てず、対象の BVE で確かめる項目にします。
- **時刻(ms)で経過時間を計算するとき**: 値が周回する(日付をまたぐ、など)・オーバーフローすることを考えに入れます
  (例: 差が負になったら周回したとみなす、`int` のまま足し引きして溢れさせない)。どの値で周回するかは確認していません。

---

## 5. ログのオン/オフ

不具合を調べるためのログ(`AtsDebug`)は、ini の `[Log] Enabled` で止められるようにしておきます。
**他の人に配布するときは、ログを止めた状態で配布します。**

- ini は DLL と同じフォルダの `<プラグイン名>.ini`。名前はアセンブリ名の末尾の「`64`」を除いたもので、x86 版・x64 版で共有します。
- **ini が無い・`[Log]` の項目が無いときは出力します**(開発中は何も置かなくてよく、配布先で ini の置き忘れや名前違いがあっても
  ログで気づけます)。`false` / `0` / `no` / `off` のときだけ止まります。止めているときはログファイルを作らず、例外ログも書きません。
- 配布物には `<プラグイン名>.ini.sample`(`[Log] Enabled = false`)を入れます。開発中に実機で調べるときは、自分の ini で `Enabled = true` にします。
- 読み込みは最初のログ出力時と `AtsLogic.OnLoad` の `AtsDebug.Reload()` のときです。ini を書き換えたら、シナリオを読み込み直せば反映されます。
- 独自の設定項目を増やすときは、`AtsConfig`(`ReadDouble` / `ReadInt` / `ReadBool` / `ReadString`)で `AtsLogic.OnLoad` から読みます。
  独自の設定のセクション名は `[<プラグイン名>]` にします(`[Log]` はログの設定用に決まっているので使いません)。
  `.ini.sample` に、独自設定の書き方の例(コメントアウトしたもの)が入っています。
  ini が無い・項目が無い・値が正しくないときは、渡した既定値が返ります(例外は投げません)。`AtsDebug` も同じ `AtsConfig` で
  `[Log]` だけを自分で読みます(他の設定の初期化より前に出るログにも効かせるため)。
- **外部パッケージに依存するプラグイン**は、配布版(ログを止めたもの)で依存 DLL を置き忘れると、手がかりが何も残りません。
  「止めているときは何も書かない」という約束は守ったまま、README に「何も起きないときは、ini を `[Log] Enabled = true` にして
  `%TEMP%\<名前>.log` を見る。依存 DLL が無いと `[WARN] 依存DLLが見つかりません` が出る」と書いて案内します。
- 毎フレーム出すような大量のログは、`AtsDebug.Enabled` で先に判定して、止めているときは集計もしないようにします。
- ログのファイル名はアセンブリ名から自動で決まるので、複数のプラグインを同じ車両で動かしてもログが衝突しません。

---

## 6. Panel / Sound の番号の決め方

複数のプラグインを併用するとき、Panel / Sound の番号は車両の `panel.cfg` / `sound.cfg` とプラグイン間で共有される
1 つの空間です([1-2](#1-2-panel--sound-配列は全プラグインが同じメモリを直接共有する))。

- プラグインごとに、**使う範囲を最初に決めて**ドキュメントに書き、`AtsLogic.PanelIndexMin/Max`・`SoundIndexMin/Max` に反映します。
- 車両側の表示(時刻表や速度計など)が使っている番号と重ならないようにします。重なるときだけ、プラグイン側の ini などで
  番号を変えられるようにしておくと、公開用の既定値を変えずに済みます。
- プラグイン同士で情報をやり取りしたいときは、Panel の値の変化で伝える方法があります(書く側が決めた番号に値を書き、
  読む側がその番号を読む)。書き手・読み手の両方で番号を ini から変えられるようにしておくと、車両ごとの調整が楽です。
