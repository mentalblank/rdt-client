using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RdtClient.Data.Models.Sabnzbd;
using RdtClient.Service.Services.Usenet;

namespace RdtClient.Web.Controllers;

[ApiController]
[Route("usenet/api")]
public class UsenetSabnzbdController(ILogger<UsenetSabnzbdController> logger, UsenetSabnzbd sabnzbd) : Controller
{
    [HttpGet]
    [HttpPost]
    public async Task<ActionResult> Get([FromQuery] String? mode)
    {
        var apiKey = GetParam("apikey");
        var settings = RdtClient.Service.Services.Settings.Get.Usenet;

        if (String.IsNullOrWhiteSpace(apiKey) || apiKey != settings.ApiKey)
        {
            return Unauthorized(new SabnzbdResponse
            {
                Error = "Invalid API key"
            });
        }

        if (Request.HasFormContentType)
        {
            mode ??= Request.Form["mode"].ToString();
        }

        if (String.IsNullOrWhiteSpace(mode))
        {
            return BadRequest(new SabnzbdResponse
            {
                Error = "No mode specified"
            });
        }

        switch (mode.ToLower())
        {
            case "version":
                return Version();
            case "queue":
                return await Queue();
            case "history":
                return await History();
            case "get_config":
                return GetConfig();
            case "get_cats":
                return GetCats();
            case "addfile":
                return await AddFile();
            case "fullstatus":
                return await FullStatus();
        }

        logger.LogWarning($"Usenet Sabnzbd API called (unknown mode) - Mode: {mode}, Method: {Request.Method}");

        return NotFound(new SabnzbdResponse());
    }

    [AllowAnonymous]
    [HttpGet]
    [SabnzbdMode("version")]
    public ActionResult Version()
    {
        return Ok(new SabnzbdResponse
        {
            Version = "4.4.0"
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [SabnzbdMode("queue")]
    public async Task<ActionResult> Queue()
    {
        var name = GetParam("name");

        if (name == "delete")
        {
            var value = GetParam("value");
            if (String.IsNullOrWhiteSpace(value))
            {
                return BadRequest(new SabnzbdResponse { Error = "No value specified" });
            }
            await sabnzbd.Delete(value);
            return Ok(new SabnzbdResponse { Status = true });
        }

        return Ok(new SabnzbdResponse
        {
            Queue = await sabnzbd.GetQueue()
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [SabnzbdMode("history")]
    public async Task<ActionResult> History()
    {
        var history = await sabnzbd.GetHistory();
        var limitStr = GetParam("limit");
        var settings = RdtClient.Service.Services.Settings.Get.Usenet;

        if (!settings.AlwaysSendFullHistory && Int32.TryParse(limitStr, out var limit) && limit > 0)
        {
            history.Slots = history.Slots.Take(limit).ToList();
            history.NoOfSlots = history.Slots.Count;
        }

        return Ok(new SabnzbdResponse
        {
            History = history
        });
    }

    [HttpGet]
    [SabnzbdMode("get_config")]
    public ActionResult GetConfig()
    {
        return Ok(new SabnzbdResponse
        {
            Config = sabnzbd.GetConfig()
        });
    }

    [HttpGet]
    [SabnzbdMode("get_cats")]
    public ActionResult GetCats()
    {
        return Ok(new SabnzbdResponse
        {
            Categories = sabnzbd.GetCategories()
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [SabnzbdMode("addfile")]
    public async Task<ActionResult> AddFile()
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest("Expected multipart/form-data");
        }

        var file = Request.Form.Files.FirstOrDefault();
        if (file == null)
        {
            return BadRequest("No file uploaded");
        }

        var category = GetParam("cat");
        var priorityStr = GetParam("priority");
        Int32? priority = Int32.TryParse(priorityStr, out var p) ? p : null;

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var result = await sabnzbd.AddFile(ms.ToArray(), file.FileName, category, priority);

        return Ok(new SabnzbdResponse
        {
            Status = true,
            NzoIds = [result]
        });
    }

    [AllowAnonymous]
    [HttpGet]
    [SabnzbdMode("fullstatus")]
    public async Task<ActionResult> FullStatus()
    {
        return Ok(new SabnzbdResponse
        {
            Version = "4.4.0",
            Queue = await sabnzbd.GetQueue()
        });
    }

    private String? GetParam(String name)
    {
        var value = Request.Query[name].ToString();
        if (String.IsNullOrWhiteSpace(value) && Request.HasFormContentType)
        {
            value = Request.Form[name].ToString();
        }
        return value;
    }
}
