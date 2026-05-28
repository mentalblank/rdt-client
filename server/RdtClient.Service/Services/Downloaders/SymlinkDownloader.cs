using System.Diagnostics;
using RdtClient.Data.Enums;
using RdtClient.Service.Helpers;
using Serilog;

namespace RdtClient.Service.Services.Downloaders;

public class SymlinkDownloader(String uri, String destinationPath, String path, Provider? clientKind) : IDownloader
{
    private const Int32 MaxRetries = 30;

    private readonly CancellationTokenSource _cancellationToken = new();

    private readonly ILogger _logger = Log.ForContext<SymlinkDownloader>();
    public event EventHandler<DownloadCompleteEventArgs>? DownloadComplete;
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    public async Task<String> Download()
    {
        _logger.Debug($"Starting symlink resolving of {path} (uri = {uri}), writing to path: {destinationPath}");

        try
        {
            var filePath = new FileInfo(path);

            var rcloneMountPath = Settings.Get.DownloadClient.RcloneMountPath.TrimEnd('\\', '/');
            var searchSubDirectories = rcloneMountPath.EndsWith('*');
            rcloneMountPath = rcloneMountPath.TrimEnd('*').TrimEnd('\\', '/');

            if (!Directory.Exists(rcloneMountPath))
            {
                throw new($"Mount path {rcloneMountPath} does not exist!");
            }

            var fileName = filePath.Name;
            var fileExtension = filePath.Extension;
            var fileNameWithoutExtension = fileName.Replace(fileExtension, "");
            var pathWithoutFileName = path.Replace(fileName, "").TrimEnd('\\', '/');
            var searchPath = Path.Combine(rcloneMountPath, pathWithoutFileName);

            List<String> unWantedExtensions =
            [
                ".zip",
                ".rar",
                ".tar"
            ];

            if (unWantedExtensions.Any(m => fileExtension == m))
            {
                throw new($"Cant handle compressed files with symlink downloader");
            }

            DownloadProgress?.Invoke(this,
                                     new()
                                     {
                                         BytesDone = 0,
                                         BytesTotal = 0,
                                         Speed = 0
                                     });

            String? file = null;
            var shouldSearch = true;

            // When resolving symlinks for AllDebrid, we know the exact file path, so we can skip the search.
            if (clientKind == Provider.AllDebrid)
            {
                var potentialFilePath = Path.Combine(rcloneMountPath, path);

                // Make sure the file exists before making any assumptions.
                // If this somehow fails, fallback to the search below.
                if (File.Exists(potentialFilePath))
                {
                    _logger.Debug($"Found file {path} at {potentialFilePath} using direct search");
                    file = potentialFilePath;
                    shouldSearch = false;
                }
                else
                {
                    // Log if the file wasn't found and continue searching.
                    _logger.Warning($"Expected file {path} to be at {potentialFilePath} but it wasn't found. Continuing search (this will probably fail).");
                }
            }

            if (shouldSearch)
            {
                var potentialFilePaths = new List<String>();

                if (!String.IsNullOrWhiteSpace(pathWithoutFileName))
                {
                    potentialFilePaths.Add(pathWithoutFileName);
                }

                var directoryInfo = new DirectoryInfo(searchPath);

                while (directoryInfo.Parent != null && directoryInfo.FullName.TrimEnd('\\', '/') != rcloneMountPath)
                {
                    potentialFilePaths.Add(directoryInfo.Name);
                    directoryInfo = directoryInfo.Parent;
                }

                potentialFilePaths.Add(fileName);
                potentialFilePaths.Add(fileNameWithoutExtension);

                // add an empty path so we can check for the new file in the base directory
                potentialFilePaths.Add("");

                potentialFilePaths = potentialFilePaths.Distinct().ToList();

                var keywords = (pathWithoutFileName + " " + fileNameWithoutExtension)
                               .Split([' ', '.', '_', '-', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries)
                               .Where(k => k.Length >= 3) // Skip short words
                               .Where(k => !Int32.TryParse(k, out _)) // Skip numbers
                               .Where(k => !new[] { "the", "and", "remux", "bluray", "h264", "x264", "x265", "hevc" }.Contains(k.ToLower())) // Skip common tags
                               .Distinct()
                               .ToList();

                for (var retryCount = 0; retryCount < MaxRetries; retryCount++)
                {
                    DownloadProgress?.Invoke(this,
                                             new()
                                             {
                                                 BytesDone = retryCount,
                                                 BytesTotal = MaxRetries,
                                                 Speed = 1
                                             });

                    _logger.Debug($"Searching {rcloneMountPath} for {fileName} (attempt #{retryCount})...");

                    // First try the root mount path with all potential sub-paths
                    file = FindFile(rcloneMountPath, potentialFilePaths, fileName);

                    if (file == null && (retryCount == 1 || retryCount == 5) && !String.IsNullOrWhiteSpace(Settings.Get.General.RcloneRefreshCommand))
                    {
                        RefreshRclone();

                        // Wait a second for the mount to settle after refresh
                        await Task.Delay(1000);

                        // Re-check root after refresh before potentially going exhaustive
                        file = FindFile(rcloneMountPath, potentialFilePaths, fileName);
                    }

                    if (file == null && searchSubDirectories && retryCount >= 5)
                    {
                        var subDirectories = Directory.GetDirectories(rcloneMountPath, "*", SearchOption.TopDirectoryOnly);

                        // When searching subdirectories, we only want to use relative potential paths.
                        var relativePotentialPaths = potentialFilePaths.Where(p => !Path.IsPathRooted(p)).ToList();

                        foreach (var subDirectory in subDirectories)
                        {
                            var subDirectoryName = Path.GetFileName(subDirectory);

                            // Only check "relevant" subdirectories: those matching keywords or expected paths
                            var isRelevant = keywords.Count == 0 || 
                                             keywords.Any(k => subDirectoryName.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                                             potentialFilePaths.Any(p => p.TrimEnd('\\', '/').Equals(subDirectoryName, StringComparison.OrdinalIgnoreCase));

                            if (!isRelevant)
                            {
                                continue;
                            }

                            file = FindFile(subDirectory, relativePotentialPaths, fileName);

                            if (file != null)
                            {
                                break;
                            }
                        }
                    }

                    if (file == null)
                    {
                        await Task.Delay(1000 * Math.Min(retryCount, 10)); // Cap the backoff at 10s
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (file == null)
            {
                _logger.Debug($"Unable to find file in rclone mount. Folders available in {rcloneMountPath}: ");

                try
                {
                    var allFolders = FileHelper.GetDirectoryContents(rcloneMountPath);

                    _logger.Debug(allFolders);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex.Message);
                }

                throw new("Could not find file from rclone mount!");
            }

            _logger.Debug($"Creating symbolic link from {file} to {destinationPath}");

            var result = TryCreateSymbolicLink(file, destinationPath);

            if (!result)
            {
                throw new("Could not find file from rclone mount!");
            }

            DownloadComplete?.Invoke(this, new());

            return file;
        }
        catch (Exception ex)
        {
            DownloadComplete?.Invoke(this,
                                     new()
                                     {
                                         Error = ex.Message
                                     });

            throw;
        }
    }

    public Task Cancel()
    {
        _cancellationToken.Cancel(false);

        return Task.CompletedTask;
    }

    public Task Pause()
    {
        return Task.CompletedTask;
    }

    public Task Resume()
    {
        return Task.CompletedTask;
    }

    private String? FindFile(String rootPath, List<String> filePaths, String fileName)
    {
        foreach (var potentialFilePath in filePaths)
        {
            // Avoid redundant path construction (e.g., /TorrentName/TorrentName/File.mkv)
            if (!String.IsNullOrEmpty(potentialFilePath) && rootPath.TrimEnd('\\', '/').EndsWith(potentialFilePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var potentialFilePathWithFileName = Path.Combine(rootPath, potentialFilePath, fileName);

            _logger.Debug($"Searching {potentialFilePathWithFileName}...");

            if (File.Exists(potentialFilePathWithFileName))
            {
                return potentialFilePathWithFileName;
            }
        }

        return null;
    }

    private Boolean TryCreateSymbolicLink(String sourcePath, String symlinkPath)
    {
        try
        {
            File.CreateSymbolicLink(symlinkPath, sourcePath);

            if (File.Exists(symlinkPath)) // Double-check that the link was created
            {
                _logger.Information($"Created symbolic link from {sourcePath} to {symlinkPath}");

                return true;
            }

            _logger.Error($"Failed to create symbolic link from {sourcePath} to {symlinkPath}");

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error creating symbolic link from {sourcePath} to {symlinkPath}: {ex.Message}");

            return false;
        }
    }

    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    private void RefreshRclone()
    {
        if (String.IsNullOrWhiteSpace(Settings.Get.General.RcloneRefreshCommand))
        {
            return;
        }

        // Use a non-blocking check to see if a refresh is already in progress
        if (!RefreshLock.Wait(0))
        {
            _logger.Debug("An rclone refresh is already in progress, skipping redundant refresh.");
            return;
        }

        try
        {
            // Only refresh if the last one was more than 30 seconds ago
            if (DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromSeconds(30))
            {
                _logger.Debug("An rclone refresh was performed recently, skipping to avoid rate limits.");
                return;
            }

            var rclonePath = "rclone"; // Let the OS find it in the PATH
            
            var processInfo = new ProcessStartInfo
            {
                FileName = rclonePath,
                Arguments = Settings.Get.General.RcloneRefreshCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.Debug($"Executing rclone refresh: {rclonePath} {Settings.Get.General.RcloneRefreshCommand}");
            
            using var process = Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!String.IsNullOrWhiteSpace(output))
                    _logger.Debug($"rclone refresh output: {output}");
                
                if (!String.IsNullOrWhiteSpace(error))
                    _logger.Warning($"rclone refresh error output: {error}");
            }

            _lastRefresh = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to execute rclone refresh command: {ex.Message}");
        }
        finally
        {
            RefreshLock.Release();
        }
    }
}