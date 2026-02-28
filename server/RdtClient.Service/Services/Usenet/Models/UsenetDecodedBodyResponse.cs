using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet.Models;

public record UsenetDecodedBodyResponse : UsenetResponse
{
    public required String SegmentId { get; init; }
    public required YencStream Stream { get; init; }
}
