using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Queue.Steps;
using RdtClient.Service.Services.Usenet.Utils;
using RdtClient.Service.Services.Usenet.Exceptions;

namespace RdtClient.Service.Services.Usenet.Queue.FileProcessors;

public class UsenetFileProcessor(
    UsenetGetFileInfosStep.FileInfo fileInfo,
    INntpClient usenetClient,
    CancellationToken ct
) : UsenetBaseProcessor
{
    public override async Task<UsenetBaseProcessor.Result?> ProcessAsync()
    {
        try
        {
            return new Result()
            {
                NzbFile = fileInfo.NzbFile,
                FileName = fileInfo.FileName,
                FileSize = fileInfo.FileSize ?? await usenetClient
                    .GetFileSizeAsync(fileInfo.NzbFile, ct)
                    .ConfigureAwait(false),
                ReleaseDate = fileInfo.ReleaseDate,
            };
        }

        // Ignore missing articles if it's not a video file.
        // In that case, simply skip the file altogether.
        catch (UsenetArticleNotFoundException) when (!FilenameUtil.IsVideoFile(fileInfo.FileName))
        {
            Serilog.Log.Warning("File {fileName} has missing articles. Skipping file since it is not a video.", fileInfo.FileName);
            return null;
        }
    }

    public new class Result : UsenetBaseProcessor.Result
    {
        public required NzbFile NzbFile { get; init; }
        public required String FileName { get; init; }
        public required Int64 FileSize { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
    }
}
