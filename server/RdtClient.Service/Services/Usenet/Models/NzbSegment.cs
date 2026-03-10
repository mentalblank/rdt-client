namespace RdtClient.Service.Services.Usenet.Models;

public class NzbSegment
{
    public required Int64 Bytes { get; init; }
    public required String MessageId { get; init; }
}
