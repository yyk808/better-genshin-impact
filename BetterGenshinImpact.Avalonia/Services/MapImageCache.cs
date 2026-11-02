using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Infrastructure.Network;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 地图点位的图标与详情图片（对应 WPF MapIconImageCache + RemoteImageService）：
/// 网络图片经文件缓存（Cache/Files 的“地图点位图标”“点位详情图片”分区，过期规则同 WPF）下载，用图片客户端（15 秒超时、20 MB 上限），
/// 本地图片（绝对路径或 file://）直接读；解码为 Avalonia 位图（Skia，支持 PNG / JPEG / WebP），图标在内存里最多留 512 个（最久未用的先移除）
/// </summary>
public static class MapImageCache
{
    private const int MaxImageBytes = 20 * 1024 * 1024;
    private const int MaxIcons = 512;

    private static IFileCache? _fileCache;
    private static IHttpClientFactory? _http;
    private static ILogger _log = Log.Logger;

    private static readonly object Lock = new();
    private static readonly Dictionary<string, LinkedListNode<(string Url, Bitmap Image)>> Index = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Url, Bitmap Image)> Recent = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Inflight = new(StringComparer.Ordinal);

    /// <summary>图标加载完成（参数为地址），在后台线程触发</summary>
    public static event EventHandler<string>? ImageUpdated;

    public static void Configure(IFileCache? fileCache, IHttpClientFactory? http, ILogger log)
    {
        _fileCache = fileCache;
        _http = http;
        _log = log;
    }

    /// <summary>已加载时返回图标，否则 null</summary>
    public static Bitmap? TryGet(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        lock (Lock)
        {
            if (!Index.TryGetValue(url, out var node)) return null;
            Recent.Remove(node);
            Recent.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <summary>加载图标，失败返回 null；同一图标只加载一次，ct 只取消本次等待</summary>
    public static Task<Bitmap?> GetAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
        if (TryGet(url) is { } cached) return Task.FromResult<Bitmap?>(cached);
        var task = Inflight.GetOrAdd(url, u => Task.Run(() => LoadIconAsync(u)));
        return task.WaitAsync(ct);
    }

    /// <summary>点位详情图片：每次重新解码（同 WPF 弹窗不进内存缓存），网络图片走文件缓存</summary>
    public static async Task<Bitmap?> LoadPointImageAsync(string url, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(CachePartitions.MapPointImages, url, ct).ConfigureAwait(false);
        return bytes is { Length: > 0 } ? Decode(bytes) : null;
    }

    private static async Task<Bitmap?> LoadIconAsync(string url)
    {
        try
        {
            var bytes = await GetBytesAsync(CachePartitions.MapIcons, url, CancellationToken.None).ConfigureAwait(false);
            if (bytes is not { Length: > 0 } || Decode(bytes) is not { } image) return null;
            lock (Lock)
            {
                if (Index.Remove(url, out var old)) Recent.Remove(old);
                Index[url] = Recent.AddFirst((url, image));
                while (Recent.Count > MaxIcons && Recent.Last is { } last)
                {
                    Recent.RemoveLast();
                    Index.Remove(last.Value.Url);
                }
            }

            ImageUpdated?.Invoke(null, url);
            return image;
        }
        catch (Exception e)
        {
            _log.Debug(e, "加载地图图标失败：{Url}", url);
            return null;
        }
        finally
        {
            Inflight.TryRemove(url, out _);
        }
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception e)
        {
            _log.Debug(e, "解码地图图片失败");
            return null;
        }
    }

    private static async Task<byte[]?> GetBytesAsync(CachePartition partition, string url, CancellationToken ct)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (_fileCache is null || _http is null) return null;
            return await _fileCache.GetOrAddBytesAsync(partition, uri.AbsoluteUri, token => DownloadAsync(uri, token), ct).ConfigureAwait(false);
        }

        // 本地图片：绝对路径或 file://（WPF 的 pack:// 资源在 Linux 上没有）
        var path = uri is { IsFile: true } ? uri.LocalPath : url;
        return Path.IsPathRooted(path) && File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
    }

    private static async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken ct)
    {
        var client = _http!.CreateClient(HttpClientNames.Images);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes) throw new InvalidDataException("图片大小超过 20 MB 限制。");
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaxImageBytes) throw new InvalidDataException("图片大小超过 20 MB 限制。");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
