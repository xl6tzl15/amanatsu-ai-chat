using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amanatsu.AiChat.Config;
using Amanatsu.AiChat.Sequence;

namespace Amanatsu.AiChat.LLM;

internal sealed class LlmClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(330) };
    private readonly Uri _endpoint;
    private readonly string _token;

    public LlmClient(string endpoint, string token)
    {
        _endpoint = new Uri(endpoint);
        _token = token;
    }

    public async Task<SequenceEnvelope> CompleteAsync(
        CharacterConfig character,
        IReadOnlyList<HistoryItem> history,
        string userText,
        IReadOnlyList<string> availableOutfitActions,
        IReadOnlyDictionary<string, string> currentOutfit,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            character = character.Id,
            character_name = character.Name,
            system_prompt = character.SystemPrompt,
            user_text = userText,
            history = history.Select(item => new { role = item.Role, text = item.Text }).ToArray(),
            available_expressions = character.Expressions.Keys.OrderBy(x => x).ToArray(),
            expression_descriptions = character.Expressions.ToDictionary(
                pair => pair.Key, pair => pair.Value?.Description ?? "", StringComparer.OrdinalIgnoreCase),
            available_motions = character.Motions.Keys.OrderBy(x => x).ToArray(),
            motion_descriptions = character.MotionDescriptions,
            language = L.Code,
            available_poses = GameAdapter.MotionCatalog.Poses.Select(p => p.Key).ToArray(),
            pose_descriptions = GameAdapter.MotionCatalog.Poses.ToDictionary(p => p.Key, p => p.Description),
            available_outfit_actions = availableOutfitActions,
            current_outfit = currentOutfit
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Show the bridge's own message (it is written for the player) instead of raw JSON.
            string message = null;
            try { message = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString(); } catch { }
            throw new HttpRequestException(message ?? L.T($"ブリッジがエラーを返しました（{(int)response.StatusCode}）", $"The bridge returned an error ({(int)response.StatusCode})"));
        }
        return JsonSerializer.Deserialize<SequenceEnvelope>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("Bridge returned an empty JSON document.");
    }

    public void Dispose() => _http.Dispose();
}

public sealed class HistoryItem
{
    public string Role { get; set; }
    public string Text { get; set; }

    public HistoryItem(string role, string text)
    {
        Role = role;
        Text = text;
    }
}
