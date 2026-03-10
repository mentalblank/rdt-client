using System.Net;
using RdtClient.Service.Services.Usenet.Utils;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class UsenetSonarrClient(String host, String apiKey) : UsenetArrClient(host, apiKey)
{
    private static readonly Dictionary<String, Int32> SeriesPathToSeriesIdCache = [];
    private static readonly Dictionary<String, Int32> SymlinkOrStrmToEpisodeFileIdCache = [];

    public Task<List<SonarrSeries>> GetAllSeries() =>
        Get<List<SonarrSeries>>($"/series");

    public Task<SonarrSeries> GetSeries(Int32 seriesId) =>
        Get<SonarrSeries>($"/series/{seriesId}");

    public Task<SonarrEpisodeFile> GetEpisodeFile(Int32 episodeFileId) =>
        Get<SonarrEpisodeFile>($"/episodefile/{episodeFileId}");

    public Task<List<SonarrEpisodeFile>> GetAllEpisodeFiles(Int32 seriesId) =>
        Get<List<SonarrEpisodeFile>>($"/episodefile?seriesId={seriesId}");

    public Task<List<SonarrEpisode>> GetEpisodesFromEpisodeFileId(Int32 episodeFileId) =>
        Get<List<SonarrEpisode>>($"/episode?episodeFileId={episodeFileId}");

    public Task<HttpStatusCode> DeleteEpisodeFile(Int32 episodeFileId) =>
        Delete($"/episodefile/{episodeFileId}");

    public Task<ArrCommand> SearchEpisodesAsync(List<Int32> episodeIds) =>
        CommandAsync(new { name = "EpisodeSearch", episodeIds });

    public override async Task<Boolean> RemoveAndSearch(String symlinkOrStrmPath)
    {
        // get episode-file-id and episode-ids
        var mediaIds = await GetMediaIds(symlinkOrStrmPath);
        if (mediaIds == null) return false;

        // delete the episode-file
        if (await DeleteEpisodeFile(mediaIds.Value.episodeFileId) != HttpStatusCode.OK)
            throw new Exception($"Failed to delete episode file `{symlinkOrStrmPath}` from sonarr instance `{Host}`.");

        // trigger a new search for each episode
        await SearchEpisodesAsync(mediaIds.Value.episodeIds);
        return true;
    }

    private async Task<(Int32 episodeFileId, List<Int32> episodeIds)?> GetMediaIds(String symlinkOrStrmPath)
    {
        // get episode-file-id
        var episodeFileId = await GetEpisodeFileId(symlinkOrStrmPath);
        if (episodeFileId == null) return null;

        // get episode-ids
        var episodes = await GetEpisodesFromEpisodeFileId(episodeFileId.Value);
        var episodeIds = episodes.Select(x => x.Id).ToList();
        if (episodeIds.Count == 0) return null;

        // return
        return (episodeFileId.Value, episodeIds);
    }

    private async Task<Int32?> GetEpisodeFileId(String symlinkOrStrmPath)
    {
        // if episode-file-id is found in the cache, verify it and return it
        if (SymlinkOrStrmToEpisodeFileIdCache.TryGetValue(symlinkOrStrmPath, out var episodeFileId))
        {
            var episodeFile = await GetEpisodeFile(episodeFileId);
            if (episodeFile.Path == symlinkOrStrmPath) return episodeFileId;
        }

        // otherwise, find the series-id
        var seriesId = await GetSeriesId(symlinkOrStrmPath);
        if (seriesId == null) return null;

        // then use it to find all episode-files and repopulate the cache
        Int32? result = null;
        foreach (var episodeFile in await GetAllEpisodeFiles(seriesId.Value))
        {
            if (episodeFile.Path != null)
            {
                SymlinkOrStrmToEpisodeFileIdCache[episodeFile.Path] = episodeFile.Id;
                if (episodeFile.Path == symlinkOrStrmPath)
                    result = episodeFile.Id;
            }
        }

        // return the found episode-file-id
        return result;
    }

    private async Task<Int32?> GetSeriesId(String symlinkOrStrmPath)
    {
        // get series-id from cache
        var cachedSeriesId = UsenetPathUtil.GetAllParentDirectories(symlinkOrStrmPath)
            .Where(x => SeriesPathToSeriesIdCache.ContainsKey(x))
            .Select(x => SeriesPathToSeriesIdCache[x])
            .Select(x => (Int32?)x)
            .FirstOrDefault();

        // if found, verify and return it
        if (cachedSeriesId != null)
        {
            var series = await GetSeries(cachedSeriesId.Value);
            if (symlinkOrStrmPath.StartsWith(series.Path!))
                return cachedSeriesId;
        }

        // otherwise, fetch all series and repopulate the cache
        Int32? result = null;
        foreach (var series in await GetAllSeries())
        {
            if (series.Path != null)
            {
                SeriesPathToSeriesIdCache[series.Path] = series.Id;
                if (symlinkOrStrmPath.StartsWith(series.Path))
                    result = series.Id;
            }
        }

        // return the found series-id
        return result;
    }
}
