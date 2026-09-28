using System.Net.Http.Headers;
using System.Text.Json;

namespace Amanatsu.AiChat.LLM;

// Lists the models installed on the configured upstream so the user picks instead of typing names.
internal static class ModelCatalog
{
    public static async Task<string[]> ListAsync(string provider, string upstreamUrl, string apiKey)
    {
        var upstream = new Uri(upstreamUrl);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        Uri listUri;
        if (provider == "ollama")
            listUri = new UriBuilder(upstream) { Path = "/api/tags", Query = "" }.Uri;
        else
        {
            var path = upstream.AbsolutePath;
            path = path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? path[..^"/chat/completions".Length] + "/models" : "/v1/models";
            listUri = new UriBuilder(upstream) { Path = path, Query = "" }.Uri;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var names = new List<string>();
        if (root.TryGetProperty("models", out var models))
            foreach (var model in models.EnumerateArray())
                if (model.TryGetProperty("name", out var name)) names.Add(name.GetString());
        if (root.TryGetProperty("data", out var data))
            foreach (var model in data.EnumerateArray())
                if (model.TryGetProperty("id", out var id)) names.Add(id.GetString());
        return names.Where(n => !string.IsNullOrWhiteSpace(n) && (provider == "ollama" || IsChatModel(n)))
            .Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // OpenAI-style lists also contain audio, image and embedding models that cannot chat.
    private static readonly string[] NonChatMarkers =
    {
        "embedding", "tts", "whisper", "transcribe", "dall-e", "image", "moderation", "audio",
        "realtime", "search", "davinci", "babbage", "sora", "computer-use"
    };

    private static bool IsChatModel(string name) =>
        !NonChatMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
