using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// Wine 注册表文件（user.reg / system.reg）的解析器。格式：
/// <code>
/// WINE REGISTRY Version 2
/// [Software\\miHoYo\\\x539f\x795e] 1700000000
/// "GENERAL_DATA_h2389025596"=hex:7b,22,...,\
///   00
/// "Screenmanager Resolution Width_h182942802"=dword:00000780
/// "name"="text"
/// @="default value"
/// "multi"=str(7):"a\0b\0"
/// </code>
/// 键名与字符串中 <c>\\</c>、<c>\"</c>、<c>\n</c> 等为转义，非 ASCII 字符写成 <c>\x539f</c>
/// </summary>
public sealed class WineRegistryFile
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _valueOrder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>全部键路径（相对文件根，user.reg 即相对 HKEY_CURRENT_USER）</summary>
    public IEnumerable<string> KeyPaths => _keys.Keys;

    public static WineRegistryFile Load(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Parse(reader);
    }

    public static WineRegistryFile Parse(TextReader reader)
    {
        var file = new WineRegistryFile();
        Dictionary<string, object>? current = null;
        List<string>? order = null;
        string? line;
        while ((line = ReadLogicalLine(reader)) != null)
        {
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var end = FindKeyEnd(line);
                if (end < 0) { current = null; continue; }
                var name = Unescape(line.AsSpan(1, end - 1));
                if (!file._keys.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    order = [];
                    file._keys[name] = current;
                    file._valueOrder[name] = order;
                }
                else
                {
                    order = file._valueOrder[name];
                }

                continue;
            }

            if (current == null || order == null)
            {
                continue;
            }

            if (TryParseValue(line, out var valueName, out var value))
            {
                if (!current.ContainsKey(valueName)) order.Add(valueName);
                current[valueName] = value;
            }
        }

        return file;
    }

    /// <summary>键是否存在</summary>
    public bool ContainsKey(string path) => _keys.ContainsKey(Normalize(path));

    /// <summary>键下的值名（文件中的顺序），键不存在时为 null</summary>
    public IReadOnlyList<string>? GetValueNames(string path) =>
        _valueOrder.TryGetValue(Normalize(path), out var o) ? o : null;

    public object? GetValue(string path, string name) =>
        _keys.TryGetValue(Normalize(path), out var k) && k.TryGetValue(name ?? "", out var v) ? v : null;

    private static string Normalize(string path) => path.Replace('/', '\\').Trim('\\');

    /// <summary>读取一行，合并以 <c>\</c> 结尾的续行（hex 数据）</summary>
    private static string? ReadLogicalLine(TextReader reader)
    {
        var line = reader.ReadLine();
        if (line == null) return null;
        if (!line.EndsWith('\\') || line.StartsWith('[')) return line;
        var sb = new StringBuilder(line, 0, line.Length - 1, line.Length * 4);
        while (true)
        {
            var next = reader.ReadLine();
            if (next == null) break;
            next = next.TrimStart();
            if (next.EndsWith('\\'))
            {
                sb.Append(next, 0, next.Length - 1);
                continue;
            }

            sb.Append(next);
            break;
        }

        return sb.ToString();
    }

    private static int FindKeyEnd(string line)
    {
        for (var i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == ']') return i;
        }

        return -1;
    }

    private static bool TryParseValue(string line, out string name, out object value)
    {
        name = "";
        value = null!;
        int pos;
        if (line[0] == '@')
        {
            pos = 1;
        }
        else if (line[0] == '"')
        {
            var end = FindStringEnd(line, 1);
            if (end < 0) return false;
            name = Unescape(line.AsSpan(1, end - 1));
            pos = end + 1;
        }
        else
        {
            return false;
        }

        if (pos >= line.Length || line[pos] != '=') return false;
        var data = line.AsSpan(pos + 1);

        if (data.StartsWith("\""))
        {
            var end = FindStringEnd(line, pos + 2);
            if (end < 0) return false;
            value = Unescape(line.AsSpan(pos + 2, end - pos - 2));
            return true;
        }

        if (data.StartsWith("dword:"))
        {
            value = unchecked((int)uint.Parse(data[6..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }

        if (data.StartsWith("str("))
        {
            // str(2):"..." 为 REG_EXPAND_SZ，str(7):"a\0b\0" 为 REG_MULTI_SZ
            var close = data.IndexOf(')');
            var type = int.Parse(data[4..close], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var q = pos + 1 + close + 2; // 跳过 '):' 到引号
            if (q >= line.Length || line[q] != '"') return false;
            var end = FindStringEnd(line, q + 1);
            if (end < 0) return false;
            var s = Unescape(line.AsSpan(q + 1, end - q - 1));
            value = type == 7 ? SplitMulti(s) : s;
            return true;
        }

        if (data.StartsWith("hex"))
        {
            var type = 3; // hex: 为 REG_BINARY
            var colon = data.IndexOf(':');
            if (colon < 0) return false;
            if (data[3] == '(')
            {
                var close = data.IndexOf(')');
                type = int.Parse(data[4..close], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            var bytes = ParseHex(data[(colon + 1)..]);
            value = type switch
            {
                1 or 2 => DecodeUtf16(bytes),                 // REG_SZ / REG_EXPAND_SZ 以 hex 存储时
                4 when bytes.Length >= 4 => BitConverter.ToInt32(bytes, 0),
                7 => SplitMulti(DecodeUtf16(bytes)),
                0xb when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
                _ => bytes,
            };
            return true;
        }

        return false;
    }

    private static string DecodeUtf16(byte[] bytes)
    {
        var s = Encoding.Unicode.GetString(bytes);
        var nul = s.IndexOf('\0');
        return nul >= 0 && s[(nul)..].All(c => c == '\0') ? s[..nul] : s.TrimEnd('\0');
    }

    private static string[] SplitMulti(string s) => s.Split('\0').Where(x => x.Length > 0).ToArray();

    private static byte[] ParseHex(ReadOnlySpan<char> s)
    {
        var list = new List<byte>(s.Length / 3 + 1);
        foreach (var range in s.Split(','))
        {
            var part = s[range].Trim();
            if (part.Length == 0) continue;
            list.Add(byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return list.ToArray();
    }

    private static int FindStringEnd(string line, int start)
    {
        for (var i = start; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '"') return i;
        }

        return -1;
    }

    /// <summary>反转义（与 Wine server/unicode.c 的 dump_strW 对应）</summary>
    public static string Unescape(ReadOnlySpan<char> s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }

            c = s[++i];
            switch (c)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'e': sb.Append('\x1b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'x':
                {
                    var v = 0;
                    var n = 0;
                    while (n < 4 && i + 1 < s.Length && Uri.IsHexDigit(s[i + 1]))
                    {
                        v = v * 16 + Convert.ToInt32(s[++i].ToString(), 16);
                        n++;
                    }

                    sb.Append(n == 0 ? 'x' : (char)v);
                    break;
                }
                case >= '0' and <= '7':
                {
                    var v = c - '0';
                    var n = 1;
                    while (n < 3 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '7')
                    {
                        v = v * 8 + (s[++i] - '0');
                        n++;
                    }

                    sb.Append((char)v);
                    break;
                }
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// 从 Wine 前缀的 user.reg 读取当前用户注册表（只读）。文件修改后（按修改时间）重新解析。
/// 注意 wineserver 运行时可能有尚未写回文件的改动（通常几秒内落盘）
/// </summary>
public sealed class WinePrefixUserRegistry : IUserRegistry
{
    private readonly string _file;
    private readonly object _gate = new();
    private WineRegistryFile? _cached;
    private DateTime _cachedTime;

    /// <param name="prefix">Wine 前缀目录（含 user.reg），为 null 时取 $WINEPREFIX，再退回 ~/.wine</param>
    public WinePrefixUserRegistry(string? prefix = null)
    {
        prefix ??= Environment.GetEnvironmentVariable("WINEPREFIX");
        if (string.IsNullOrEmpty(prefix))
        {
            prefix = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine");
        }

        Prefix = prefix;
        _file = System.IO.Path.Combine(prefix, "user.reg");
    }

    public string Prefix { get; }

    public IRegistryKey? OpenSubKey(string path)
    {
        var file = Load();
        return file != null && file.ContainsKey(path) ? new Key(file, path) : null;
    }

    private WineRegistryFile? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_file)) return null;
            var t = File.GetLastWriteTimeUtc(_file);
            if (_cached == null || t != _cachedTime)
            {
                _cached = WineRegistryFile.Load(_file);
                _cachedTime = t;
            }

            return _cached;
        }
    }

    private sealed class Key(WineRegistryFile file, string path) : IRegistryKey
    {
        public string Path { get; } = path;
        public IReadOnlyList<string> GetValueNames() => file.GetValueNames(Path) ?? [];
        public object? GetValue(string name) => file.GetValue(Path, name);
        public void Dispose() { }
    }
}
