using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BveAtsPlugin;

/// <summary>
/// BVE5のATSプラグインABI(atsplugin.h)に対応するネイティブエントリポイント。素の
/// __stdcallエクスポートで、GetProcAddressによりこれらの名前(装飾なし)で検索される
/// (DllExportのビルドステップ - BveAtsPlugin.csprojの`DllExportNamespace`を参照。これが下で使う
/// `DllExportAttribute`をこのプロジェクト自身の名前空間に直接生成するため、ここで`using`は
/// 不要 - が各メソッドをそのままの名前でエクスポートする)。各エクスポートはすべての例外を
/// キャッチする - 例外がBVE5のネイティブ呼び出しスタックに漏れると、プラグインだけでなく
/// シミュレータ本体がクラッシュする。実際の挙動はすべてAtsLogicにあり、このクラスは
/// 相互運用の配線役に過ぎない。
///
/// 外部のNuGetパッケージ(NAudioなど)の依存DLLを、このDLL自身のあるフォルダから探す仕組み
/// (`AssemblyResolve`)も、ここに最初から入れてある。プラグインDLLはBVEにネイティブ側から読み込まれるため、
/// .NETの依存DLLの探索はBVE本体の実行ファイルのあるフォルダが基準になり、プラグインと同じフォルダに
/// 依存DLLを置いても見つからない。依存パッケージが無いプラグインでは、何も起きない。
/// </summary>
public static class AtsExports
{
    // 重要: 静的フィールドは書かれた順に初期化される。依存DLLを使う型は`AtsLogic`の中で初めて読み込まれるので、
    // 必ず`Logic`を作るより前に、この登録を行うこと(順番を入れ替えない)。
    private static readonly bool ResolverRegistered = RegisterResolver();

    private static readonly AtsLogic Logic = new();

    private static bool RegisterResolver()
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(AtsExports).Assembly.Location) ?? "";
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                try
                {
                    var name = new AssemblyName(e.Name).Name ?? "";
                    var path = Path.Combine(pluginDir, name + ".dll");
                    if (File.Exists(path))
                    {
                        return Assembly.LoadFrom(path);
                    }

                    // 言語別のリソースDLL(〜.resources)は無くて普通なので、記録しない。
                    if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    // 見つからないのは、依存DLLの置き忘れの可能性がある(ログが有効なときだけ記録される)。
                    AtsDebug.Log($"[WARN] 依存DLLが見つかりません: {e.Name} (探した場所: {pluginDir})");
                    return null;
                }
                catch
                {
                    return null;
                }
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void Load()
    {
        // 読み込み直したら、例外時に返すハンドルの値も受け取り直す(AtsLogicの_powerReceivedなどと同じ考え方)。
        _brakeReceived = false;
        NativeIntArray.ResetWarnings();
        try
        {
            Logic.OnLoad();
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(Load), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void Dispose()
    {
        try
        {
            Logic.OnDispose();
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(Dispose), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static int GetPluginVersion() => AtsConstants.PluginVersion;

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetVehicleSpec(AtsVehicleSpec spec)
    {
        _brakeNotches = spec.BrakeNotches;
        try
        {
            Logic.OnVehicleSpec(spec);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetVehicleSpec), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void Initialize(int initialHandlePosition)
    {
        // まだSetBrakeを受け取っていなければ、例外時に返すブレーキも初期位置に合わせる(非常位置で始まるときは非常)。
        // 受け取った後は戻さない(AtsLogic.OnInitializeと同じ規則)。
        if (!_brakeReceived)
        {
            _receivedBrake = initialHandlePosition == AtsConstants.InitEmergencyBrake ? _brakeNotches + 1 : 0;
        }

        try
        {
            Logic.OnInitialize(initialHandlePosition);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(Initialize), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static AtsHandles Elapse(AtsVehicleState state, IntPtr panel, IntPtr sound)
    {
        try
        {
            var panelArray = new NativeIntArray("panel", panel, AtsLogic.PanelIndexMin, AtsLogic.PanelIndexMax,
                AtsLogic.PanelReadIndexMin, AtsLogic.PanelReadIndexMax);
            var soundArray = new NativeIntArray("sound", sound, AtsLogic.SoundIndexMin, AtsLogic.SoundIndexMax,
                AtsLogic.SoundReadIndexMin, AtsLogic.SoundReadIndexMax);
            return Logic.OnElapse(state, panelArray, soundArray);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(Elapse), ex);

            // 固定値(0)を返すと、運転士のハンドルや前のプラグインの判断(ブレーキなど)を消してしまう。
            // AtsLogicの状態に頼らず、ここで受け取ったハンドルの値をそのまま返す(=介入しない)。
            return new AtsHandles
            {
                Brake = _receivedBrake,
                Power = _receivedPower,
                Reverser = _receivedReverser,
                ConstantSpeed = AtsConstants.ConstantSpeedContinue,
            };
        }
    }

    // Elapseが例外になったときに返す値。AtsLogicで例外が出ても失われないよう、AtsLogicとは別に、
    // SetPower/SetBrake/SetReverserで受け取った値をここでも覚えておく。
    private static int _receivedPower;
    private static int _receivedBrake;
    private static int _receivedReverser;
    private static bool _brakeReceived;
    private static int _brakeNotches;

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetPower(int notch)
    {
        _receivedPower = notch;
        try
        {
            Logic.OnPowerChanged(notch);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetPower), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetBrake(int notch)
    {
        _receivedBrake = notch;
        _brakeReceived = true;
        try
        {
            Logic.OnBrakeChanged(notch);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetBrake), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetReverser(int position)
    {
        _receivedReverser = position;
        try
        {
            Logic.OnReverserChanged(position);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetReverser), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void KeyDown(int keyIndex)
    {
        try
        {
            Logic.OnKeyDown(keyIndex);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(KeyDown), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void KeyUp(int keyIndex)
    {
        try
        {
            Logic.OnKeyUp(keyIndex);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(KeyUp), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void HornBlow(int hornIndex)
    {
        try
        {
            Logic.OnHornBlow(hornIndex);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(HornBlow), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void DoorOpen()
    {
        try
        {
            Logic.OnDoorOpen();
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(DoorOpen), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void DoorClose()
    {
        try
        {
            Logic.OnDoorClose();
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(DoorClose), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetSignal(int signalIndex)
    {
        try
        {
            Logic.OnSignalChanged(signalIndex);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetSignal), ex);
        }
    }

    [DllExport(CallingConvention = CallingConvention.StdCall)]
    public static void SetBeaconData(AtsBeaconData beacon)
    {
        try
        {
            Logic.OnBeaconData(beacon);
        }
        catch (Exception ex)
        {
            AtsDebug.LogException(nameof(SetBeaconData), ex);
        }
    }
}
