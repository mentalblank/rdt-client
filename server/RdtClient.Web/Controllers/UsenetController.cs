using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services.Usenet.WebDav;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Web.Controllers;

[ApiController]
[Route("api/usenet")]
[Authorize]
public class UsenetController(UsenetQueueManager queueManager, UsenetStore usenetStore, DataContext dataContext, ILogger<UsenetController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IList<UsenetJob>>> Get()
    {
        return Ok(await queueManager.GetJobs());
    }

    [AllowAnonymous]
    [HttpGet("webdav/download")]
    public async Task<ActionResult> DownloadWebdav([FromQuery] String path)
    {
        try
        {
            var item = await usenetStore.GetItemAsync(path, HttpContext.RequestAborted);
            if (item is not BaseStoreReadonlyItem fileItem) return NotFound("File not found");

            var stream = await fileItem.GetReadableStreamAsync(HttpContext.RequestAborted);
            return File(stream, "application/octet-stream", fileItem.Name, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error downloading WebDAV file {path}: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpDelete("webdav")]
    public async Task<ActionResult> DeleteWebdav([FromQuery] String path)
    {
        try
        {
            logger.LogInformation($"DeleteWebdav called for path: {path}");
            var settings = RdtClient.Service.Services.Settings.Get.WebDav;
            if (settings.ReadOnly) return BadRequest("WebDAV is in read-only mode");

            var item = await usenetStore.GetItemAsync(path, HttpContext.RequestAborted);
            if (item == null) 
            {
                logger.LogWarning($"Item not found for path: {path}");
                return NotFound("Item not found");
            }

            logger.LogInformation($"Found item of type: {item.GetType().Name}");

            if (item is UsenetStoreCollection collection)
            {
                // It's a job folder
                var jobId = collection.UniqueKey;
                logger.LogInformation($"Deleting job folder with ID: {jobId}");
                if (Guid.TryParse(jobId, out var jobGuid))
                {
                    var job = await dataContext.UsenetJobs.FirstOrDefaultAsync(j => j.UsenetJobId == jobGuid);
                    if (job != null)
                    {
                        await queueManager.DeleteJob(job.Hash, true);
                        logger.LogInformation($"Job {job.JobName} (Hash: {job.Hash}) deleted successfully.");
                    }
                    else
                    {
                        logger.LogWarning($"Job not found in database for Guid: {jobGuid}");
                    }
                }
                else
                {
                    logger.LogError($"Failed to parse job folder ID '{jobId}' as Guid");
                }
            }
            else if (item is UsenetStoreFile file)
            {
                // It's a file
                var fileId = file.UniqueKey;
                logger.LogInformation($"Deleting file with ID: {fileId}");
                if (Guid.TryParse(fileId, out var fileGuid))
                {
                    var dbFile = await dataContext.UsenetFiles.FirstOrDefaultAsync(f => f.UsenetFileId == fileGuid);
                    if (dbFile != null)
                    {
                        dataContext.UsenetFiles.Remove(dbFile);
                        await dataContext.SaveChangesAsync();
                        logger.LogInformation($"File {dbFile.Path} (ID: {fileGuid}) deleted from database.");
                    }
                    else
                    {
                        logger.LogWarning($"File not found in database for Guid: {fileGuid}");
                    }
                }
                else
                {
                    logger.LogError($"Failed to parse file ID '{fileId}' as Guid");
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error deleting WebDAV item {path}: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpGet("webdav")]
    public async Task<ActionResult> ListWebdav([FromQuery] String? path)
    {
        try
        {
            var targetPath = path ?? "/";
            var item = await usenetStore.GetItemAsync(targetPath, HttpContext.RequestAborted);
            
            if (item == null) return NotFound("Path not found");
            if (item is not IStoreCollection collection) return BadRequest("Path is not a collection");

            var settings = RdtClient.Service.Services.Settings.Get.WebDav;
            var isReadOnly = settings.ReadOnly;

            var items = new List<Object>();
            await foreach (var child in collection.GetItemsAsync(HttpContext.RequestAborted))
            {
                items.Add(new
                {
                    Name = child.Name,
                    IsDirectory = child is IStoreCollection,
                    Size = child is BaseStoreReadonlyItem bsi ? bsi.FileSize : (Int64?)null
                });
            }

            return Ok(new { Items = items, ReadOnly = isReadOnly });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error listing WebDAV path {path}: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<ActionResult<UsenetJob>> Get(Guid id)
    {
        var jobs = await queueManager.GetJobs();
        var job = jobs.FirstOrDefault(j => j.UsenetJobId == id);
        if (job == null) return NotFound();
        return Ok(job);
    }

    [AllowAnonymous]
    [HttpPost("upload")]
    public async Task<ActionResult> Upload()
    {
        try
        {
            if (!Request.HasFormContentType) return BadRequest("Not a form content type");
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null) return BadRequest("No file uploaded");

            var category = Request.Form["category"].ToString();
            if (String.IsNullOrWhiteSpace(category)) category = null;
            
            var priorityStr = Request.Form["priority"].ToString();
            var priority = Int32.TryParse(priorityStr, out var p) ? p : 0;

            logger.LogInformation($"Uploading NZB file: {file.FileName}, Category: {category}, Priority: {priority}");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            await queueManager.AddNzbFile(ms.ToArray(), file.FileName, category, priority);

            logger.LogInformation($"Successfully added Usenet job: {file.FileName}");

            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error uploading NZB: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpDelete("{hash}")]
    public async Task<ActionResult> Delete(String hash, [FromQuery] Boolean deleteData)
    {
        await queueManager.DeleteJob(hash, deleteData);
        return Ok();
    }

    [AllowAnonymous]
    [HttpDelete("all")]
    public async Task<ActionResult> DeleteAll([FromQuery] Boolean deleteData = false)
    {
        await queueManager.DeleteAllJobs(deleteData);
        return Ok();
    }
}
