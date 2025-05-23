
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using MonoTorrent.BEncoding;

namespace RdtClient.Service.Services;

public interface IEnricher
{
    Task<String> EnrichMagnetLink(String magnetLink);
    Task<Byte[]> EnrichTorrentBytes(Byte[] torrentBytes);
}

/// <summary>
/// Enriches magnet links and torrents by adding trackers from the tracker list grabber.
/// </summary>
public sealed class Enricher(ILogger<Enricher> logger, ITrackerListGrabber trackerListGrabber) : IEnricher
{
    /// <summary>
    /// Add trackers from the tracker list grabber to the magnet link.
    /// </summary>
    /// <param name="magnetLink">Magnet link to add trackers to. Is not modified</param>
    /// <returns>Magnet link with additional trackers</returns>
    public async Task<String> EnrichMagnetLink(String magnetLink)
    {
        var newTrackers = await trackerListGrabber.GetTrackers().ConfigureAwait(false);

        if (newTrackers.Length == 0)
        {
            logger.LogWarning("No new trackers were retrieved.");

            return magnetLink;
        }

        var qmIdx = magnetLink.IndexOf('?');

        if (qmIdx == -1 || qmIdx == magnetLink.Length - 1)
        {
            return magnetLink;
        }

        var schemePart = magnetLink[..qmIdx];
        var queryPart = magnetLink[(qmIdx + 1)..];

        var query = HttpUtility.ParseQueryString(queryPart);
        var existingTrackers = query.GetValues("tr") ?? [];
        var trackerSet = new HashSet<String>(existingTrackers, StringComparer.OrdinalIgnoreCase);
        var trackersToAdd = newTrackers.Where(t => !trackerSet.Contains(t)).ToList();

        if (trackersToAdd.Count == 0)
        {
            return magnetLink;
        }

        var allTrackers = existingTrackers.Concat(trackersToAdd).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        query.Remove("tr");

        var baseQueryPairs = new List<String>();

        foreach (var key in query.AllKeys)
        {
            foreach (var val in query.GetValues(key) ?? [])
            {
                if (key == "xt")
                {
                    baseQueryPairs.Add("xt=" + val);
                }
                else if (key != null)
                {
                    baseQueryPairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(val)}");
                }
            }
        }

        var rebuilt = schemePart + (baseQueryPairs.Count > 0 ? "?" + String.Join("&", baseQueryPairs) + "&" : "?");
        var newMagnet = new StringBuilder(rebuilt);

        for (var i = 0; i < allTrackers.Length; i++)
        {
            if (i == 0 && !rebuilt.EndsWith("&") && !rebuilt.EndsWith("?"))
            {
                newMagnet.Append('&');
            }

            newMagnet.Append("tr=").Append(Uri.EscapeDataString(allTrackers[i]));

            if (i != allTrackers.Length - 1)
            {
                newMagnet.Append('&');
            }
        }

        var finalMagnet = newMagnet.ToString();

        logger.LogInformation("Added {NewTrackersCount} new trackers to the magnet link. Total trackers: {TotalTrackersCount}.",
                              trackersToAdd.Count,
                              allTrackers.Length);

        return finalMagnet;
    }

    /// <summary>
    /// Add trackers from the tracker list grabber to the .torrent file bytes.
    /// </summary>
    /// <param name="torrentBytes">Torrent file bytes to add trackers to. Is not modified</param>
    /// <returns>Torrent file bytes with additional trackers</returns>
    public async Task<Byte[]> EnrichTorrentBytes(Byte[] torrentBytes)
    {
        if (torrentBytes == null || torrentBytes.Length == 0)
        {
            throw new ArgumentException("Torrent bytes cannot be null or empty.", nameof(torrentBytes));
        }

        BEncodedDictionary torrentDict;

        try
        {
            torrentDict = BEncodedValue.Decode<BEncodedDictionary>(torrentBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decode torrent bytes.");

            throw new InvalidOperationException("Invalid torrent file format.", ex);
        }

        var newTrackers = await trackerListGrabber.GetTrackers().ConfigureAwait(false);

        if (!newTrackers.Any())
        {
            logger.LogWarning("No new trackers were retrieved.");

            return torrentBytes;
        }

        var seenTrackers = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        var allTrackers = new List<String>();

        if (torrentDict.TryGetValue("announce-list", out var alc) && alc is BEncodedList alList)
        {
            foreach (var tier in alList.OfType<BEncodedList>())
            {
                foreach (var s in tier.OfType<BEncodedString>())
                {
                    if (seenTrackers.Add(s.Text))
                    {
                        allTrackers.Add(s.Text);
                    }
                }
            }
        }

        if (torrentDict.TryGetValue("announce", out var announceValue) && announceValue is BEncodedString announceStr)
        {
            if (seenTrackers.Add(announceStr.Text))
            {
                allTrackers.Add(announceStr.Text);
            }
        }

        foreach (var tracker in newTrackers)
        {
            if (seenTrackers.Add(tracker))
            {
                allTrackers.Add(tracker);
            }
        }

        var dedupedAnnounceList = new BEncodedList();

        foreach (var tracker in allTrackers)
        {
            dedupedAnnounceList.Add(new BEncodedList
            {
                new BEncodedString(tracker)
            });
        }

        torrentDict["announce-list"] = dedupedAnnounceList;

        if (allTrackers.Count > 0)
        {
            torrentDict["announce"] = new BEncodedString(allTrackers[0]);
        }

        logger.LogInformation("Added {NewTrackersCount} new trackers to the torrent. Total trackers: {TotalTrackersCount}.",
                              allTrackers.Count - seenTrackers.Count + newTrackers.Length,
                              allTrackers.Count);

        return torrentDict.Encode();
    }
}
