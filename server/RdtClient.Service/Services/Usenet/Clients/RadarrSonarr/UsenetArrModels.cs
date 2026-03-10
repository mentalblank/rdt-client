using System.Text.Json.Serialization;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class ArrCommand
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("name")]
    public String Name { get; set; } = null!;

    [JsonPropertyName("commandName")]
    public String CommandName { get; set; } = null!;

    [JsonPropertyName("result")]
    public String Result { get; set; } = null!;

    [JsonPropertyName("status")]
    public String Status { get; set; } = null!;

    [JsonPropertyName("priority")]
    public String Priority { get; set; } = null!;
}

public class ArrRootFolder
{
    [JsonPropertyName("id")]
    public Int32 Id { get; set; }

    [JsonPropertyName("path")]
    public String? Path { get; set; }

    [JsonPropertyName("accessible")]
    public Boolean Accessible { get; set; }

    [JsonPropertyName("freeSpace")]
    public Int64 FreeSpace { get; set; }
}
