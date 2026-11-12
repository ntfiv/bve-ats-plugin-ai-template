# AI向けの指示(BVE5/6 ATSプラグインを作る・直す)

このリポジトリは、**AI(あなた)が C# で BVE5/BVE6 のネイティブ ATS プラグインを作る**ための土台です。
ユーザーは多くの場合、プラグインのコードを自分で書きません。あなたが書き、ユーザーが Visual Studio でビルドして
BVE で確かめます。人間向けの説明は [README.md](README.md)、落とし穴と理由は [docs/KNOWHOW.md](docs/KNOWHOW.md) です。
**作る・直す前に、まず `docs/KNOWHOW.md` を読んでください。**

## 前提

- プラグインは **ATS Plugin Multiplexer または DetailManager で他のプラグインと併用される**前提。単体で動いて見えても、
  併用で壊れる実装をしないこと。
- BVE5 は 32bit(x86 の DLL)、BVE6 は 64bit のみ(x64 の DLL)。32bit 版の BVE6 は存在しない。
- 編集するのは基本的に `src/<名前>/AtsLogic.cs`(と、必要なら補助クラスの新規ファイル)だけ。`AtsExports.cs`(配線役)の構造は変えない。
  例外は、外部 NuGet パッケージを使うときの `.csproj` への `PackageReference` の追加だけ。依存 DLL を探す仕組み
  (`AtsExports.cs` の `RegisterResolver`)は**最初から入っている**ので、足さない・順番を変えない。
  独自の設定(ini)を読むときは、`AtsConfig`(`ReadDouble` / `ReadInt` / `ReadBool` / `ReadString`)を使い、`AtsLogic.OnLoad` で読む。
  P/Invoke を自分で書き足さない。

## 新しいプラグインを頼まれたら(この順で進める)

1. **確認する**(足りない項目だけユーザーに聞く。依頼文で答えが出ているものは聞き直さない):
   - 何をするプラグインか(機能。**何を・どんなときに・どうする**かの中心が決まらないなら聞く)
   - 併用するプラグインと、使われている Panel / Sound の番号(**使ってよい範囲**を決めるため。勝手に決めない)
   - 対象の BVE(5 / 6 / 両方)
   - 配布するか、ライセンスと著作権者名(**未定で可**。未定なら README に「未定」と書く)

   依頼に無い**細部**(ini の既定値、ログの設定、内部のクラス構成など)は、聞かずに決め、README に明記し、最後の報告で挙げる。
   **ただし、次の「安全にかかわる仕様」は細部として扱わず、AI が「安全側」で決めない**(そのプラグインに関係するものだけ。
   依頼文で決まっていなければユーザーに聞くか、仕様として明示してもらう): ブレーキ介入の開始条件と解除条件(超過から復帰する条件など)、
   非常ブレーキの扱いと復帰条件、未知の地上子を受け取ったときの動作、信号の情報が得られないときの動作、後退時の動作、
   ブレーキに介入するプラグインで `Elapse` が例外になったときの動作(`docs/KNOWHOW.md` 4-1)。ATS では「安全側」が必ずしも
   「強いブレーキ」ではなく、AI が実在の保安装置の仕様を創作してしまうのを防ぐため。
   依頼されていない機能(Panel への出力の追加など)は足さない。
   依頼が「〜があれば」のように**任意**の機能を求めているときは、**既定値のままなら挙動が変わらない形**(例: しきい値の既定を
   「無効」にする)で足し、報告する。
