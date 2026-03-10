using System.Text.Json.Serialization;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class RadarrMovie
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("title")]
    public String? Title { get; set; }

    [JsonPropertyName("movieFile")]
    public RadarrMovieFile? MovieFile { get; set; }
}

public class RadarrMovieFile
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("path")]
    public String? Path { get; set; }
}
