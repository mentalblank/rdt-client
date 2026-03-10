using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;

public class UsenetArrClient(String host, String apiKey)
{
    protected static readonly HttpClient HttpClient = new();

    public String Host { get; } = host;
    private String ApiKey { get; } = apiKey;
    private const String BasePath = "/api/v3";

    public virtual Task<Boolean> RemoveAndSearch(String symlinkOrStrmPath) =>
        throw new InvalidOperationException();

    public Task<List<ArrRootFolder>> GetRootFolders() =>
        Get<List<ArrRootFolder>>($"/rootfolder");

    public Task<ArrCommand> CommandAsync(Object command) =>
        Post<ArrCommand>($"/command", command);

    protected Task<T> Get<T>(String path) =>
        GetRoot<T>($"{BasePath}{path}");

    protected async Task<T> GetRoot<T>(String rootPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Host.TrimEnd('/')}{rootPath}");
        using var response = await SendAsync(request);
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(stream) ?? throw new NullReferenceException();
    }

    protected async Task<T> Post<T>(String path, Object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetRequestUri(path));
        var jsonBody = JsonSerializer.Serialize(body);
        request.Content = new StringContent(jsonBody, new MediaTypeHeaderValue("application/json"));
        using var response = await SendAsync(request);
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(stream) ?? throw new NullReferenceException();
    }

    protected async Task<HttpStatusCode> Delete(String path, Dictionary<String, String>? queryParams = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, GetRequestUri(path, queryParams));
        using var response = await SendAsync(request);
        return response.StatusCode;
    }

    private String GetRequestUri(String path, Dictionary<String, String>? queryParams = null)
    {
        queryParams ??= [];
        var resource = $"{Host.TrimEnd('/')}{BasePath}{path}";
        var query = queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
        var queryString = String.Join("&", query);
        if (queryString.Length > 0) resource = $"{resource}?{queryString}";
        return resource;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.Add("X-Api-Key", ApiKey);
        return HttpClient.SendAsync(request);
    }
}
