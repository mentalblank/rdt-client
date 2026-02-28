using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services;

namespace RdtClient.Service.Services.Usenet;

public class UsenetSymlinkManager(ILogger<UsenetSymlinkManager> logger)
{
    public void CreateSymlinks(UsenetJob job)
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
            var categoryPath = job.Category ?? "";
            var jobSymlinkPath = Path.Combine(symlinkRoot, categoryPath, job.JobName);
            var jobRclonePath = Path.Combine(rcloneRoot, categoryPath, job.JobName);

            if (!Directory.Exists(jobSymlinkPath))
            {
                Directory.CreateDirectory(jobSymlinkPath);
            }
            
            foreach (var file in job.Files)
            {
                var fileName = Path.GetFileName(file.Path);
                var symlinkPath = Path.Combine(jobSymlinkPath, fileName);
                var targetPath = Path.Combine(jobRclonePath, fileName);

                if (File.Exists(symlinkPath))
                {
                    File.Delete(symlinkPath);
                }

                logger.LogInformation($"Creating symlink: {symlinkPath} -> {targetPath}");
                
                File.CreateSymbolicLink(symlinkPath, targetPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error creating symlinks for job {job.JobName}: {ex.Message}");
        }
    }
}
