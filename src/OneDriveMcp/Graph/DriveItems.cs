using System.Text.Json;
using ModelContextProtocol;

namespace OneDriveMcp.Graph;

public sealed record DriveItemDto(
    string Id,
    string Name,
    string? Path,
    bool IsFolder,
    long? Size,
    DateTimeOffset? LastModified,
    string? WebUrl,
    string? MimeType,
    int? ChildCount);

// Path addressing and projection helpers for items in the signed-in user's drive.
public static class DriveItems
{
    public const string Select = "id,name,size,lastModifiedDateTime,webUrl,file,folder,parentReference";

    private const string RootPrefix = "/drive/root:";

    // "" or "/" -> drive root; "Docs/a b.txt" -> me/drive/root:/Docs/a%20b.txt:
    public static string ItemRef(string? path)
    {
        var segments = Segments(path);
        return segments.Length == 0
            ? "me/drive/root"
            : "me/drive/root:/" + string.Join('/', segments.Select(Uri.EscapeDataString)) + ":";
    }

    // Value for parentReference.path when moving an item.
    public static string ParentReferencePath(string? path)
    {
        var segments = Segments(path);
        return segments.Length == 0 ? RootPrefix : RootPrefix + "/" + string.Join('/', segments);
    }

    public static string Normalize(string? path) => string.Join('/', Segments(path));

    private static string[] Segments(string? path)
    {
        var segments = (path ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(s => s is "." or ".."))
            throw new McpException("Path segments '.' and '..' are not allowed.");
        return segments;
    }

    public static DriveItemDto ToDto(JsonElement item)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        string? path = null;
        if (item.TryGetProperty("parentReference", out var parent) &&
            parent.TryGetProperty("path", out var parentPath) &&
            parentPath.GetString() is { } p && p.StartsWith(RootPrefix, StringComparison.Ordinal))
        {
            var folder = Uri.UnescapeDataString(p[RootPrefix.Length..]).Trim('/');
            path = folder.Length == 0 ? name : folder + "/" + name;
        }

        var isFolder = item.TryGetProperty("folder", out var folderFacet);
        return new DriveItemDto(
            Id: item.GetProperty("id").GetString() ?? string.Empty,
            Name: name,
            Path: path,
            IsFolder: isFolder,
            Size: item.TryGetProperty("size", out var size) ? size.GetInt64() : null,
            LastModified: item.TryGetProperty("lastModifiedDateTime", out var modified) ? modified.GetDateTimeOffset() : null,
            WebUrl: item.TryGetProperty("webUrl", out var url) ? url.GetString() : null,
            MimeType: item.TryGetProperty("file", out var file) && file.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null,
            ChildCount: isFolder && folderFacet.TryGetProperty("childCount", out var count) ? count.GetInt32() : null);
    }

    public static (IReadOnlyList<DriveItemDto> Items, bool HasMore) ToPage(JsonElement page) =>
        (page.GetProperty("value").EnumerateArray().Select(ToDto).ToList(),
         page.TryGetProperty("@odata.nextLink", out _));
}
