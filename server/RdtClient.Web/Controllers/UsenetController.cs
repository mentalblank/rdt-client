using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet;

namespace RdtClient.Web.Controllers;

[ApiController]
[Route("api/usenet")]
[Authorize]
public class UsenetController(UsenetQueueManager queueManager, ILogger<UsenetController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IList<UsenetJob>>> Get()
    {
        return Ok(await queueManager.GetJobs());
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
        await queueManager.DeleteJob(hash);
        return Ok();
    }

    [AllowAnonymous]
    [HttpDelete("all")]
    public async Task<ActionResult> DeleteAll([FromQuery] Boolean deleteData)
    {
        await queueManager.DeleteAllJobs();
        return Ok();
    }
}
