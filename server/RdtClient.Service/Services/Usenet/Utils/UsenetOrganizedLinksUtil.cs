using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Extensions;

namespace RdtClient.Service.Services.Usenet.Utils;

/// <summary>
/// Note: In this class, a `Link` refers to either a symlink or strm file.
/// </summary>
public static class UsenetOrganizedLinksUtil
{
    private static readonly Dictionary<Guid, String> Cache = [];

    /// <summary>
    /// Searches organized media library for a symlink or strm pointing to the given target
    /// </summary>
    /// <param name="targetDavItem">The given target</param>
    /// <returns>The path to a symlink or strm in the organized media library that points to the given target.</returns>
    public static String? GetLink(UsenetDavItem targetDavItem)
    {
        if (Cache.TryGetValue(targetDavItem.Id, out var linkFromCache) && Verify(linkFromCache, targetDavItem))
        {
            return linkFromCache;
        }

        return SearchForLink(targetDavItem);
    }

    /// <summary>
    /// Enumerates all DavItemLinks within the organized media library that point to nzbdav dav-items.
    /// </summary>
    /// <returns>All DavItemLinks within the organized media library that point to nzbdav dav-items.</returns>
    public static IEnumerable<DavItemLink> GetLibraryDavItemLinks()
    {
        var settings = Settings.Get.Usenet;
        var libraryRoot = settings.LibraryDirectory;
        if (String.IsNullOrWhiteSpace(libraryRoot)) return [];

        var allSymlinksAndStrms = UsenetSymlinkAndStrmUtil.GetAllSymlinksAndStrms(libraryRoot);
        return GetDavItemLinks(allSymlinksAndStrms);
    }

    private static Boolean Verify(String linkFromCache, UsenetDavItem targetDavItem)
    {
        var mountDir = Settings.Get.Usenet.RcloneMountPath;
        var fileInfo = new FileInfo(linkFromCache);
        var symlinkOrStrmInfo = UsenetSymlinkAndStrmUtil.GetSymlinkOrStrmInfo(fileInfo);
        if (symlinkOrStrmInfo == null) return false;
        var davItemLink = GetDavItemLink(symlinkOrStrmInfo, mountDir);
        return davItemLink?.DavItemId == targetDavItem.Id;
    }

    private static String? SearchForLink(UsenetDavItem targetDavItem)
    {
        String? result = null;
        foreach (var davItemLink in GetLibraryDavItemLinks())
        {
            Cache[davItemLink.DavItemId] = davItemLink.LinkPath;
            if (davItemLink.DavItemId == targetDavItem.Id)
                result = davItemLink.LinkPath;
        }

        return result;
    }

    private static IEnumerable<DavItemLink> GetDavItemLinks
    (
        IEnumerable<UsenetSymlinkAndStrmUtil.ISymlinkOrStrmInfo> symlinkOrStrmInfos
    )
    {
        var mountDir = Settings.Get.Usenet.RcloneMountPath;
        return symlinkOrStrmInfos
            .Select(x => GetDavItemLink(x, mountDir))
            .Where(x => x != null)
            .Select(x => x!.Value);
    }

    private static DavItemLink? GetDavItemLink
    (
        UsenetSymlinkAndStrmUtil.ISymlinkOrStrmInfo symlinkOrStrmInfo,
        String mountDir
    )
    {
        return symlinkOrStrmInfo switch
        {
            UsenetSymlinkAndStrmUtil.SymlinkInfo symlinkInfo => GetDavItemLink(symlinkInfo, mountDir),
            UsenetSymlinkAndStrmUtil.StrmInfo strmInfo => GetDavItemLink(strmInfo),
            _ => throw new Exception("Unknown link type")
        };
    }

    private static DavItemLink? GetDavItemLink(UsenetSymlinkAndStrmUtil.SymlinkInfo symlinkInfo, String mountDir)
    {
        var targetPath = symlinkInfo.TargetPath;
        if (String.IsNullOrWhiteSpace(mountDir) || !targetPath.StartsWith(mountDir)) return null;
        
        targetPath = targetPath.RemovePrefix(mountDir);
        targetPath = targetPath.StartsWith('/') ? targetPath : $"/{targetPath}";
        if (!targetPath.StartsWith("/.ids")) return null;
        
        var guidStr = Path.GetFileNameWithoutExtension(targetPath);
        if (Guid.TryParse(guidStr, out var guid))
        {
            return new DavItemLink
            {
                LinkPath = symlinkInfo.SymlinkPath,
                DavItemId = guid,
                SymlinkOrStrmInfo = symlinkInfo
            };
        }
        return null;
    }

    private static DavItemLink? GetDavItemLink(UsenetSymlinkAndStrmUtil.StrmInfo strmInfo)
    {
        var targetUrl = strmInfo.TargetUrl;
        try 
        {
            var absolutePath = new Uri(targetUrl).AbsolutePath;
            // Matches the URL generated in UsenetImportManager.GetStrmTargetUrl
            // which currently is /api/usenet/webdav/download?path=...
            // But nzbdav used /view/.ids/...
            // For now let's support both if we can, or just our own.
            
            if (absolutePath.Contains("/.ids/"))
            {
                var guidStr = Path.GetFileNameWithoutExtension(absolutePath);
                if (Guid.TryParse(guidStr, out var guid))
                {
                    return new DavItemLink
                    {
                        LinkPath = strmInfo.StrmPath,
                        DavItemId = guid,
                        SymlinkOrStrmInfo = strmInfo
                    };
                }
            }
        }
        catch { /* ignored */ }
        
        return null;
    }

    public struct DavItemLink
    {
        public String LinkPath; // Path to either a symlink or strm file.
        public Guid DavItemId;
        public UsenetSymlinkAndStrmUtil.ISymlinkOrStrmInfo SymlinkOrStrmInfo;
    }
}
