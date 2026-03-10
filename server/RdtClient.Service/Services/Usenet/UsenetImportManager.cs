using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services;
using RdtClient.Data.Models.Internal;
using Microsoft.EntityFrameworkCore;
using RdtClient.Data.Data;

namespace RdtClient.Service.Services.Usenet;

public class UsenetImportManager(ILogger<UsenetImportManager> logger, DataContext dataContext)
{
    public async Task CreateImports(UsenetDavItem mountFolder)
    {
        var settings = Settings.Get.Usenet;
        
        if (settings.ImportStrategy == UsenetImportStrategy.Symlinks)
        {
            await CreateSymlinks(mountFolder);
        }
        else if (settings.ImportStrategy == UsenetImportStrategy.StrmFiles)
        {
            await CreateStrmFiles(mountFolder);
        }
    }

    public async Task CreateSymlinks(UsenetDavItem mountFolder)
    {
        var settings = Settings.Get.Usenet;
        var symlinkRoot = settings.SymlinkPath;
        var rcloneRoot = settings.RcloneMountPath;

        if (String.IsNullOrWhiteSpace(symlinkRoot))
        {
            logger.LogWarning("Usenet Symlink Path is not set. Skipping symlink creation.");
            return;
        }

        if (String.IsNullOrWhiteSpace(rcloneRoot))
        {
            logger.LogWarning("Usenet Rclone Mount Path is not set. Skipping symlink creation.");
            return;
        }

        try
        {
            var children = await dataContext.UsenetDavItems
                .Where(x => x.ParentId == mountFolder.Id)
                .ToListAsync();

            // Recursively handle directories or just files in the mount folder
            await ProcessItemForSymlink(mountFolder, symlinkRoot, rcloneRoot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error creating symlinks for mount folder {mountFolder.Name}: {ex.Message}");
        }
    }

    private async Task ProcessItemForSymlink(UsenetDavItem item, String symlinkRoot, String rcloneRoot)
    {
        if (item.Type == UsenetDavItem.UsenetItemType.Directory)
        {
            var children = await dataContext.UsenetDavItems.Where(x => x.ParentId == item.Id).ToListAsync();
            foreach (var child in children)
            {
                await ProcessItemForSymlink(child, symlinkRoot, rcloneRoot);
            }
        }
        else
        {
            // It's a file
            var relativePath = item.Path.TrimStart('/');
            var symlinkPath = Path.Combine(symlinkRoot, relativePath);
            var targetPath = Path.Combine(rcloneRoot, relativePath);

            var directoryName = Path.GetDirectoryName(symlinkPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            if (File.Exists(symlinkPath))
            {
                File.Delete(symlinkPath);
            }

            logger.LogInformation($"Creating symlink: {symlinkPath} -> {targetPath}");
            File.CreateSymbolicLink(symlinkPath, targetPath);
        }
    }

    public async Task CreateStrmFiles(UsenetDavItem mountFolder)
    {
        var settings = Settings.Get.Usenet;
        var libraryDir = settings.LibraryDirectory;

        if (String.IsNullOrWhiteSpace(libraryDir))
        {
            logger.LogWarning("Usenet Library Directory is not set. Skipping STRM creation.");
            return;
        }

        try
        {
            await ProcessItemForStrm(mountFolder, libraryDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error creating STRM files for mount folder {mountFolder.Name}: {ex.Message}");
        }
    }

    private async Task ProcessItemForStrm(UsenetDavItem item, String libraryDir)
    {
        if (item.Type == UsenetDavItem.UsenetItemType.Directory)
        {
            var children = await dataContext.UsenetDavItems.Where(x => x.ParentId == item.Id).ToListAsync();
            foreach (var child in children)
            {
                await ProcessItemForStrm(child, libraryDir);
            }
        }
        else
        {
            // It's a file, only create STRM for video files
            if (!Utils.FilenameUtil.IsVideoFile(item.Name)) return;

            var relativePath = item.Path.TrimStart('/') + ".strm";
            var strmPath = Path.Combine(libraryDir, relativePath);

            var directoryName = Path.GetDirectoryName(strmPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            var targetUrl = GetStrmTargetUrl(item);
            await File.WriteAllTextAsync(strmPath, targetUrl);
            logger.LogInformation($"Created STRM file: {strmPath}");
        }
    }

    private String GetStrmTargetUrl(UsenetDavItem item)
    {
        // In rdt-client, we use the internal API for downloading
        // The URL format should match what's expected by the frontend/Plex
        var baseUrl = "http://rdtclient:6500"; 
        var pathUrl = Uri.EscapeDataString(item.Path.TrimStart('/'));
        return $"{baseUrl}/api/usenet/webdav/download?path={pathUrl}";
    }

    public async Task<Int32> ConvertStrmFilesToSymlinks()
    {
        var settings = Settings.Get.Usenet;
        var rcloneRoot = settings.RcloneMountPath;
        var libraryDir = settings.LibraryDirectory;

        if (String.IsNullOrWhiteSpace(rcloneRoot) || String.IsNullOrWhiteSpace(libraryDir))
        {
            return 0;
        }

        var convertedCount = 0;
        var strmLinks = Utils.UsenetOrganizedLinksUtil.GetLibraryDavItemLinks()
            .Where(x => x.SymlinkOrStrmInfo is Utils.UsenetSymlinkAndStrmUtil.StrmInfo)
            .ToList();

        foreach (var link in strmLinks)
        {
            var strmInfo = (Utils.UsenetSymlinkAndStrmUtil.StrmInfo)link.SymlinkOrStrmInfo;
            var extension = "mkv"; // Default
            
            try 
            {
                var uri = new Uri(strmInfo.TargetUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                extension = query.Get("extension") ?? "mkv";
            }
            catch { /* fallback */ }

            var davItem = await dataContext.UsenetDavItems.FirstOrDefaultAsync(x => x.Id == link.DavItemId);
            if (davItem == null) continue;

            var relativePath = davItem.Path.TrimStart('/');
            var symlinkPath = Path.Combine(settings.SymlinkPath, relativePath);
            var targetPath = Path.Combine(rcloneRoot, relativePath);

            var directoryName = Path.GetDirectoryName(symlinkPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            if (File.Exists(symlinkPath))
            {
                File.Delete(symlinkPath);
            }

            File.CreateSymbolicLink(symlinkPath, targetPath);
            File.Delete(strmInfo.StrmPath);
            convertedCount++;
        }

        return convertedCount;
    }
}
