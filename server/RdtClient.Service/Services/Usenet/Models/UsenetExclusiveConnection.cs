using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet.Models;

public readonly struct UsenetExclusiveConnection(Action<ArticleBodyResult>? onConnectionReadyAgain)
{
    public Action<ArticleBodyResult>? OnConnectionReadyAgain => onConnectionReadyAgain;
}
