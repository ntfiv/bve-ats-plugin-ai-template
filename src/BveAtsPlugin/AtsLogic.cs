using System;

namespace BveAtsPlugin;

/// <summary>
/// プラグインの挙動。BVE5にDLLをロードしなくても単体テストできるよう、ネイティブ
/// エクスポートの配線(AtsExports.cs)とは分離してある。このひな形は透過的な
/// パススルー実装で、プレイヤーや他デバイスが既に設定したハンドル値をそのまま転送する
/// だけで、それ以上は何もしない。以下のメソッドの中身を実際のATS/ATC/パネルロジックに
/// 置き換えること。
///
/// 重要: ATS Plugin Multiplexer(またはDetailManager)経由で他のプラグインと併用される場合、
/// Multiplexerは各子プラグインを順番に呼び出す際、直前の結果を`SetPower`/`SetBrake`/
/// `SetReverser`として子に伝えてから`Elapse`を呼び、その戻り値でそのまま(マージなしで)
/// 上書きする(`ConstantSpeed`だけ例外で最大値が採用される)。そのため`OnElapse`は、
/// 積極的に制御しないフィールドについては必ず`_lastHandles`をそのまま返さなければならない。
/// それ以外の値(0のような「無害」に見える値であっても)を返すと、運転士の実際のハンドル
/// 位置や他プラグインの判断を silently 上書きしてしまう。上書きしてよいのは実際に
/// 制御したいフィールドだけ。
/// </summary>
public sealed class AtsLogic
{
    // このプラグインが書き込んでよいPanel/Sound indexの範囲。複数プラグインをATS Plugin
    // Multiplexer経由で組み合わせる場合、全プラグインが同一のpanel[]/sound[]バッファを
    // 直接共有する(マージなし) - 各プラグインが直接書き込むため、indexが重複すると
    // 後から呼ばれたプラグインの値で上書きされてしまう。プラグインごとに重複しない範囲を
    // 車両のpanel.cfg/sound.cfg作者と取り決め、その上でこの範囲を設定すること。宣言した
    // 範囲外への書き込みはログに警告を出したうえで無視され、他プラグインのindexには触れない。
    // この範囲は「書いてよい上限」の宣言であって、書く義務ではない(使わない番号は書かなくてよい)。
    //
    // 既定は「空の範囲」(`Min = 1`・`Max = 0`)。設定し忘れたときに、他のプラグインの番号を上書きせず
    // 「何も出ない」(=すぐ気づく)ようにするため。PanelまたはSoundを使うときは、必ずここを設定する。
    public const int PanelIndexMin = 1;
    public const int PanelIndexMax = 0;
    public const int SoundIndexMin = 1;
    public const int SoundIndexMax = 0;

    // 書き込みはしないが「読む」番号の範囲(ほかのプラグインが書いた値を読むとき用。書き込みの範囲は、読むのにも使える)。
    // 範囲外の読み取りは0を返す(ホストのバッファの大きさは教えられないので、範囲外のメモリには触れない)。既定は空の範囲。
    public const int PanelReadIndexMin = 1;
    public const int PanelReadIndexMax = 0;
    public const int SoundReadIndexMin = 1;
    public const int SoundReadIndexMax = 0;

    private AtsHandles _lastHandles;

    // このプラグイン自身が持つ状態(ドアの開閉、タイマーなど)は、`OnLoad`で初期化する。`OnInitialize`
    // (シナリオ開始・駅へのジャンプ)では、BVEからの通知が来ていない状態だけを初期化し、通知で更新される状態は
    // 戻さない。`DoorOpen`などの通知が`Initialize`の後に送り直されるかは確認していない。確認していないBVEの挙動を、
    // コメントやREADMEに断定で書かないこと(「未確認」と書く)。

    // SetPower/SetBrake/SetReverserを、Loadしてから一度でも受け取ったか(項目ごと)。受け取った項目は、
    // Initialize(シナリオ開始・駅へのジャンプ)でも初期値に戻さない(OnInitializeを参照)。
    private bool _powerReceived;
    private bool _brakeReceived;
    private bool _reverserReceived;

    public void OnLoad()
    {
        // 読み込み直したら、ハンドルの値は受け取り直す(Multiplexerも読み込み直すと最初のElapseの前に全部送り直す)。
        _powerReceived = false;
        _brakeReceived = false;
        _reverserReceived = false;

        // ログのオン/オフ(iniの[Log] Enabled)を読み直す。iniを編集すれば、シナリオを読み込み直した
        // ときから反映される。
        AtsDebug.Reload();
    }

