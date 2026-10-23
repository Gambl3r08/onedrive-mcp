using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OneDriveMcp.Graph;

namespace OneDriveMcp.Tools;

// Registered only when OneDrive:ReadOnly is false. There is deliberately no delete tool.
[McpServerToolType]
public sealed class OneDriveWriteTools(GraphClient graph, IOptions<OneDriveOptions> options)
{
    private readonly OneDriveOptions _options = options.Value;

    [McpServerTool(Name = "onedrive_create_folder", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Creates a folder. Fails if an item with the same name already exists in the parent folder.")]
    public async Task<DriveItemDto> CreateFolder(
        [Description("Parent folder path relative to the drive root. Empty for the root.")] string parentPath,
        [Description("Name of the new folder.")] string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\']) >= 0)
            throw new McpException("name must be a single non-empty path segment.");

        var body = new Dictionary<string, object>
        {
            ["name"] = name.Trim(),
            ["folder"] = new { },
            ["@microsoft.graph.conflictBehavior"] = "fail",
        };
        return DriveItems.ToDto(await graph.SendJsonAsync(HttpMethod.Post, $"{DriveItems.ItemRef(parentPath)}/children", body, ct));
    }

    [McpServerTool(Name = "onedrive_upload_text", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Writes a UTF-8 text file. Parent folders are created as needed. Fails if the file exists unless overwrite is true.")]
    public async Task<DriveItemDto> UploadText(
        [Description("Target file path relative to the drive root, e.g. 'Reports/2026-10.md'.")] string path,
        [Description("Text content to write.")] string content,
        [Description("Replace the file if it already exists.")] bool overwrite = false,
        CancellationToken ct = default)
    {
        if (DriveItems.Normalize(path).Length == 0)
            throw new McpException("path must name a file.");

        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        if (bytes.Length > _options.MaxUploadBytes)
            throw new McpException($"Content is {bytes.Length} bytes; the limit is {_options.MaxUploadBytes}.");

        var upload = new ByteArrayContent(bytes);
        upload.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        var conflict = overwrite ? "replace" : "fail";
        var item = await graph.SendAsync(HttpMethod.Put,
            $"{DriveItems.ItemRef(path)}/content?@microsoft.graph.conflictBehavior={conflict}", upload, ct);
        return DriveItems.ToDto(item);
    }

    [McpServerTool(Name = "onedrive_move_item", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Moves and/or renames a file or folder. Provide newParentPath, newName, or both.")]
    public async Task<DriveItemDto> MoveItem(
        [Description("Current item path relative to the drive root.")] string path,
        [Description("Destination folder path (empty string = root). Omit to keep the current folder.")] string? newParentPath = null,
        [Description("New name. Omit to keep the current name.")] string? newName = null,
        CancellationToken ct = default)
    {
        if (newParentPath is null && string.IsNullOrWhiteSpace(newName))
            throw new McpException("Provide newParentPath, newName, or both.");
        if (newName is not null && newName.IndexOfAny(['/', '\\']) >= 0)
            throw new McpException("newName must be a single path segment.");

        var body = new Dictionary<string, object>();
        if (newParentPath is not null)
            body["parentReference"] = new { path = DriveItems.ParentReferencePath(newParentPath) };
        if (!string.IsNullOrWhiteSpace(newName))
            body["name"] = newName.Trim();

        return DriveItems.ToDto(await graph.SendJsonAsync(HttpMethod.Patch, DriveItems.ItemRef(path), body, ct));
    }

    [McpServerTool(Name = "onedrive_create_share_link", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Creates a sharing link for a file or folder. type: 'view' or 'edit'. scope: 'organization' (default) or 'anonymous' (only if the server allows it).")]
    public async Task<object> CreateShareLink(
        [Description("Item path relative to the drive root.")] string path,
        [Description("'view' or 'edit'.")] string type = "view",
        [Description("'organization' or 'anonymous'.")] string scope = "organization",
        CancellationToken ct = default)
    {
        if (type is not ("view" or "edit"))
            throw new McpException("type must be 'view' or 'edit'.");
        if (scope is not ("organization" or "anonymous"))
            throw new McpException("scope must be 'organization' or 'anonymous'.");
        if (scope == "anonymous" && !_options.AllowAnonymousLinks)
            throw new McpException("Anonymous links are disabled on this server (OneDrive__AllowAnonymousLinks).");

        var result = await graph.SendJsonAsync(HttpMethod.Post, $"{DriveItems.ItemRef(path)}/createLink", new { type, scope }, ct);
        var link = result.GetProperty("link");
        return new
        {
            path = DriveItems.Normalize(path),
            type = link.GetProperty("type").GetString(),
            scope = link.GetProperty("scope").GetString(),
            url = link.GetProperty("webUrl").GetString(),
        };
    }
}
