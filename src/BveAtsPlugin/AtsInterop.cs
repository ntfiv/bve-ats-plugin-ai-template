using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BveAtsPlugin;

/// <summary>
/// BVEの「ATSプラグイン」ネイティブインターフェース(atsplugin.h)で定義されている定数。
/// </summary>
public static class AtsConstants
{
    public const int PluginVersion = 0x00020000;

    // ATSキーのindex(KeyDown/KeyUpに渡される)。
    public const int KeyS = 0;
    public const int KeyA1 = 1;
    public const int KeyA2 = 2;
    public const int KeyB1 = 3;
    public const int KeyB2 = 4;
    public const int KeyC1 = 5;
    public const int KeyC2 = 6;
    public const int KeyD = 7;
    public const int KeyE = 8;
    public const int KeyF = 9;
    public const int KeyG = 10;
    public const int KeyH = 11;
    public const int KeyI = 12;
    public const int KeyJ = 13;
    public const int KeyK = 14;
    public const int KeyL = 15;

    // 初期ハンドル位置(Initializeに渡される)。
    public const int InitServiceBrake = 0;
    public const int InitEmergencyBrake = 1;
    public const int InitHandleRemoved = 2;

    // サウンド制御命令(Elapseからsound[]配列に書き込む)。
    public const int SoundStop = -10000;
    public const int SoundPlayOnce = 1;
    public const int SoundPlayLooping = 0;
    public const int SoundContinue = 2;

    // 警笛のindex(HornBlowに渡される)。
    public const int HornPrimary = 0;
    public const int HornSecondary = 1;
    public const int HornMusic = 2;

    // 定速制御命令(Elapseから返すAtsHandlesの一部)。
    public const int ConstantSpeedContinue = 0;
    public const int ConstantSpeedEnable = 1;
    public const int ConstantSpeedDisable = 2;
}

/// <summary>車両仕様。SetVehicleSpec経由で一度だけ渡される。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AtsVehicleSpec
{
    public int BrakeNotches;
    public int PowerNotches;
    public int AtsNotch;
    public int B67Notch;
    public int Cars;
}

/// <summary>
/// 車両の現在状態。Elapseに毎フレーム渡される。単位は BVE 公式の保安装置プラグイン構造体仕様による
/// (公開ヘッダ atsplugin.h の圧力のコメントは Pa だが、構造体仕様は kPa。Pa への換算などはしない):
/// 位置=m、速度=km/h、時刻=ms(0時からの経過)、圧力=kPa、電流=A。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AtsVehicleState
{
    /// <summary>列車の位置(m)。</summary>
    public double Location;

    /// <summary>
    /// 速度(km/h)。後退したときの符号は確認していない。符号に依存した判定をするときは、
    /// `Math.Abs`を使うか、実機で確かめること。
    /// </summary>
    public float Speed;

    /// <summary>時刻(ms)。0時からの経過時間。</summary>
    public int Time;

    /// <summary>ブレーキシリンダ圧力(kPa)。</summary>
    public float BcPressure;

    /// <summary>元空気ダメ圧力(kPa)。</summary>
    public float MrPressure;

    /// <summary>釣り合い空気ダメ圧力(kPa)。</summary>
    public float ErPressure;

    /// <summary>ブレーキ管圧力(kPa)。</summary>
    public float BpPressure;

    /// <summary>直通管圧力(kPa)。</summary>
    public float SapPressure;

    /// <summary>電流(A)。</summary>
    public float Current;
}

/// <summary>地上子(ビーコン)データ。地上子を通過した際にSetBeaconData経由で渡される。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AtsBeaconData
{
    public int Type;
    public int Signal;
    public float Distance;
    public int Optional;
}

/// <summary>運転指示。Elapseから毎フレーム返される。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AtsHandles
{
    public int Brake;
    public int Power;
    public int Reverser;
    public int ConstantSpeed;
}

/// <summary>
/// BVE5がElapseにpanel/sound状態として渡すネイティブint[]バッファへの薄いインデクサ。
/// ホスト側がバッファを所有し、プラグインにサイズを一切教えない。範囲外のメモリに触れると、
/// 例外では済まずにBVEごと落ちることがあるため、読み書きとも宣言した範囲に限る。
/// - 書き込みは[writeMin, writeMax]だけ: ATS Plugin Multiplexer経由で複数プラグインがこのバッファを共有する場合、
///   宣言範囲外への書き込みは他プラグインのpanel/sound状態を silently 壊してしまう。
/// - 読み取りは、書き込みの範囲か[readMin, readMax](ほかのプラグインが書いた番号を読むとき用)だけ。
/// - 負の番号と、ポインタが空(IntPtr.Zero)のときは、宣言範囲にかかわらず読み書きしない。
/// 範囲外の読み書きはログに警告を出して無視する(読み取りは0を返す)。警告は毎フレーム出続けないよう、
/// 「配列(panel/sound)・番号・読み書きの別」ごとに1回だけ記録する(`Load`のたびにリセット。種類は最大100まで)。
/// </summary>
public readonly struct NativeIntArray(string label, IntPtr pointer, int writeMin, int writeMax, int readMin, int readMax)
{
    private const int MaxWarningKinds = 100;
    private static readonly object WarnLock = new();
    private static readonly HashSet<string> Warned = new();

    /// <summary>範囲外の警告の記録をリセットする(`Load`の時に呼ぶ)。</summary>
    public static void ResetWarnings()
    {
        lock (WarnLock)
        {
            Warned.Clear();
        }
    }

    public int this[int index]
    {
        get
        {
            if (pointer == IntPtr.Zero)
            {
                Warn($"{label}:null", $"{label}のポインタが空(IntPtr.Zero)。読み取りは0を返し、書き込みは無視する。");
                return 0;
            }

            if (!InRange(index, writeMin, writeMax) && !InRange(index, readMin, readMax))
            {
                Warn($"{label}:read:{index}",
                    $"{label}のindex {index} の読み取りを拒否(" + (index < 0 ? "負の番号は宣言範囲にかかわらず拒否" :
                        $"宣言範囲 書き込み[{writeMin}, {writeMax}]・読み取り[{readMin}, {readMax}]の外") + ")。0を返す。");
                return 0;
            }

            return Marshal.ReadInt32(pointer, index * sizeof(int));
        }
        set
        {
            if (pointer == IntPtr.Zero)
            {
                Warn($"{label}:null", $"{label}のポインタが空(IntPtr.Zero)。読み取りは0を返し、書き込みは無視する。");
                return;
            }

            if (!InRange(index, writeMin, writeMax))
            {
                Warn($"{label}:write:{index}",
                    $"{label}のindex {index} への書き込みを拒否(" + (index < 0 ? "負の番号は宣言範囲にかかわらず拒否" :
                        $"このプラグインの書き込み範囲[{writeMin}, {writeMax}]の外") + ")。");
                return;
            }

            Marshal.WriteInt32(pointer, index * sizeof(int), value);
        }
    }

    // 負の番号は、宣言した範囲にかかわらず拒否する(範囲の定数を誤って負にしても、バッファの前に触れないように)。
    private static bool InRange(int index, int min, int max) => index >= 0 && index >= min && index <= max;

    private static void Warn(string key, string message)
    {
        lock (WarnLock)
        {
            if (Warned.Count >= MaxWarningKinds || !Warned.Add(key))
            {
                return;
            }

            var last = Warned.Count == MaxWarningKinds;
            AtsDebug.Log("[WARN] " + message + "(同じ番号の警告は、次の読み込みまで記録しない)" +
                (last ? "(範囲外の警告が多すぎるため、以降は記録しない)" : ""));
        }
    }
}
