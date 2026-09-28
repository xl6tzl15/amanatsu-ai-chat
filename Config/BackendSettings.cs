using System.Text.Json;
using System.Text.Json.Nodes;

namespace Amanatsu.AiChat.Config;

internal sealed class BackendSettings
{
    public string BridgeEndpoint { get; set; } = "http://127.0.0.1:38429/v1/chat";
    public string BridgeToken { get; set; } = "";
    public string Provider { get; set; } = "ollama";
    public string UpstreamUrl { get; set; } = "http://127.0.0.1:11434/api/chat";
    public string Model { get; set; } = "gemma4:e4b";
    public string ApiKey { get; set; } = "";
    public bool LogThinking { get; set; }
    // 0 means "use the provider's default" (Ollama 512/4096, OpenAI-compatible 512/16384).
    public int MaxReplyTokens { get; set; }
    public int ContextTokens { get; set; }
    public int HistoryMessages { get; set; } = 30;

    public static BackendSettings Load(string path, string endpoint, string token)
    {
        var result = new BackendSettings { BridgeEndpoint = endpoint, BridgeToken = token };
        if (!File.Exists(path)) return result;
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Bridge settings must be a JSON object.");
        result.Provider = root["provider"]?.GetValue<string>() ?? result.Provider;
        result.UpstreamUrl = root["url"]?.GetValue<string>() ?? result.UpstreamUrl;
        result.Model = root["model"]?.GetValue<string>() ?? result.Model;
        result.ApiKey = root["api_key"]?.GetValue<string>() ?? "";
        result.LogThinking = root["log_thinking"]?.GetValue<bool>() ?? false;
        result.MaxReplyTokens = root["max_reply_tokens"]?.GetValue<int>() ?? 0;
        result.ContextTokens = root["context_tokens"]?.GetValue<int>() ?? 0;
        result.HistoryMessages = root["history_messages"]?.GetValue<int>() ?? 30;
        return result;
    }

    public void Save(string path)
    {
        if (Provider != "ollama" && Provider != "chat-completions")
            throw new InvalidDataException(L.T("接続方式が不正です。", "The provider is not valid."));
        ValidateUrl(BridgeEndpoint, L.T("ブリッジURL", "The bridge URL"));
        ValidateUrl(UpstreamUrl, "API URL");
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidDataException(L.T("モデル名を入力してください。", "Enter a model name."));
        if (MaxReplyTokens != 0 && (MaxReplyTokens < 64 || MaxReplyTokens > 32768))
            throw new InvalidDataException(L.T("返事の上限は空欄か64～32768にしてください。", "Max reply tokens must be empty or 64-32768."));
        if (ContextTokens != 0 && (ContextTokens < 1024 || ContextTokens > 262144))
            throw new InvalidDataException(L.T("文脈の長さは空欄か1024～262144にしてください。", "Context length must be empty or 1024-262144."));
        if (HistoryMessages < 0 || HistoryMessages > 200)
            throw new InvalidDataException(L.T("履歴の件数は0～200にしてください。", "History messages must be 0-200."));
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            : new JsonObject();
        if (root == null) throw new InvalidDataException("Bridge settings must be a JSON object.");
        root["provider"] = Provider;
        root["url"] = UpstreamUrl.Trim();
        root["model"] = Model.Trim();
        root["api_key"] = ApiKey.Trim();
        root["log_thinking"] = LogThinking;
        root["bridge_token"] = BridgeToken?.Trim() ?? "";
        root["max_reply_tokens"] = MaxReplyTokens;
        root["context_tokens"] = ContextTokens;
        root["history_messages"] = HistoryMessages;
        if (root["timeout_seconds"] == null) root["timeout_seconds"] = 300;
        if (root["options"] == null)
            root["options"] = new JsonObject { ["num_ctx"] = 4096, ["num_predict"] = 384 };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    private static void ValidateUrl(string value, string label)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException(L.T($"{label}には http(s) URL を指定してください。", $"{label} must be an http(s) URL."));
        if (!uri.IsLoopback && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException(L.T($"{label}のリモート接続には HTTPS が必要です。", $"{label} needs HTTPS for a remote connection."));
    }
}
