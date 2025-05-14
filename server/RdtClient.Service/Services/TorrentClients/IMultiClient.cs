using RdtClient.Data.Models.TorrentClient;

namespace RdtClient.Service.Services.TorrentClients;

/// <summary>
/// Extended client interface for providers supporting multiple download types (e.g., Usenet + Torrent).
/// </summary>
public interface IMultiClient : ITorrentClient
{
    /// <summary>
    /// Adds a magnet link to the appropriate queue based on the type.
    /// </summary>
    /// <param name="link">The magnet link</param>
    /// <param name="debridContentKind">0 = Torrent, 1 = Usenet</param>
    Task<String> AddMagnet(String link, Int64? debridContentKind);

    /// <summary>
    /// Adds a file (e.g., NZB or .torrent) to the debrid queue based on the type.
    /// </summary>
    /// <param name="bytes">The file contents</param>
    /// <param name="debridContentKind">0 = Torrent, 1 = Usenet</param>
    /// <param name="fileName">The file name (optional)</param>
    Task<String> AddFile(Byte[] bytes, Int64? debridContentKind, String? fileName = null);

    /// <summary>
    /// Converts a debrid provider link to a direct download link.
    /// </summary>
    /// <param name="link">The debrid link</param>
    /// <param name="debridContentKind">0 = Torrent, 1 = Usenet</param>
    Task<String> Unrestrict(String link, Int64? debridContentKind);

    /// <summary>
    /// Gets the list of available files from the provider by hash and type.
    /// </summary>
    Task<IList<TorrentClientAvailableFile>> GetAvailableFiles(String hash, Int64? debridContentKind);

    /// <summary>
    /// Deletes a remote torrent/NZB from the debrid provider.
    /// </summary>
    Task Delete(String torrentId, Int64? debridContentKind);
}