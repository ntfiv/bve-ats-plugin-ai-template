using System;
using System.IO;
using System.Reflection;

namespace BveAtsPlugin;

/// <summary>
/// ベストエフォートのファイルロガー。BVE5はプラグインにコンソールもエラー報告手段も
/// 与えないため、クラッシュやサイレントな不具合の後に何が起きたか確認する唯一の手段。
/// 例外は投げない。BVE本体のフォルダではなく、現在のユーザーの一時フォルダに
/// 書き出す: BVE5/6はProgram Files配下にインストールされることが多く、標準ユーザーには
/// 書き込み権限がない - 実機で確認済み(Program Files配下にあるBVEでは書き込みが
/// 拒否され、UACの仮想化によるフォールバックも発生しなかった)。そのため作業ディレクトリ
/// 相対のパスではログが何も出力されず、「プラグインが一度も動いていない」状態と
/// 見分けがつかなくなる。一時フォルダはインストール場所に関わらず、常に現在のユーザーが
/// 書き込み可能。ログファイル名は固定文字列ではなくアセンブリ名から生成する
/// (BveAtsPlugin.log / BveAtsPlugin64.log) - このテンプレートからコピーしてリネームした
/// プラグインが自動的に自分専用のログファイルを持てるようにするため(複数プラグインを
/// ATS Plugin Multiplexer経由で併用する場合、そうしないとログが衝突してしまう)。
///
/// ログの出力は、DLLと同じフォルダのiniファイルの`[Log] Enabled`で止められる(オフのときはファイルを
/// 作らず、例外のログも含めて何も書かない)。iniのファイル名はアセンブリ名の末尾の「64」を除いたもの
/// (BveAtsPlugin.dll / BveAtsPlugin64.dll → BveAtsPlugin.ini)で、x86版とx64版で共有する。
/// ユーザーに配布するときは、サンプルini(`BveAtsPlugin.ini.sample`)の`Enabled = false`を同梱してログを止める。
/// iniが無い・`[Log]`の項目が無いときはオン(開発中は何もしなくてよく、また配布先でiniの置き忘れや
/// 名前の間違いがあっても、ログで気づけるように)。`false`/`0`/`no`/`off`のときだけ止まる。
/// 読み込みは最初のログ出力時と、`Load`の時(`Reload`)に行う。設定クラスより前に出るログにも効かせるため、
/// このクラス自身がiniの`[Log]`だけを読む。
/// </summary>
internal static class AtsDebug
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), $"{Assembly.GetExecutingAssembly().GetName().Name}.log");

    private static readonly object EnabledLock = new();
    private static bool _enabledLoaded;
    private static bool _enabled = true;

    /// <summary>ログを出力するか。初回に`[Log] Enabled`を読む。</summary>
    public static bool Enabled
    {
        get
        {
            lock (EnabledLock)
            {
                if (!_enabledLoaded)
                {
                    _enabled = ReadEnabledFromIni();
                    _enabledLoaded = true;
                }

                return _enabled;
            }
        }
    }

    /// <summary>iniの`[Log] Enabled`を読み直す(`Load`の時に呼ぶ。iniを編集すれば次の読み込みから反映される)。</summary>
    public static void Reload()
    {
        lock (EnabledLock)
        {
            _enabled = ReadEnabledFromIni();
            _enabledLoaded = true;
        }
    }

    // iniが無い・[Log] Enabledが無い・読めないときはtrue。false/0/no/offのときだけfalse。
    // iniの読み方は`AtsConfig`と共通。設定クラスなどの初期化より前に出るログにも効かせるため、
    // このクラスは自分で`[Log] Enabled`だけを読む。
    private static bool ReadEnabledFromIni() => AtsConfig.ReadBool("Log", "Enabled", true);

    public static void Log(string message)
    {
        try
        {
            if (!Enabled)
            {
                return;
            }

            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ログ出力自体がホストプロセスのクラッシュ原因になってはならない。
        }
    }

    public static void LogException(string context, Exception ex)
    {
        try
        {
            // 例外のToString()(文字列の組み立て)自体が例外を出すこともあるので、ここでも握りつぶす。
            Log($"[ERROR] {context}: {ex}");
        }
        catch
        {
            // ログ出力自体がホストプロセスのクラッシュ原因になってはならない。
        }
    }
}
