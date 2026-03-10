using System.ComponentModel.DataAnnotations;

namespace RdtClient.Data.Models.Data;

public class UsenetHealthCheckResult
{
    [Key]
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid DavItemId { get; init; }
    public String Path { get; init; } = null!;
    public HealthResult Result { get; init; }
    public RepairAction RepairStatus { get; set; }
    public String? Message { get; set; }

    public enum HealthResult
    {
        Healthy = 0,
        Unhealthy = 1,
    }

    public enum RepairAction
    {
        None = 0,
        Repaired = 1,
        Deleted = 2,
        ActionNeeded = 3,
    }
}