2. **プロジェクトを作る**: 次を **PowerShell で**実行する(`-Name` は英数字と `_`、先頭は英字、**末尾を `64` にしない**)。
   手でフォルダをコピー・リネームしない。(Git Bash では `\` が消えるので、パスは `/` で書くか PowerShell を使う。)
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/New-Plugin.ps1 -Name <名前>
   ```
   このリポジトリの隣に `<名前>` フォルダができる(`-Destination` で場所を変えられる)。以降はそのフォルダで作業する。
   できたフォルダの `AGENTS.md` の冒頭には「ここは作成済みのプラグイン」という注記が入り、`tools/` には `Build-Check.ps1` と
   `Smoke-Test.ps1` だけが入る。
3. **`src/<名前>/AtsLogic.cs` に処理を書く。** あわせて `AtsLogic.PanelIndexMin/Max`・`SoundIndexMin/Max` を、1 で決めた範囲に設定する。
   - **既定は空の範囲(`Min = 1`・`Max = 0`)で、何も書き込めない。** Panel / Sound を使うなら必ず設定する
     (設定し忘れると何も出ない。他のプラグインの番号を上書きしないための既定)。使わないなら既定のままでよい。
   - ほかのプラグインが書いた番号を**読む**だけのときは、`PanelReadIndexMin/Max`・`SoundReadIndexMin/Max` に設定する
     (書き込みの範囲は読むのにも使える)。宣言していない番号を読むと 0 が返る。
   - この範囲は「書いてよい上限」の宣言で、書く義務ではない。**依頼されていない Panel / Sound の出力は追加しない。**
4. **ビルドと確認**(下の「ビルドと確認」)。
5. **`README.md`(新しいプラグインのもの)を埋める**: 機能、使う Panel/Sound の範囲、設定(ini)、決めた細部、ライセンス。
   コピーされた `LICENSE` の著作権者名(テンプレート作者のまま)は、ユーザーに確認して直すか、配布しないなら触れずに報告する。
   **外部パッケージを使うなら、さらに:**
   - README に**配布物の構成**(どのファイルを車両フォルダのどこに置くか。必須の依存 DLL と、wav などのデータ)を書く。
   - README に、「何も起きないときは、ini を `[Log] Enabled = true` にして `%TEMP%\<名前>.log` を見る(依存 DLL が無いと
     `[WARN] 依存DLLが見つかりません` が出る)」と書く(配布版はログを止めるため。`docs/KNOWHOW.md` 5)。
   - `THIRD-PARTY-NOTICES.md` を、実際に使うパッケージの一覧に書き換え、そのライセンス文を配布物に含める(手順は同ファイル)。
   - キー入力を使うなら、「どのキーかは BVE の設定による(未確認)」のように、確認していないと分かる書き方をする(KNOWHOW 4-5)。
6. **報告する**: 何を作ったか、どこまで確認したか、ユーザーがやること(Visual Studio でのビルド、BVE での確認)を分けて書く。
   依頼に無く自分で決めたことも挙げる。

既存のプラグインを直すときは、手順 3〜6 だけを行う。

## 必ず守ること

1. **Power / Brake / Reverser は、介入しない項目は最後に受け取った値をそのまま返す。** 固定値(0 など)を返さない。
   `OnInitialize` でも 0 に戻さない(まだ受け取っていない項目だけ初期値を入れる。`_powerReceived` などの形に合わせる)。
   **介入するときも**、受け取った値より弱くしない(ブレーキなら `Math.Max(受け取った値, 自分の値)`)。介入した値を
   `_lastHandles` に書き戻さない(書き方は `AtsLogic.OnElapse` のコメントと `docs/KNOWHOW.md` 1-1)。
   **定速制御(`ConstantSpeed`)に介入しないときは `AtsConstants.ConstantSpeedContinue` のままにする。** `Enable` / `Disable` は、
   ユーザーが定速制御への介入を求めたときだけ使う。
2. **Panel / Sound の番号は、読むのも書くのも、`AtsLogic` で宣言した範囲にとどめる**(書き込みは `PanelIndexMin/Max`・
   `SoundIndexMin/Max`、読むだけの番号は `PanelReadIndexMin/Max`・`SoundReadIndexMin/Max`)。ホストのバッファの大きさは
   分からないので、範囲外のメモリに触れない。範囲はユーザーと決める(勝手に決めない)。Panel の値は、`Elapse` のたびに
   (毎フレーム)書くのを標準にする(BVE が配列を毎フレーム初期化するかは確認していないため、安全側)。
3. **非常ブレーキは `BrakeNotches + 1`**(`OnVehicleSpec` で受け取った段数を使う)。`int.MaxValue` を返さない。
4. **エクスポートから例外を漏らさない。** `AtsExports.cs` の try/catch を外さない。`Elapse` が例外になったときは、
   `AtsExports` が受け取ったハンドルの値をそのまま返す(=介入しない)ようになっている。固定値を返す形に戻さない。
5. **`Dispose` は複数回・複数スレッドから呼ばれうる前提で書く。** 2 回目以降は何もしない形にし、破棄するリソースは
   生成・使用・破棄を同じロックの下で扱う。`Dispose` の後に `Load` が来ても使い直せる形にする(`docs/KNOWHOW.md` 4-3)。
6. **ビルドの落とし穴を踏まない書き方をする:**
   - `double.NaN` / 無限大の定数を書かない(エクスポート埋め込みで失敗する)。「値なし」は `double?` を使う。
   - ラムダの引数を `(_, _) =>` と書かない(`Duplicate param name`)。`(sender, e) =>` のように名前を付ける。
   - `.csproj` の `<DebugType>none</DebugType>` を消さない(消すとエクスポートが付かない DLL ができる)。
   - `DllExport` 3F 版を使う。`UnmanagedExports` に戻さない。`[DllExport]` 用の `using` は書かない。
7. **外部 NuGet パッケージ(NAudio など)を使うなら、`docs/KNOWHOW.md` の 4-2 を読む。** 依存 DLL を探す仕組みは入っている
   (足さない)。パッケージの型は 1 つの専用クラスに閉じ込める。依存 DLL は本体 DLL とは別の場所に出るので、配布物は
   `Build-Check.ps1 -Stage` で集め、`Smoke-Test.ps1` で確かめる。
8. **ログは ini の `[Log] Enabled` で止められるようにし、配布物にはログを止めた `.ini.sample` を入れる**(`AtsDebug` と `AtsConfig` をそのまま使う)。
9. **C# のコメントは日本語で書く**(このリポジトリの既存コメントに合わせる)。
10. **プラグイン自身の状態(ドアの開閉など)は `OnLoad` で初期化する。** `OnInitialize` では、BVE からの通知が来ていない状態だけを
   初期化し、通知で更新される状態は戻さない(`DoorOpen` などが `Initialize` の後に送り直されるかは確認していない)。
11. **確認していない BVE の挙動を、コードのコメントや README に断定で書かない。** 「BVE は〜から始まる」のような文は、
    実機やソースで確かめたものだけにする。確かめていないものは「未確認」と書く。
12. **車両の状態の単位**: `AtsVehicleState` の位置は m、速度は km/h、時刻は ms、圧力は kPa(BVE 公式の構造体の仕様による。
   `atsplugin.h` のコメントは Pa だが換算しない。`docs/KNOWHOW.md` 4-6)。速度の符号(後退時)は確認していない。速度の大きさだけが要る
   判定では `Math.Abs` を使ってよい。前進・後退の区別が要る機能では符号を不用意に捨てず、対象の BVE で確かめる項目としてユーザーに伝える。
   時刻(ms)で経過時間を計算するときは、値の周回・オーバーフローを考えに入れる。
13. **ATS のコールバック(`OnElapse` / `OnInitialize` / `OnBeaconData` / `OnKeyDown` / `OnKeyUp` など)は同期的に処理する。**
    `async` にしない(`async void` にしない、`Task` / `Task<T>` を返す形にしない)。`Task.Run` の投げっぱなしもしない。
    `Elapse` の処理は呼び出しの中で終わらせて、`AtsHandles` を返す。Panel / Sound(`NativeIntArray`)は、その呼び出しの間だけ使う
    (フィールドに保持してコールバックの後に使ったり、別のスレッドから触ったりしない)。バックグラウンドの処理が本当に必要なときは、
    スレッド安全性・キャンセル・`Dispose` での終了まで設計し、**ユーザーに確認してから**入れる(勝手に入れない)。

## ビルドと確認

- **`tools/Build-Check.ps1` を使う**(リポジトリ/プラグインのフォルダで、PowerShell から実行):
  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/Build-Check.ps1
  ```
  `vswhere` で Visual Studio の MSBuild と dumpbin を見つけ、Release|x86 と Release|x64 をビルドし、2 つの DLL のビット数と
  **16 個のエクスポート**を確認して、`RESULT: OK` / `RESULT: FAIL` を出す(終了コードは 0 / 1)。PATH の設定は要らない。
  出力は短い。ビルドが失敗したときだけ、ビルドのログ全文を出す(成功時にも見たいときは `-ShowBuildLog`)。
- **外部パッケージを使うとき**: `Build-Check.ps1 -Stage <出力先>` が、本体 DLL・依存 DLL・`.ini.sample` を 1 つのフォルダに集める。
  続けて `Smoke-Test.ps1` で、BVE を使わずに「配布の形に置いた DLL が、依存 DLL を見つけて動くか」を確かめる:
  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/Smoke-Test.ps1 -Dir <出力先> -KeyDown 0 -Require <依存アセンブリ名>
  ```
  x86 と x64 の両方を、それぞれ 32bit / 64bit の PowerShell で行い、`RESULT: OK` / `FAIL` を出す。BVE の中での確認ではない
  (報告には「BVE 内は未確認」と分けて書く)。`-KeyDown` はキー入力を使うときだけ。
- 手で行う場合: VS 付属の MSBuild.exe で、Release|x86 と Release|x64 の**両方**をビルドする(`/restore` を付ける)。
  `dotnet build` はコンパイルチェックにしかならず、BVE に渡せる DLL にならない。
- 実際に使うファイルは `src\<名前>\bin\<Platform>\Release\net48\<Platform>\` の**サブフォルダ**にある(直下ではない):
  `src\<名前>\bin\x86\Release\net48\x86\<名前>.dll` と `src\<名前>\bin\x64\Release\net48\x64\<名前>64.dll`。
- エクスポートが無い DLL は BVE に読み込まれても何も起きず、エラーも出ない。
- **あなたの環境でビルドできない・BVE を動かせないなら、できないと書く。** 「ビルドとエクスポートの確認まで」と
  「BVE での動作は未確認」を分けて報告する。動いたことにしない。
- 不具合の相談を受けたら、まず `%TEMP%\<プラグイン名>.log`(BVE6 は `<プラグイン名>64.log`)をユーザーに見せてもらう。
  ログは UTF-8(BOM なし)。Windows PowerShell 5.1 で読むときは `Get-Content -Encoding UTF8` を使う。

## やらないこと

- このリポジトリやプラグインに、個人のパス・メールアドレス・他人の著作物(車両データ、音声など)・BVE 本体のファイルを入れない。
- `DllExport.bat` を削除・手編集しない(ビルド時の復元に使う)。
- テンプレート(`src/BveAtsPlugin`)を、特定のプラグインの実装で書き換えない。プラグインは `New-Plugin.ps1` で別フォルダに作る。
