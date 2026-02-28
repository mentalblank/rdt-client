using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet.Models;

public record UsenetDecodedArticleResponse : UsenetResponse
{
    public required String SegmentId { get; init; }
    public required YencStream Stream { get; init; }
    public required UsenetArticleHeader ArticleHeaders { get; init; }
}
