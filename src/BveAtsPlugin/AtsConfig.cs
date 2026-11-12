using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace BveAtsPlugin;

/// <summary>
/// プラグイン独自の設定(ini)を読む部品。iniは、DLLと同じフォルダの`<プラグイン名>.ini`
/// (アセンブリ名の末尾の「64」を除いたもの。BveAtsPlugin.dll / BveAtsPlugin64.dll → BveAtsPlugin.ini)で、
/// x86版とx64版で共有する。`AtsDebug`のログ設定(`[Log] Enabled`)も、この部品で読む。
///
/// 使い方: `AtsLogic.OnLoad`で読む(iniを編集すれば、シナリオを読み込み直したときから反映される)。
///   var limit = AtsConfig.ReadDouble("BveAtsPlugin", "SpeedLimit", 60.0);
/// 独自の設定のセクション名は`[プラグイン名]`にする(上の例なら`[BveAtsPlugin]`。新しいプラグインを作ると、
/// この名前はそのプラグインの名前に置き換わる)。`[Log]`はログの設定用なので使わない。
/// iniが無い・項目が無い・読めない・値が正しくないときは、渡した既定値を返す(例外は投げない)。
/// 値は半角英数字で書くこと(日本語の値は文字化けする)。行の途中の「 ;」以降はコメントとして無視する。
/// </summary>
internal static class AtsConfig
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder result, int size, string filePath);

    /// <summary>iniのパス。DLLと同じフォルダの`<プラグイン名>.ini`。</summary>
    public static string IniPath
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetName().Name ?? "";
            if (name.EndsWith("64", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - 2);
            }

            var dir = Path.GetDirectoryName(assembly.Location) ?? "";
            return Path.Combine(dir, name + ".ini");
        }
    }

    /// <summary>文字列を読む。iniが無い・項目が無い・読めないときは`defaultValue`。</summary>
    public static string ReadString(string section, string key, string defaultValue)
    {
        try
        {
            var path = IniPath;
            if (!File.Exists(path))
            {
                return defaultValue;
            }

            var buffer = new StringBuilder(512);
            GetPrivateProfileString(section, key, "", buffer, buffer.Capacity, path);
            var value = StripInlineComment(buffer.ToString());
            return value.Length == 0 ? defaultValue : value;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>小数を読む(小数点は「.」)。正しくない値(`NaN`・`Infinity` を含む)のときは`defaultValue`。</summary>
    public static double ReadDouble(string section, string key, double defaultValue)
    {
        var text = ReadString(section, key, "");
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return defaultValue;
        }

        return double.IsNaN(value) || double.IsInfinity(value) ? defaultValue : value;
    }

    /// <summary>整数を読む。正しくない値のときは`defaultValue`。</summary>
    public static int ReadInt(string section, string key, int defaultValue)
    {
        var text = ReadString(section, key, "");
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
    }

    /// <summary>真偽を読む。`false`/`0`/`no`/`off`は偽、`true`/`1`/`yes`/`on`は真(大文字小文字は問わない)。それ以外は`defaultValue`。</summary>
    public static bool ReadBool(string section, string key, bool defaultValue)
    {
        switch (ReadString(section, key, "").ToLowerInvariant())
        {
            case "false": case "0": case "no": case "off": return false;
            case "true": case "1": case "yes": case "on": return true;
            default: return defaultValue;
        }
    }

    // 行の途中の「 ;」以降をコメントとして取り除く(一般的なiniの書き方)。
    private static string StripInlineComment(string value)
    {
        if (value.TrimStart().StartsWith(";", StringComparison.Ordinal))
        {
            return "";
        }

        var comment = value.IndexOf(" ;", StringComparison.Ordinal);
        if (comment >= 0)
        {
            value = value.Substring(0, comment);
        }

        return value.Trim();
    }
}
