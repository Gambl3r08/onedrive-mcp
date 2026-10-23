using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OneDriveMcp.Graph;

namespace OneDriveMcp.Tools;

[McpServerToolType]
public sealed class OneDriveReadTools(GraphClient graph, IOptions<OneDriveOptions> options)
{
    private readonly OneDriveOptions _options = options.Value;

    [McpServerTool(Name = "onedrive_status", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Returns the signed-in OneDrive for Business drive: owner, drive type and storage quota.")]
    public async Task<object> Status(CancellationToken ct)
    {
        var drive = await graph.GetAsync("me/drive?$select=id,driveType,owner,quota,webUrl", ct);
        var quota = drive.GetProperty("quota");
        return new
        {
            driveId = drive.GetProperty("id").GetString(),
            driveType = drive.GetProperty("driveType").GetString(),
            owner = drive.GetProperty("owner").GetProperty("user").GetProperty("displayName").GetString(),
            webUrl = drive.GetProperty("webUrl").GetString(),
            quotaUsedBytes = quota.GetProperty("used").GetInt64(),
            quotaTotalBytes = quota.GetProperty("total").GetInt64(),
            readOnlyMode = _options.ReadOnly,
        };
    }

    [McpServerTool(Name = "onedrive_list", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Lists the files and folders inside a OneDrive folder. Paths are relative to the drive root, e.g. 'Documents/Reports'. Empty path = root.")]
    public async Task<object> List(
        [Description("Folder path relative to the drive root. Empty for the root.")] string path = "",
        [Description("Maximum number of items to return (1-200).")] int top = 50,
        CancellationToken ct = default)
    {
        top = Math.Clamp(top, 1, 200);
        var page = await graph.GetAsync($"{DriveItems.ItemRef(path)}/children?$top={top}&$select={DriveItems.Select}", ct);
        var (items, hasMore) = DriveItems.ToPage(page);
        return new { path = DriveItems.Normalize(path), items, hasMore };
    }

    [McpServerTool(Name = "onedrive_search", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Searches the drive by file name and content. Returns matching files and folders with their paths.")]
    public async Task<object> Search(
        [Description("Search text.")] string query,
        [Description("Maximum number of results (1-100).")] int top = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("query must not be empty.");
        top = Math.Clamp(top, 1, 100);
        var escaped = Uri.EscapeDataString(query.Replace("'", "''"));
        var page = await graph.GetAsync($"me/drive/root/search(q='{escaped}')?$top={top}&$select={DriveItems.Select}", ct);
        var (items, hasMore) = DriveItems.ToPage(page);
        return new { query, items, hasMore };
    }

    [McpServerTool(Name = "onedrive_get_item", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Returns metadata (size, type, last modified, web URL) of a file or folder by path.")]
    public async Task<DriveItemDto> GetItem(
        [Description("Item path relative to the drive root.")] string path,
        CancellationToken ct = default)
    {
        var item = await graph.GetAsync($"{DriveItems.ItemRef(path)}?$select={DriveItems.Select}", ct);
        return DriveItems.ToDto(item);
    }

    [McpServerTool(Name = "onedrive_read_text", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Reads a text file (txt, md, csv, json, yaml, log, config...) as UTF-8. Binary files (docx, xlsx, pdf, images) are rejected. Output is capped in size.")]
    public async Task<object> ReadText(
        [Description("File path relative to the drive root.")] string path,
        [Description("Maximum bytes to read; capped by the server limit.")] int maxBytes = 0,
        CancellationToken ct = default)
    {
        var limit = maxBytes > 0 ? Math.Min(maxBytes, _options.MaxReadBytes) : _options.MaxReadBytes;
        var item = DriveItems.ToDto(await graph.GetAsync($"{DriveItems.ItemRef(path)}?$select={DriveItems.Select}", ct));
        if (item.IsFolder)
            throw new McpException($"'{path}' is a folder; use onedrive_list.");

        var (data, truncated) = await graph.DownloadAsync($"{DriveItems.ItemRef(path)}/content", limit, ct);
        if (Array.IndexOf(data, (byte)0) >= 0)
            throw new McpException($"'{path}' looks like a binary file ({item.MimeType}); only text files can be read.");

        return new
        {
            path = item.Path ?? DriveItems.Normalize(path),
            size = item.Size,
            truncated,
            content = Encoding.UTF8.GetString(data),
        };
    }
}
