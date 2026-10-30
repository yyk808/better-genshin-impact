using System.Text.Json;
using System.Text.Json.Nodes;

namespace BetterGenshinImpact.Host.Common.Config;

/// <summary>
/// 运行时覆盖（命令行开关、Linux 上强制 CPU 推理等）：只影响本次运行，保存配置时写回文件里原来的值。
/// <para>
/// 记录覆盖前后序列化结果中不同的叶子节点；保存时，若某个节点的当前值仍等于覆盖值，就换回覆盖前的值；
/// 用户之后在界面上改过的（当前值不等于覆盖值）照常保存。
/// </para>
/// </summary>
public sealed class RuntimeOverrides
{
    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = [];

    private sealed record Entry(string[] Path, JsonNode? FileValue, JsonNode? OverrideValue);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>覆盖的节点路径（如 autoPickConfig.enabled），用于日志和测试</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_lock)
            {
                return _entries.Select(e => string.Join('.', e.Path)).ToList();
            }
        }
    }

    /// <summary>比较覆盖前后的 JSON，登记发生变化的叶子节点</summary>
    public void Record(string before, string after)
    {
        var a = JsonNode.Parse(before);
        var b = JsonNode.Parse(after);
        var found = new List<Entry>();
        Diff(a, b, [], found);
        lock (_lock)
        {
            foreach (var entry in found)
            {
                // 同一节点多次覆盖：保留最初的文件值，更新覆盖值
                var index = _entries.FindIndex(e => e.Path.SequenceEqual(entry.Path));
                if (index >= 0)
                {
                    _entries[index] = _entries[index] with { OverrideValue = entry.OverrideValue };
                }
                else
                {
                    _entries.Add(entry);
                }
            }
        }
    }

    /// <summary>把仍等于覆盖值的节点换回文件里的值；没有覆盖时原样返回</summary>
    public string Apply(string json, JsonSerializerOptions options)
    {
        List<Entry> entries;
        lock (_lock)
        {
            if (_entries.Count == 0)
            {
                return json;
            }

            entries = [.. _entries];
        }

        var root = JsonNode.Parse(json);
        if (root is not JsonObject)
        {
            return json;
        }

        var changed = false;
        foreach (var entry in entries)
        {
            if (Navigate(root, entry.Path, out var parent, out var key) && parent is JsonObject obj
                && JsonNode.DeepEquals(obj[key], entry.OverrideValue))
            {
                obj[key] = entry.FileValue?.DeepClone();
                changed = true;
            }
        }

        return changed ? root.ToJsonString(options) : json;
    }

    private static void Diff(JsonNode? a, JsonNode? b, List<string> path, List<Entry> found)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var (key, value) in ob)
            {
                path.Add(key);
                if (oa.TryGetPropertyValue(key, out var old))
                {
                    Diff(old, value, path, found);
                }

                // 覆盖前不存在的字段（新增）不登记：没有可以写回的旧值
                path.RemoveAt(path.Count - 1);
            }

            return;
        }

        // 数组和标量整体比较
        if (path.Count > 0 && !JsonNode.DeepEquals(a, b))
        {
            found.Add(new Entry([.. path], a?.DeepClone(), b?.DeepClone()));
        }
    }

    private static bool Navigate(JsonNode root, string[] path, out JsonNode? parent, out string key)
    {
        parent = root;
        key = path[^1];
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (parent is not JsonObject obj || !obj.TryGetPropertyValue(path[i], out parent))
            {
                return false;
            }
        }

        return parent is JsonObject o && o.ContainsKey(key);
    }
}
