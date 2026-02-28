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
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text;

namespace RdtClient.Service.Services.Usenet;

public class UsenetQueueManager(DataContext dataContext, INntpClient usenetClient, ILogger<UsenetQueueManager> logger)
{
    private static readonly HashSet<String> VideoExtensions =
    [
        ".webm", ".m4v", ".3gp", ".nsv", ".ty", ".strm", ".rm", ".rmvb", ".m3u", ".ifo", ".mov", ".qt", ".divx",
        ".xvid", ".bivx", ".nrg", ".pva", ".wmv", ".asf", ".asx", ".ogm", ".ogv", ".m2v", ".avi", ".bin", ".dat",
        ".dvr-ms", ".mpg", ".mpeg", ".mp4", ".avc", ".vp3", ".svq3", ".nuv", ".viv", ".dv", ".fli", ".flv", ".wpl",
        ".img", ".iso", ".vob", ".mkv", ".mk3d", ".ts", ".wtv", ".m2ts"
    ];

    private static Boolean IsImportantFile(String filename)
    {
        var ext = Path.GetExtension(filename).ToLower();
        if (VideoExtensions.Contains(ext)) return true;
        if (ext == ".rar") return true;
        if (Regex.IsMatch(ext, @"^\.r\d+$")) return true;
        return false;
    }

    private static Boolean IsVideoFile(String filename)
    {
        var ext = Path.GetExtension(filename).ToLower();
        return VideoExtensions.Contains(ext);
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
                // Change the hash to allow adding the same NZB content multiple times
                hash = Guid.NewGuid().ToString().Replace("-", "");
            }

            var nzbContent = System.Text.Encoding.UTF8.GetString(bytes);
            var (parsedJobName, files) = await ParseNzb(nzbContent);

            if (files.Count == 0)
            {
                logger.LogWarning($"No valid files found in NZB after filtering: {fileName}");
                throw new Exception("No valid files found in NZB after applying include/exclude filters.");
            }

            var baseJobName = parsedJobName ?? Path.GetFileNameWithoutExtension(fileName);
            var finalJobName = baseJobName;

            var existingJobByName = await dataContext.UsenetJobs.FirstOrDefaultAsync(j => j.JobName == finalJobName && j.Category == category);
            if (existingJobByName != null)
            {
                if (settings.DuplicateNzbBehavior == DuplicateNzbBehavior.MarkFailed)
                {
                    throw new Exception($"Duplicate NZB: A job with name '{finalJobName}' already exists in category '{category ?? "none"}'.");
                }
                else 
                {
                    for (var i = 2; i < 100; i++)
                    {
                        var suffixedName = $"{baseJobName} ({i})";
                        if (!await dataContext.UsenetJobs.AnyAsync(j => j.JobName == suffixedName && j.Category == category))
                        {
                            finalJobName = suffixedName;
                            break;
                        }
                    }
                }
            }

            var job = new UsenetJob
            {
                UsenetJobId = Guid.NewGuid(),
                Hash = hash,
                JobName = finalJobName,
                NzbFileName = fileName,
                NzbContents = nzbContent,
                Category = category,
                Priority = priority,
                Added = DateTimeOffset.UtcNow,
                Status = TorrentStatus.Finished,
                TotalSize = files.Sum(f => f.Size)
            };

            foreach (var file in files)
            {
                file.UsenetJobId = job.UsenetJobId;
                job.Files.Add(file);
            }

            await dataContext.UsenetJobs.AddAsync(job);
            await dataContext.SaveChangesAsync();

            logger.LogInformation($"Saved Usenet job {job.JobName} to database with {job.Files.Count} files. Total size: {job.TotalSize}");

