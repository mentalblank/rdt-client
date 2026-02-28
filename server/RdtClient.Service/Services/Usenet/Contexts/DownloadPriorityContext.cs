using RdtClient.Service.Services.Usenet.Concurrency;

namespace RdtClient.Service.Services.Usenet.Contexts;

public record DownloadPriorityContext
{
    public required SemaphorePriority Priority { get; init; }
}