    public void OnDispose()
    {
    }

    // 常用ブレーキの段数。SetVehicleSpec経由で渡される。atsplugin.hの規約では、Brakeに
    // 有効な値は0(緩解)～BrakeNotches(常用最大)で、BrakeNotches+1が非常ブレーキを表す。
    // 以前は範囲外の値(int.MaxValue)を返せばBVE側で丸めてくれると想定していたが、実機
    // で「インデックスが配列の境界外です」エラーを起こすことが
    // 判明したため、必ずこの正しい値を使うこと。
    private int _brakeNotches;

    public void OnVehicleSpec(AtsVehicleSpec spec)
    {
        _brakeNotches = spec.BrakeNotches;
    }

    public void OnInitialize(int initialHandlePosition)
    {
        // ハンドルの値は、受け取った値をそのまま返し続ける。Initialize(シナリオ開始・駅へのジャンプ)で
        // 0などに戻してはいけない: ATS Plugin MultiplexerはSetPower/SetBrake/SetReverserを値が変わったときしか
        // 送り直さない(Multiplexer・DetailManagerのソースで確認できる)。BVE本体側も、Initializeのたびに現在のハンドル位置を
        // 送り直してくれるとは限らない。送り直される前提で書かず、いつも「最後に受け取った値」を保持する。戻すと、
        // 運転士がハンドルを動かすまで、レバーサ中立・ブレーキ緩解などを返し続け、運転士の操作や前のプラグイン
        // (ATCのブレーキなど)を上書きしてしまう。まだ一度も受け取っていない項目だけ、初期値を入れる。
        if (!_brakeReceived)
        {
            _lastHandles.Brake = initialHandlePosition == AtsConstants.InitEmergencyBrake ? _brakeNotches + 1 : 0;
        }

        if (!_powerReceived)
        {
            _lastHandles.Power = 0;
        }

        if (!_reverserReceived)
        {
            _lastHandles.Reverser = 0;
        }

        _lastHandles.ConstantSpeed = AtsConstants.ConstantSpeedContinue;
    }

    public AtsHandles OnElapse(AtsVehicleState state, NativeIntArray panel, NativeIntArray sound)
    {
        // 介入しないときは、受け取った値をそのまま返す。
        // 定速制御(ConstantSpeed)に介入しないときは、AtsConstants.ConstantSpeedContinueのままにする。
        // Enable / Disableは、ユーザーが定速制御への介入を求めたときだけ使う。
        //
        // この処理は呼び出しの中で同期的に終わらせて、AtsHandlesを返す(async・Task・Task.Runの投げっぱなしにしない)。
        // panel / sound(NativeIntArray)は、この呼び出しの間だけ使う(フィールドに保持したり、別のスレッドから触ったりしない)。
        //
        // 介入するときの書き方(例: 速度超過で常用ブレーキを最大にする):
        //   var handles = _lastHandles;                                   // 構造体なので複製される
        //   if (state.Speed > limit) handles.Brake = Math.Max(handles.Brake, _brakeNotches);
        //   return handles;                                               // _lastHandlesには書き戻さない
        // - 受け取った値より弱くしない(`Math.Max`)。前のプラグインが非常ブレーキ(`BrakeNotches + 1`)を
        //   指示していたら、それを保つ。
        // - `_lastHandles`に書き戻さない。書き戻すと、介入をやめた後も「受け取った値」として残り続ける
        //   (まとめ役は`SetBrake`を値が変わったときしか送り直さない)。
        return _lastHandles;
    }

    public void OnPowerChanged(int notch)
    {
        _lastHandles.Power = notch;
        _powerReceived = true;
    }

    public void OnBrakeChanged(int notch)
    {
        _lastHandles.Brake = notch;
        _brakeReceived = true;
    }

    public void OnReverserChanged(int position)
    {
        _lastHandles.Reverser = position;
        _reverserReceived = true;
    }

    public void OnKeyDown(int keyIndex)
    {
    }

    public void OnKeyUp(int keyIndex)
    {
    }

    public void OnHornBlow(int hornIndex)
    {
    }

    public void OnDoorOpen()
    {
    }

    public void OnDoorClose()
    {
    }

    public void OnSignalChanged(int signalIndex)
    {
    }

    public void OnBeaconData(AtsBeaconData beacon)
    {
    }
}