            return job.Hash;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error in UsenetQueueManager.AddNzbFile: {ex.Message}");
            throw;
        }
    }

    public async Task DeleteJob(String hash)
    {
        var job = await GetJobByHash(hash);
        if (job != null)
        {
            dataContext.UsenetFiles.RemoveRange(job.Files);
            dataContext.UsenetJobs.Remove(job);
            await dataContext.SaveChangesAsync();
        }
    }

    public async Task DeleteAllJobs()
    {
        var allFiles = await dataContext.UsenetFiles.ToListAsync();
        dataContext.UsenetFiles.RemoveRange(allFiles);
        var allJobs = await dataContext.UsenetJobs.ToListAsync();
        dataContext.UsenetJobs.RemoveRange(allJobs);
        await dataContext.SaveChangesAsync();
    }

    private async Task<(String? jobName, List<UsenetFile> files)> ParseNzb(String nzbContent)
    {
        logger.LogDebug("ParseNzb started");
        
        var settings = Settings.Get.Usenet;
        if (settings.Enabled && usenetClient is UsenetStreamingClient streamingClient)
        {
            streamingClient.AddProvider(new UsenetProvider
            {
                Host = settings.Host,
                Port = settings.Port,
                UseSsl = settings.UseSsl,
                Username = settings.Username,
                Password = settings.Password,
                MaxConnections = settings.MaxDownloadConnections,
                Priority = 0,
                Enabled = true
            });
        }

        var doc = XDocument.Parse(nzbContent);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        
        var metaJobName = doc.Root?.Element(ns + "head")?.Elements(ns + "meta")
            .FirstOrDefault(m => (String?)m.Attribute("type") == "name" || (String?)m.Attribute("type") == "title")?.Value;

        var allParsedFiles = new List<(String SubjectPath, String[] Segments, Byte[] First16K, String? HeaderFileName, Int64 CalculatedSize, Boolean IsMissing)>();
        var fileElements = doc.Descendants(ns + "file").ToList();

        foreach (var fileElement in fileElements)
        {
            var subject = (String?)fileElement.Attribute("subject");
            if (String.IsNullOrWhiteSpace(subject)) continue;

            var subjectFileName = CleanSubject(subject);
            
            var segmentIds = fileElement.Descendants(ns + "segment")
                .OrderBy(s => (Int32?)s.Attribute("number"))
                .Select(s => s.Value)
                .ToArray();
            
            if (segmentIds.Length == 0) continue;

            String? headerFileName = null;
            Int64 totalSize = 0;
            Byte[] first16K = [];
            Boolean isMissing = false;
            
            try 
            {
                var firstSegmentId = segmentIds[0];
                var article = await usenetClient.DecodedArticleAsync(firstSegmentId, CancellationToken.None);
                
                var buffer = new Byte[16 * 1024];
                var read = await article.Stream.ReadAsync(buffer, 0, buffer.Length);
                first16K = new Byte[read];
                Buffer.BlockCopy(buffer, 0, first16K, 0, read);

                try 
                {
                    var yencHeader = await usenetClient.GetYencHeadersAsync(firstSegmentId, CancellationToken.None);
                    headerFileName = yencHeader.FileName;
                }
                catch { /* fallback */ }

                var lastSegmentId = segmentIds[^1];
                var lastHeader = await usenetClient.GetYencHeadersAsync(lastSegmentId, CancellationToken.None);
                totalSize = lastHeader.PartOffset + lastHeader.PartSize;
            }
            catch (UsenetArticleNotFoundException)
            {
                isMissing = true;
                totalSize = segmentIds.Length * 750 * 1024;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not fetch metadata for {subjectFileName}: {ex.Message}");
                totalSize = segmentIds.Length * 750 * 1024;
            }

            allParsedFiles.Add((subjectFileName, segmentIds, first16K, headerFileName, totalSize, isMissing));
        }

        // HEALTH CHECK: If too many important files are missing, fail the NZB.
        var importantFiles = allParsedFiles.Where(f => IsImportantFile(f.SubjectPath) || IsImportantFile(f.HeaderFileName ?? "")).ToList();
        if (importantFiles.Count > 0)
        {
            var missingCount = importantFiles.Count(f => f.IsMissing);
            var missingPercentage = (Double)missingCount / importantFiles.Count;
            if (missingPercentage > 0.5)
            {
                throw new Exception($"NZB health check failed: {missingCount}/{importantFiles.Count} files ({(missingPercentage * 100):F0}%) are missing from the provider.");
            }
        }

        var par2FileDescriptors = new List<FileDesc>();
        var par2IndexFile = allParsedFiles
            .Where(f => !f.IsMissing && f.First16K.Length > 0 && Par2.Par2.HasPar2MagicBytes(f.First16K))
            .OrderBy(f => f.Segments.Length)
            .FirstOrDefault();

        if (!String.IsNullOrEmpty(par2IndexFile.SubjectPath))
        {
            try 
            {
                logger.LogInformation($"Found PAR2 index file: {par2IndexFile.SubjectPath}. Probing for real filenames...");
                var stream = new Streams.NzbFileStream(par2IndexFile.Segments, par2IndexFile.CalculatedSize, usenetClient, 40);
                await foreach (var fileDesc in Par2.Par2.ReadFileDescriptions(stream))
                {
                    par2FileDescriptors.Add(fileDesc);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error probing PAR2: {ex.Message}");
            }
        }

        var groupedFiles = new List<UsenetFile>();
        var processedPaths = new HashSet<String>();

        using var md5 = MD5.Create();
        var hashToPar2 = par2FileDescriptors.ToDictionary(d => BitConverter.ToString(d.File16kHash!).Replace("-", "").ToLower(), d => d);

        var finalizedFiles = new List<(String Path, Int64 Size, String[] Segments)>();

        foreach (var f in allParsedFiles)
        {
            String? par2Name = null;
            UInt64? par2Size = null;

            if (f.First16K.Length >= 16 * 1024)
            {
                var hash = BitConverter.ToString(md5.ComputeHash(f.First16K)).Replace("-", "").ToLower();
                if (hashToPar2.TryGetValue(hash, out var desc))
                {
                    par2Name = desc.FileName;
                    par2Size = desc.FileLength;
                }
            }

            var bestFileName = new[] 
            {
                (Name: par2Name, Priority: GetFilenamePriority(par2Name, 30)),
                (Name: f.SubjectPath, Priority: GetFilenamePriority(f.SubjectPath, 20)),
                (Name: f.HeaderFileName, Priority: GetFilenamePriority(f.HeaderFileName, 10))
            }
            .Where(x => !String.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.Priority)
            .First().Name;

            finalizedFiles.Add((bestFileName!, (Int64)(par2Size ?? (UInt64)f.CalculatedSize), f.Segments));
        }

        foreach (var fileData in finalizedFiles)
        {
            if (processedPaths.Contains(fileData.Path)) continue;
            
            // APPLY INCLUDE/EXCLUDE FILTERS
            if (!ShouldIncludeFile(fileData.Path)) 
            {
                logger.LogDebug($"Skipping {fileData.Path} due to include/exclude filters.");
                continue;
            }

            var multipartMatch = Regex.Match(fileData.Path, @"^(.*\.mkv)\.\d+$", RegexOptions.IgnoreCase);
            var rarPartMatch = Regex.Match(fileData.Path, @"^(.*\.part)\d+(\.rar)$", RegexOptions.IgnoreCase);
            
            if (multipartMatch.Success)
            {
                var baseName = multipartMatch.Groups[1].Value;
                if (processedPaths.Contains(baseName)) continue;

                var parts = finalizedFiles
                    .Where(f => f.Path.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                groupedFiles.Add(new UsenetFile
                {
                    UsenetFileId = Guid.NewGuid(),
                    Path = baseName,
                    Size = parts.Sum(p => p.Size),
                    SegmentIdList = parts.SelectMany(p => p.Segments).ToArray()
                });

                foreach (var p in parts) processedPaths.Add(p.Path);
                processedPaths.Add(baseName);
            }
            else if (rarPartMatch.Success)
            {
                var baseName = rarPartMatch.Groups[1].Value + "rar";
                var prefix = rarPartMatch.Groups[1].Value;
                if (processedPaths.Contains(baseName)) continue;

                var parts = finalizedFiles
                    .Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                groupedFiles.Add(new UsenetFile
                {
                    UsenetFileId = Guid.NewGuid(),
                    Path = baseName,
                    Size = parts.Sum(p => p.Size),
                    SegmentIdList = parts.SelectMany(p => p.Segments).ToArray()
                });

                foreach (var p in parts) processedPaths.Add(p.Path);
                processedPaths.Add(baseName);
            }
            else if (fileData.Path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) && !processedPaths.Contains(fileData.Path))
            {
                var baseName = Path.GetFileNameWithoutExtension(fileData.Path) + ".rar";
                var prefix = Path.GetFileNameWithoutExtension(fileData.Path);
                
                var parts = finalizedFiles
                    .Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                groupedFiles.Add(new UsenetFile
                {
                    UsenetFileId = Guid.NewGuid(),
                    Path = baseName,
                    Size = parts.Sum(p => p.Size),
                    SegmentIdList = parts.SelectMany(p => p.Segments).ToArray()
                });

                foreach (var p in parts) processedPaths.Add(p.Path);
                processedPaths.Add(baseName);
            }
            else if (IsImportantFile(fileData.Path))
            {
                groupedFiles.Add(new UsenetFile
                {
                    UsenetFileId = Guid.NewGuid(),
                    Path = fileData.Path,
                    Size = fileData.Size,
                    SegmentIdList = fileData.Segments
                });
                processedPaths.Add(fileData.Path);
            }
        }

        // VIDEO CHECK
        if (settings.FailIfNoVideo && !groupedFiles.Any(f => IsVideoFile(f.Path)))
        {
            throw new Exception("FailIfNoVideo: No video files found in NZB.");
        }

        String? finalJobName = null;
        if (groupedFiles.Count > 0)
        {
            var firstFile = groupedFiles[0].Path;
            if (!IsProbablyObfuscated(firstFile))
            {
                finalJobName = Path.GetFileNameWithoutExtension(firstFile);
            }
        }

        if (String.IsNullOrWhiteSpace(finalJobName) && !String.IsNullOrWhiteSpace(metaJobName))
        {
            if (!IsProbablyObfuscated(metaJobName))
            {
                finalJobName = metaJobName;
            }
        }

        return (finalJobName ?? metaJobName, groupedFiles);
    }

    private static Int32 GetFilenamePriority(String? filename, Int32 startingPriority)
    {
        if (String.IsNullOrWhiteSpace(filename)) return -10000;
        var priority = startingPriority;
        if (IsProbablyObfuscated(filename)) priority -= 1000;
        
        var extension = Path.GetExtension(filename).ToLower();
        if (VideoExtensions.Contains(extension)) priority += 50;
        if (extension == ".rar" || Regex.IsMatch(extension, @"^\.r\d+$")) priority += 40;
        
        if (extension.Length >= 2 && extension.Length <= 5) priority += 10;
        
        return priority;
    }

    private static String CleanSubject(String subject)
    {
        var match = Regex.Match(subject, "\"([^\"]+)\"");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var cleaned = Regex.Replace(subject, @"\s*\[\d+/\d+\]\s*", " ");
        cleaned = Regex.Replace(cleaned, @"\s*\(\d+/\d+\)\s*$", "");
        cleaned = cleaned.Trim();

        return cleaned;
    }

    private static Boolean IsProbablyObfuscated(String filename)
    {
        var fileBaseName = Path.GetFileNameWithoutExtension(filename);
        if (Regex.IsMatch(fileBaseName, @"^[a-f0-9]{32}$", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(fileBaseName, @"^[a-f0-9.]{40,}$", RegexOptions.IgnoreCase)) return true;
        if (fileBaseName.Length > 20 && !fileBaseName.Contains(' ') && !fileBaseName.Contains('.') && !fileBaseName.Contains('_'))
        {
             return true;
        }
        return false;
    }
}
