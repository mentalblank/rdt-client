using System.Net;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class UsenetRadarrClient(String host, String apiKey) : UsenetArrClient(host, apiKey)
{
    private static readonly Dictionary<String, Int32> SymlinkOrStrmToMovieIdCache = [];

    public Task<RadarrMovie> GetMovieAsync(Int32 id) =>
        Get<RadarrMovie>($"/movie/{id}");

    public Task<List<RadarrMovie>> GetMoviesAsync() =>
        Get<List<RadarrMovie>>($"/movie");

    public Task<HttpStatusCode> DeleteMovieFile(Int32 id) =>
        Delete($"/moviefile/{id}");

    public Task<ArrCommand> SearchMovieAsync(Int32 id) =>
        CommandAsync(new { name = "MoviesSearch", movieIds = new List<Int32> { id } });


    public override async Task<Boolean> RemoveAndSearch(String symlinkOrStrmPath)
    {
        var mediaIds = await GetMediaIds(symlinkOrStrmPath);
        if (mediaIds == null) return false;

        if (await DeleteMovieFile(mediaIds.Value.movieFileId) != HttpStatusCode.OK)
            throw new Exception($"Failed to delete movie file `{symlinkOrStrmPath}` from radarr instance `{Host}`.");

        await SearchMovieAsync(mediaIds.Value.movieId);
        return true;
    }

    private async Task<(Int32 movieFileId, Int32 movieId)?> GetMediaIds(String symlinkOrStrmPath)
    {
        // if we already have the movie-id cached
        // then let's use it to find and return the corresponding movie-file-id
        if (SymlinkOrStrmToMovieIdCache.TryGetValue(symlinkOrStrmPath, out var movieId))
        {
            var movie = await GetMovieAsync(movieId);
            if (movie.MovieFile?.Path == symlinkOrStrmPath)
                return (movie.MovieFile.Id, movieId);
        }

        // otherwise, let's fetch all movies, cache all movie files
        // and return the matching movie-id and movie-file-id
        var allMovies = await GetMoviesAsync();
        (Int32 movieFileId, Int32 movieId)? result = null;
        foreach (var movie in allMovies)
        {
            var movieFile = movie.MovieFile;
            if (movieFile?.Path != null)
                SymlinkOrStrmToMovieIdCache[movieFile.Path] = movie.Id;
            if (movieFile?.Path == symlinkOrStrmPath)
                result = (movieFile.Id, movie.Id);
        }

        return result;
    }
}
