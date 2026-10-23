using Microsoft.Identity.Client;

namespace OneDriveMcp.Auth;

// Persists the MSAL user token cache to a single file. The file contains the refresh
// token in plain form, so it must live on a volume only this container can read; on
// Unix it is written with mode 0600.
internal static class FileTokenCache
{
    private static readonly object Gate = new();

    public static void Bind(ITokenCache cache, string path)
    {
        cache.SetBeforeAccess(args =>
        {
            lock (Gate)
            {
                if (File.Exists(path))
                    args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(path));
            }
        });

        cache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, args.TokenCache.SerializeMsalV3());
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(tmp, path, overwrite: true);
            }
        });
    }
}
