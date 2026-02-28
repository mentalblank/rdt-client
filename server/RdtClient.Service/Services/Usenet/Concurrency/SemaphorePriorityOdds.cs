namespace RdtClient.Service.Services.Usenet.Concurrency;

public record SemaphorePriorityOdds
{
    public required Int32 HighPriorityOdds { get; set; }
    public Int32 LowPriorityOdds => 100 - HighPriorityOdds;
}
