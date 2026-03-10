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
public class UsenetController(
    UsenetQueueManager queueManager,
    UsenetStore usenetStore,
    DataContext dataContext,
    UsenetMaintenanceManager maintenanceManager,
    UsenetImportManager importManager,
    ILogger<UsenetController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IList<UsenetJob>>> Get()
    {
        return Ok(await queueManager.GetJobs());
    }

    [AllowAnonymous]
    [HttpPost("maintenance/cleanup")]
    public async Task<ActionResult> CleanupOrphans()
    {
        try
        {
            var count = await maintenanceManager.RemoveOrphanedFiles(HttpContext.RequestAborted);
            return Ok(new { Count = count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error during orphan cleanup: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpPost("maintenance/strm-to-symlinks")]
    public async Task<ActionResult> StrmToSymlinks()
    {
        try
        {
            var count = await importManager.ConvertStrmFilesToSymlinks();
            return Ok(new { Count = count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error during STRM to Symlink conversion: {ex.Message}");
            return BadRequest(ex.Message);
        }
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
                // It's a directory
                if (Guid.TryParse(collection.UniqueKey, out var itemGuid))
                {
                    var dbItem = await dataContext.UsenetDavItems.FirstOrDefaultAsync(j => j.Id == itemGuid);
                    if (dbItem != null)
                    {
                        if (dbItem.ParentId == null)
                        {
                            return BadRequest("Cannot delete static system folders.");
                        }

                        // If it's a mount folder (top level job folder), use queueManager to clean up legacy tables too
                        var job = await dataContext.UsenetJobs.FirstOrDefaultAsync(j => j.JobName == dbItem.Name);
                        if (job != null && dbItem.ParentId == UsenetDavItemConstants.ContentFolder.Id)
                        {
                            await queueManager.DeleteJob(job.Hash, true);
                        }
                        else 
                        {
                            dataContext.UsenetDavItems.Remove(dbItem);
                            await dataContext.SaveChangesAsync();
                        }
                        logger.LogInformation($"Item {dbItem.Name} (ID: {itemGuid}) deleted successfully.");
                    }
                }
            }
            else if (item is UsenetStoreItem file)
            {
                // It's a file
                if (Guid.TryParse(file.UniqueKey, out var itemGuid))
                {
                    var dbItem = await dataContext.UsenetDavItems.FirstOrDefaultAsync(f => f.Id == itemGuid);
                    if (dbItem != null)
                    {
                        dataContext.UsenetDavItems.Remove(dbItem);
                        await dataContext.SaveChangesAsync();
                        logger.LogInformation($"File {dbItem.Name} (ID: {itemGuid}) deleted from database.");
                    }
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
