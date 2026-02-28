using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using NWebDav.Server;
using NWebDav.Server.Handlers;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using RdtClient.Service.Services.Usenet.Streams;

namespace RdtClient.Service.Services.Usenet.WebDav.Base;

public class GetAndHeadHandlerPatch(IStore store) : IRequestHandler
{
    public async Task<bool> HandleRequestAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;

        var isHeadRequest = request.Method == HttpMethods.Head;
        var range = request.GetRange();

        var entry = await store.GetItemAsync(request.GetUri(), httpContext.RequestAborted).ConfigureAwait(false);
        if (entry == null)
        {
            response.StatusCode = (Int32)DavStatusCode.NotFound;
            return true;
        }

        var propertyManager = entry.PropertyManager;
        if (propertyManager != null)
        {
            var lastModifiedUtc = await propertyManager.GetPropertyAsync(entry, (XName)"{DAV:}getlastmodified", true, httpContext.RequestAborted).ConfigureAwait(false);
            if (lastModifiedUtc != null)
                response.Headers.LastModified = lastModifiedUtc.ToString();
        }

        if (entry is IStoreCollection)
        {
            response.StatusCode = (Int32)DavStatusCode.Ok;
            return true;
        }

        var stream = await entry.GetReadableStreamAsync(httpContext.RequestAborted).ConfigureAwait(false);
        var probingStream = new ProbingStream(stream);
        await using (probingStream.ConfigureAwait(false))
        {
            if (!await probingStream.IsEmptyAsync().ConfigureAwait(false))
            {
                response.StatusCode = (Int32)DavStatusCode.Ok;

                if (probingStream.CanSeek)
                {
                    response.Headers.AcceptRanges = "bytes";
                    var length = probingStream.Length;

                    if (range != null)
                    {
                        var start = range.Start ?? 0;
                        var end = Math.Min(range.End ?? Int64.MaxValue, length - 1);
                        length = end - start + 1;

                        response.Headers.ContentRange = $"bytes {start}-{end} / {probingStream.Length}";
                        response.StatusCode = (Int32)DavStatusCode.PartialContent;
                    }

                    response.ContentLength = length;
                }

                if (!isHeadRequest)
                    await CopyToAsync(probingStream, response.Body, range?.Start ?? 0, range?.End, httpContext.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                response.StatusCode = (Int32)DavStatusCode.NoContent;
            }
        }
        return true;
    }

    private async Task CopyToAsync(Stream src, Stream dest, Int64 start, Int64? end, CancellationToken cancellationToken)
    {
        if (start > 0)
        {
            if (!src.CanSeek)
                throw new IOException("Cannot use range, because the source stream isn't seekable");
            
            src.Seek(start, SeekOrigin.Begin);
        }

        var bytesToRead = end - start + 1 ?? Int64.MaxValue;
        var buffer = new Byte[64 * 1024];

        while (bytesToRead > 0)
        {
            var requestedBytes = (Int32)Math.Min(bytesToRead, buffer.Length);
            var bytesRead = await src.ReadAsync(buffer, 0, requestedBytes, cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
                return;
            
            await dest.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            bytesToRead -= bytesRead;
        }
    }
}
