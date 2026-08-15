using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace UotanToolbox.Common;

/// <summary>
/// 从 GitHub 自动获取最新 Root 方案（Magisk / KernelSU）。
/// 支持 GitHub 镜像加速（ghproxy 等）。
/// </summary>
public static class RootDownloadHelper
{
    /// <summary>
    /// 常用 GitHub 镜像加速前缀（用户可选）。空表示直连。
    /// </summary>
    public static readonly string[] Mirrors =
    [
        "",
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://mirror.ghproxy.com/",
    ];

    public record ReleaseAsset(string Name, string DownloadUrl, long Size);

    /// <summary>
    /// 获取 Magisk 最新 release 的资产。返回 (版本号, assets)。
    /// </summary>
    public static async Task<(string? version, List<ReleaseAsset> assets)> GetMagiskLatestAsync(string mirror = "")
    {
        var (tag, assets) = await GetLatestReleaseAsync("topjohnwu", "Magisk", mirror);
        // 过滤出 .apk（app-debug.apk 或 Magisk-v*.apk 都可用，优先 Magisk-v*）
        var apks = assets.Where(a => a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)).ToList();
        return (tag, apks);
    }

    /// <summary>
    /// 获取 KernelSU 最新 release 的资产。返回 (版本号, assets)。
    /// </summary>
    public static async Task<(string? version, List<ReleaseAsset> assets)> GetKernelSuLatestAsync(string mirror = "")
    {
        var (tag, assets) = await GetLatestReleaseAsync("tiann", "KernelSU", mirror);
        return (tag, assets);
    }

    private static async Task<(string? tag, List<ReleaseAsset> assets)> GetLatestReleaseAsync(string owner, string repo, string mirror)
    {
        // 镜像通常只代理 release 下载 URL，不代理 api.github.com。
        // 因此 API（获取版本/资产列表）始终走直连；--mirror 仅用于 DownloadAsync 阶段。
        _ = mirror;
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "utoolbox");

        string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var resp = await client.GetAsync(apiUrl);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"从 GitHub 获取 {owner}/{repo} release 失败: HTTP {(int)resp.StatusCode}");
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var data = await JsonSerializer.DeserializeAsync<ReleaseJson>(stream);
        if (data == null) throw new HttpRequestException("release 响应为空");
        var assets = data.assets?
            .Select(a => new ReleaseAsset(a.name, a.browser_download_url, a.size))
            .ToList() ?? [];
        return (data.tag_name, assets);
    }

    /// <summary>
    /// 下载文件到本地路径。
    /// </summary>
    public static async Task DownloadAsync(string url, string destPath, string mirror = "", Action<long, long>? progress = null)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Add("User-Agent", "utoolbox");

        string effective = url;
        if (mirror.Length > 0 && !url.Contains(mirror))
            effective = mirror + url;

        using var resp = await client.GetAsync(effective, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = System.IO.File.Create(destPath);
        var buf = new byte[1024 * 256];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            read += n;
            progress?.Invoke(read, total);
        }
    }

    private class ReleaseJson
    {
        public string? tag_name { get; set; }
        public List<AssetJson>? assets { get; set; }
    }

    private class AssetJson
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public long size { get; set; }
    }
}