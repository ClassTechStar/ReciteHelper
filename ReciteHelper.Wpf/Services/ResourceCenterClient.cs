using ReciteHelper.Wpf.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace ReciteHelper.Wpf.Services;

public sealed class ResourceCenterClient
{
    // Downloaded archives are unpacked and imported automatically, so cap the size
    // to bound both disk usage and the blast radius of a compromised server entry.
    public const long MaxDownloadBytes = 512L * 1024 * 1024;

    private readonly HttpClient _httpClient = new();

    public async Task<ResourceCenterSearchResult> SearchAsync(
        string serverUrl,
        int page,
        int pageSize,
        string? uploader,
        string? school,
        string? subject,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(serverUrl, "/api/resources", new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["uploader"] = uploader,
            ["school"] = school,
            ["subject"] = subject
        });

        return await _httpClient.GetFromJsonAsync<ResourceCenterSearchResult>(uri, cancellationToken)
            ?? new ResourceCenterSearchResult();
    }

    public async Task UploadAsync(
        string serverUrl,
        string filePath,
        string uploader,
        string school,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));
        content.Add(new StringContent(uploader.Trim()), "uploader");
        content.Add(new StringContent(school.Trim()), "school");
        content.Add(new StringContent(subject.Trim()), "subject");

        using var response = await _httpClient.PostAsync(BuildUri(serverUrl, "/api/resources"), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "上传失败。" : message);
        }
    }

    public async Task DownloadAsync(
        string serverUrl,
        ResourceCenterItem item,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (item.SizeBytes > MaxDownloadBytes)
            throw new InvalidOperationException(
                $"该资源大小（{item.SizeBytes / 1024 / 1024} MB）超过下载上限（{MaxDownloadBytes / 1024 / 1024} MB）。");

        var uri = BuildUri(serverUrl, $"/api/resources/{item.Id}/download");
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
            throw new InvalidOperationException("服务器报告的文件大小超过下载上限，已中止下载。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > MaxDownloadBytes)
                throw new InvalidOperationException("下载内容超过大小上限，已中止下载。");
            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static Uri BuildUri(string serverUrl, string path, IReadOnlyDictionary<string, string?>? query = null)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new InvalidOperationException("尚未配置资源中心服务器地址。");

        var builder = new UriBuilder(new Uri(new Uri(serverUrl.Trim().TrimEnd('/')), path));
        if (query is not null)
        {
            var queryText = string.Join("&", query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
            builder.Query = queryText;
        }

        return builder.Uri;
    }
}
