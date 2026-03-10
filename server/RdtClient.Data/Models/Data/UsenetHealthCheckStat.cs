namespace RdtClient.Data.Models.Data;

public class UsenetHealthCheckStat
{
    public DateTimeOffset DateStartInclusive { get; set; }
    public DateTimeOffset DateEndExclusive { get; set; }
    public UsenetHealthCheckResult.HealthResult Result { get; set; }
    public UsenetHealthCheckResult.RepairAction RepairStatus { get; set; }
    public Int32 Count { get; set; }
}
