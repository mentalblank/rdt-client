using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.Internal;
using RdtClient.Service.Services;
using RdtClient.Service.Services.Usenet.Par2;
using RdtClient.Service.Services.Usenet.Par2.Packets;
using RdtClient.Service.Services.Usenet.Exceptions;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Queue.Steps;
using RdtClient.Service.Services.Usenet.Queue.FileProcessors;
using RdtClient.Service.Services.Usenet.Queue.Aggregators;
using RdtClient.Service.Services.Usenet.Utils;
using RdtClient.Service.Services.Usenet.Extensions;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;

namespace RdtClient.Service.Services.Usenet;

public class UsenetQueueManager(DataContext dataContext, INntpClient usenetClient, UsenetImportManager importManager, ILogger<UsenetQueueManager> logger)
{
    public async Task<IList<UsenetJob>> GetJobs()
    {
        return await dataContext.UsenetJobs
            .Include(j => j.Files)
            .ToListAsync();
    }

    public async Task<UsenetJob?> GetJobByHash(String hash)
    {
        return await dataContext.UsenetJobs
            .Include(j => j.Files)
            .FirstOrDefaultAsync(j => j.Hash == hash);
    }

    public async Task<String> AddNzbFile(Byte[] bytes, String fileName, String? category, Int32 priority)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(category)) category = null;
            
            logger.LogInformation($"AddNzbFile: {fileName}");
            var hash = BitConverter.ToString(SHA1.HashData(bytes)).Replace("-", "").ToLower();
            
            var settings = Settings.Get.Usenet;

            var existingJobByHash = await dataContext.UsenetJobs.FirstOrDefaultAsync(j => j.Hash == hash);
            if (existingJobByHash != null)
            {
                if (settings.DuplicateNzbBehavior == DuplicateNzbBehavior.MarkFailed)
                {
                    logger.LogInformation($"NZB file {fileName} already exists in database with hash {hash}. Marking as failed.");
                    throw new Exception($"Duplicate NZB: This NZB has already been added (Hash: {hash}).");
                }
                logger.LogInformation($"NZB file {fileName} already exists by hash, but behavior is DownloadAgain. Continuing with suffix logic.");
                hash = Guid.NewGuid().ToString().Replace("-", "");
            }

            using var nzbStream = new MemoryStream(bytes);
            var nzbDocument = await UsenetNzbDocument.LoadAsync(nzbStream);
            var nzbFiles = nzbDocument.Files.Where(x => x.Segments.Count > 0).ToList();

            if (nzbFiles.Count == 0)
            {
                throw new Exception("No valid files found in NZB.");
            }

            var archivePassword = FilenameUtil.GetNzbPassword(fileName) ??
                                nzbDocument.Metadata.GetValueOrDefault("password");

            // Step 1: Fetch first segments for metadata
            logger.LogInformation("Step 1: Fetching metadata for NZB files...");
            var segmentsWithMetadata = await UsenetFetchFirstSegmentsStep.FetchFirstSegments(
                nzbFiles, usenetClient, CancellationToken.None);

            // Step 2: Get PAR2 descriptors
            logger.LogInformation("Step 2: Probing PAR2 for real filenames...");
            var par2FileDescriptors = await UsenetGetPar2FileDescriptorsStep.GetPar2FileDescriptors(
                segmentsWithMetadata, usenetClient, CancellationToken.None);

            // Step 3: Get File Infos
            var fileInfos = UsenetGetFileInfosStep.GetFileInfos(segmentsWithMetadata, par2FileDescriptors);

            // Filter files based on settings
            fileInfos = fileInfos.Where(fi => ShouldIncludeFile(fi.FileName)).ToList();

            if (fileInfos.Count == 0)
            {
                throw new Exception("No valid files found in NZB after applying include/exclude filters.");
            }

            // Step 4: File Processing (Deep probing for RARs etc.)
            logger.LogInformation("Step 4: Processing files...");
            var processors = GetFileProcessors(fileInfos, usenetClient, archivePassword).ToList();
            var processingResultsAll = await processors
                .Select(x => x.ProcessAsync())
                .WithConcurrencyAsync(Settings.Get.Usenet.MaxDownloadConnections + 5)
                .GetAllAsync(CancellationToken.None);
            
            var processingResults = processingResultsAll
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();

            // Determine Job Name
            var parsedJobName = nzbDocument.Metadata.GetValueOrDefault("name") ?? nzbDocument.Metadata.GetValueOrDefault("title");
            var baseJobName = parsedJobName ?? Path.GetFileNameWithoutExtension(fileName);
            var finalJobName = baseJobName;

            // Collision handling for Job Name
            var existingJobByName = await dataContext.UsenetJobs.FirstOrDefaultAsync(j => j.JobName == finalJobName);
            if (existingJobByName != null)
            {
                if (settings.DuplicateNzbBehavior == DuplicateNzbBehavior.MarkFailed)
                {
                    throw new Exception($"Duplicate NZB: A job with name '{finalJobName}' already exists.");
                }
                else 
                {
                    for (var i = 2; i < 1000; i++)
                    {
                        var suffixedName = $"{baseJobName} ({i})";
                        if (!await dataContext.UsenetJobs.AnyAsync(j => j.JobName == suffixedName))
                        {
                            finalJobName = suffixedName;
                            break;
                        }
                    }
                }
            }

            // Create Job Record
            var job = new UsenetJob
            {
                UsenetJobId = Guid.NewGuid(),
                Hash = hash,
                JobName = finalJobName,
                NzbFileName = fileName,
                NzbContents = Encoding.UTF8.GetString(bytes),
                Category = category,
                Priority = priority,
                Added = DateTimeOffset.UtcNow,
                Status = TorrentStatus.Finished,
                TotalSize = fileInfos.Sum(fi => fi.FileSize ?? 0)
            };

            foreach (var result in processingResults)
            {
                if (result is UsenetFileProcessor.Result fr)
                {
                    job.Files.Add(new UsenetFile
                    {
                        UsenetFileId = Guid.NewGuid(),
                        Path = fr.FileName,
                        Size = fr.FileSize,
                        SegmentIdList = fr.NzbFile.GetSegmentIds()
                    });
                }
            }

            await dataContext.UsenetJobs.AddAsync(job);

            // Update DavItem Hierarchy
            var categoryFolder = await GetOrCreateCategoryFolder(category);
            var mountFolder = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = categoryFolder.Id,
                Name = finalJobName,
                Type = UsenetDavItem.UsenetItemType.Directory,
                Path = Path.Join(categoryFolder.Path, finalJobName),
                CreatedAt = DateTime.Now
            };
            dataContext.UsenetDavItems.Add(mountFolder);

            new UsenetRarAggregator(dataContext, mountFolder, false).UpdateDatabase(processingResults);
            new UsenetFileAggregator(dataContext, mountFolder, false).UpdateDatabase(processingResults);
            new UsenetSevenZipAggregator(dataContext, mountFolder, false).UpdateDatabase(processingResults);
            new UsenetMultipartMkvAggregator(dataContext, mountFolder, false).UpdateDatabase(processingResults);

            await dataContext.SaveChangesAsync();

            await importManager.CreateImports(mountFolder);

            logger.LogInformation($"Saved Usenet job {job.JobName} to database and virtual filesystem.");

            return job.Hash;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error in UsenetQueueManager.AddNzbFile: {ex.Message}");
            throw;
        }
    }

    private IEnumerable<UsenetBaseProcessor> GetFileProcessors(
        List<UsenetGetFileInfosStep.FileInfo> fileInfos,
        INntpClient usenetClient,
        String? archivePassword)
    {
        var groups = fileInfos
            .DistinctBy(x => x.FileName)
            .GroupBy(GetGroup);

        foreach (var group in groups)
        {
            if (group.Key == "7z")
            {
                yield return new UsenetSevenZipProcessor(group.ToList(), usenetClient, archivePassword, CancellationToken.None);
            }
            else if (group.Key == "rar")
            {
                foreach (var fileInfo in group)
                    yield return new UsenetRarProcessor(fileInfo, usenetClient, archivePassword, CancellationToken.None);
            }
            else if (group.Key == "multipart-mkv")
            {
                yield return new UsenetMultipartMkvProcessor(group.ToList(), usenetClient, CancellationToken.None);
            }
            else if (group.Key == "other")
            {
                foreach (var fileInfo in group)
                    yield return new UsenetFileProcessor(fileInfo, usenetClient, CancellationToken.None);
            }
        }

        String GetGroup(UsenetGetFileInfosStep.FileInfo x) => 
            FilenameUtil.Is7zFile(x.FileName) ? "7z" :
            x.IsRar || FilenameUtil.IsRarFile(x.FileName) ? "rar" :
            FilenameUtil.IsMultipartMkv(x.FileName) ? "multipart-mkv" : "other";
    }

    private async Task<UsenetDavItem> GetOrCreateCategoryFolder(String? category)
    {
        var parentId = UsenetDavItemConstants.ContentFolder.Id;
        var categoryName = category ?? "No Category";
        
        await EnsureStaticFolders();

        var folder = await dataContext.UsenetDavItems
            .FirstOrDefaultAsync(x => x.ParentId == parentId && x.Name == categoryName);

        if (folder == null)
        {
            var contentFolder = await dataContext.UsenetDavItems.FirstAsync(x => x.Id == parentId);
            folder = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Name = categoryName,
                Type = UsenetDavItem.UsenetItemType.Directory,
                Path = Path.Join(contentFolder.Path, categoryName),
                CreatedAt = DateTime.Now
            };
            dataContext.UsenetDavItems.Add(folder);
            await dataContext.SaveChangesAsync();
        }

        return folder;
    }

    private async Task EnsureStaticFolders()
    {
        if (!await dataContext.UsenetDavItems.AnyAsync(x => x.Id == UsenetDavItemConstants.Root.Id))
        {
            dataContext.UsenetDavItems.Add(UsenetDavItemConstants.Root);
        }
        if (!await dataContext.UsenetDavItems.AnyAsync(x => x.Id == UsenetDavItemConstants.NzbFolder.Id))
        {
            dataContext.UsenetDavItems.Add(UsenetDavItemConstants.NzbFolder);
        }
        if (!await dataContext.UsenetDavItems.AnyAsync(x => x.Id == UsenetDavItemConstants.ContentFolder.Id))
        {
            dataContext.UsenetDavItems.Add(UsenetDavItemConstants.ContentFolder);
        }
        await dataContext.SaveChangesAsync();
    }

    public async Task DeleteJob(String hash, Boolean deleteData)
    {
        var job = await GetJobByHash(hash);
        if (job != null)
        {
            if (deleteData)
            {
                dataContext.UsenetFiles.RemoveRange(job.Files);

                var mountFolder = await dataContext.UsenetDavItems
                    .FirstOrDefaultAsync(x => x.Name == job.JobName && x.Type == UsenetDavItem.UsenetItemType.Directory);
                if (mountFolder != null)
                {
                    dataContext.UsenetDavItems.Remove(mountFolder);
                }

                var settings = Settings.Get.Usenet;
                
                // Cleanup Symlinks/STRM
                var cleanupPath = settings.ImportStrategy == UsenetImportStrategy.Symlinks ? settings.SymlinkPath : settings.LibraryDirectory;
                if (!String.IsNullOrWhiteSpace(cleanupPath))
                {
                    try
                    {
                        var jobPath = Path.Combine(cleanupPath, job.Category ?? "", job.JobName);
                        if (Directory.Exists(jobPath))
                        {
                            Directory.Delete(jobPath, true);
                            logger.LogInformation($"Deleted import directory: {jobPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Error deleting imports for job {job.JobName}: {ex.Message}");
                    }
                }
            }
            dataContext.UsenetJobs.Remove(job);
            await dataContext.SaveChangesAsync();
        }
    }

    public async Task DeleteAllJobs(Boolean deleteData)
    {
        var allJobs = await dataContext.UsenetJobs.ToListAsync();
        foreach (var job in allJobs)
        {
            await DeleteJob(job.Hash, deleteData);
        }
    }

    private static Boolean ShouldIncludeFile(String filename)
    {
        var settings = Settings.Get.Usenet;
        
        if (!String.IsNullOrWhiteSpace(settings.IncludeRegex))
        {
            return Regex.IsMatch(filename, settings.IncludeRegex, RegexOptions.IgnoreCase);
        }

        if (!String.IsNullOrWhiteSpace(settings.ExcludeRegex))
        {
            return !Regex.IsMatch(filename, settings.ExcludeRegex, RegexOptions.IgnoreCase);
        }

        return true;
    }
}
