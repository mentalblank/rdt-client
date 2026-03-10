using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services.Usenet.Par2.Packets;

namespace RdtClient.Service.Services.Usenet.Queue.Steps;

public static class UsenetGetPar2FileDescriptorsStep
{
    public static async Task<List<FileDesc>> GetPar2FileDescriptors
    (
        List<UsenetFetchFirstSegmentsStep.NzbFileWithFirstSegment> files,
        INntpClient usenetClient,
        CancellationToken cancellationToken = default
    )
    {
        // find the par2 index file
        var par2Index = files
            .Where(x => !x.MissingFirstSegment)
            .Where(x => Par2.Par2.HasPar2MagicBytes(x.First16KB!))
            .OrderBy(x => x.NzbFile.Segments.Count)
            .FirstOrDefault();
            
        if (par2Index is null) return [];

        // return all file descriptors
        var fileDescriptors = new List<FileDesc>();
        var segments = par2Index.NzbFile.GetSegmentIds();
        
        var filesize = par2Index.NzbFile.Segments.Count == 1
            ? par2Index.Header!.PartOffset + par2Index.Header!.PartSize
            : await usenetClient.GetFileSizeAsync(par2Index.NzbFile, cancellationToken).ConfigureAwait(false);
            
        await using var stream = usenetClient.GetFileStream(segments, filesize, articleBufferSize: 0);
        
        await foreach (var fileDescriptor in Par2.Par2.ReadFileDescriptions(stream, cancellationToken).ConfigureAwait(false))
            fileDescriptors.Add(fileDescriptor);
            
        return fileDescriptors;
    }
}
