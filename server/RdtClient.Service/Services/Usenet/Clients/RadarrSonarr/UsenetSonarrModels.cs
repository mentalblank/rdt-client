using System.Text.Json.Serialization;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class SonarrSeries
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("title")]
    public String? Title { get; set; }

    [JsonPropertyName("path")]
    public String? Path { get; set; }
}

public class SonarrEpisodeFile
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("seriesId")]
    public Int32 SeriesId { get; set; }

    [JsonPropertyName("path")]
    public String? Path { get; set; }
}

public class SonarrEpisode
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("seriesId")]
    public Int32 SeriesId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public Int32 SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public Int32 EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public String? Title { get; set; }
}
