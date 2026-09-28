using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Net.Http.Headers;
using System.Text.Json;
using Amanatsu.AiChat.Config;
using Amanatsu.AiChat.GameAdapter;
using Amanatsu.AiChat.LLM;
using Amanatsu.AiChat.Sequence;
using Amanatsu.AiChat.UI;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amanatsu.AiChat;

[BepInPlugin("amanatsu.ai-chat", ModIdentity.Name, ModIdentity.Version)]
public sealed class Plugin : BasePlugin
{
    private static int _capturedUnityErrors;
    private static void CaptureUnityError(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception || !condition.Contains("NullReferenceException") ||
            System.Threading.Interlocked.Increment(ref _capturedUnityErrors) > 5) return;
        var path = Path.GetFullPath("BepInEx/config/amanatsu.ai-chat/diagnostics/unity-errors.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, condition + "\n" + stackTrace + "\n---\n");
    }

    public override void Load()
    {
        AiChatBehaviour.LogSource = Log;
        L.Init(Config.Bind("UI", "Language", "auto", "UI and conversation language: auto (follows the installed package), ja, or en.").Value,
            Path.GetDirectoryName(typeof(AiChatBehaviour).Assembly.Location));
        AiChatBehaviour.EndpointConfig = Config.Bind("Bridge", "Endpoint", "http://127.0.0.1:38429/v1/chat", "Local LLM bridge endpoint.");
        AiChatBehaviour.TokenConfig = Config.Bind("Bridge", "BearerToken", "", "Optional local bridge bearer token.");
        AiChatBehaviour.Endpoint = AiChatBehaviour.EndpointConfig.Value;
        AiChatBehaviour.Token = AiChatBehaviour.TokenConfig.Value;
        AiChatBehaviour.BackendSettingsPath = Config.Bind("Bridge", "BackendSettingsPath", "BepInEx/config/amanatsu.ai-chat/bridge.json", "Generated bridge settings JSON; the cfg is the source for UI-editable settings.").Value;
        BackendSettings backendDefaults;
        try { backendDefaults = BackendSettings.Load(Path.GetFullPath(AiChatBehaviour.BackendSettingsPath), AiChatBehaviour.Endpoint, AiChatBehaviour.Token); }
        catch (Exception ex)
        {
            Log.LogWarning($"Existing bridge JSON could not be read; using safe defaults: {ex.GetType().Name}");
            backendDefaults = new BackendSettings { BridgeEndpoint = AiChatBehaviour.Endpoint, BridgeToken = AiChatBehaviour.Token };
        }
        AiChatBehaviour.Settings = new ModSettings(Config, backendDefaults);
        AiChatBehaviour.Graphics = new GraphicPreferences(Config);
        AiChatBehaviour.CharacterConfigPath = Config.Bind("Character", "ConfigPath", "BepInEx/config/amanatsu.ai-chat/default.json", "Character prompt and mapping JSON.").Value;
        // The English edition keeps its prompts apart from the Japanese ones.
        if (L.En && AiChatBehaviour.CharacterConfigPath == "BepInEx/config/amanatsu.ai-chat/default.json")
            AiChatBehaviour.CharacterConfigPath = "BepInEx/config/amanatsu.ai-chat/default-en.json";
        AiChatBehaviour.CardPathConfig = Config.Bind("Character", "CardPath", "", "Last selected female character card. Empty opens the file picker when chat is opened.");
        AiChatBehaviour.CharacterCardPath = AiChatBehaviour.CardPathConfig.Value;
        AiChatBehaviour.OpenOnStartup = Config.Bind("UI", "OpenOnStartup", false, "Open the dedicated chat UI immediately, including on the title screen. Normally leave this disabled and use the configured OpenChat key.").Value;
        AiChatBehaviour.DiagnosticsEnabled = Config.Bind("Testing", "DiagnosticsEnabled", false, "Opt-in local file test commands and state readback. No network listener.").Value;
        if (AiChatBehaviour.DiagnosticsEnabled)
        {
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.add_logMessageReceived((Action<string, string, LogType>)CaptureUnityError);
        }
        AddComponent<AiChatBehaviour>();
        Log.LogInfo($"{ModIdentity.Label} loaded. Dedicated UI is available without entering the main game.");
    }
}

public sealed class AiChatBehaviour : MonoBehaviour
{
    private static readonly JsonSerializerOptions DialogueLogOptions = new()
    { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static BepInEx.Logging.ManualLogSource LogSource;
    internal static ModSettings Settings;
    internal static GraphicPreferences Graphics;
    internal static string Endpoint = "";
    internal static string Token = "";
    internal static ConfigEntry<string> EndpointConfig;
    internal static ConfigEntry<string> TokenConfig;
    internal static ConfigEntry<string> CardPathConfig;
    internal static string BackendSettingsPath = "";
    internal static string CharacterConfigPath = "";
    internal static string CharacterCardPath = "";
    internal static bool OpenOnStartup;
    internal static bool DiagnosticsEnabled;
    private DiagnosticsFile _diagnostics;

    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly List<HistoryItem> _history = new();
    private CharacterConfig _character;
    private CharacterConfig _baseCharacter;
    private Dictionary<string, ExpressionPreset> _globalExpressions;
    private string _profileCardPath;
    private CharacterAdapter _adapter;
    private SequenceRunner _runner;
    private LlmClient _client;
    private CancellationTokenSource _requestCancellation;
    private string _transcript = L.T("AI会話専用モード\n本編を開始せず、この画面だけで会話できます。\n", "AI chat mode\nTalk here without starting the main game.\n");
    private string _status = "initializing";
    private bool _visible;
    private bool _requesting;
    private bool _bridgeRestarting;
    private float _restartConfirmationUntil;
    private DedicatedChatUi _ui;
    private DedicatedCharacterStage _stage;
    private int _requestGeneration;
    private bool _greetingPending;
    private bool _greetingInFlight;
    private static string GreetingInstruction => L.T(
        "（会話が始まりました。あなたから相手に、キャラクターらしい自然な最初のひと言を一つだけ話しかけてください。候補を並べたり、かぎかっこで区切ったりしないでください。）",
        "(The conversation has just started. Say one natural opening line to the other person, in character. Do not list options or wrap the line in quotation marks.)");
    private TitleEntryUi _titleEntry;
    private bool _optionsPending;
    private bool _optionsSeen;
    private float _optionsDeadline;
    private bool OptionsBusy => _optionsPending || AL.Config.ConfigWindow.IsActive || AL.Config.ConfigWindow.IsTransition;

    public AiChatBehaviour(IntPtr pointer) : base(pointer) { }

    private void Start()
    {
        _visible = false;
        try
        {
            try { LoadBackendSettings().Save(Path.GetFullPath(BackendSettingsPath)); }
            catch (Exception ex) { LogSource?.LogWarning($"Bridge JSON was not synchronized from cfg: {ex.Message}"); }
            var path = Path.GetFullPath(CharacterConfigPath);
            _baseCharacter = CharacterConfig.LoadOrCreate(path);
            _character = _baseCharacter;
            if (ExpressionCatalog.IsLegacyDefault(_baseCharacter.Expressions))
                _baseCharacter.Expressions = ExpressionCatalog.Defaults();
            try { _globalExpressions = GlobalExpressionPresets.LoadOrCreate(
                GlobalExpressionPresets.PathOnDisk, _baseCharacter.Expressions); }
            catch (Exception ex)
            {
                LogSource?.LogWarning($"Global expression presets could not be loaded; using in-memory defaults: {ex.Message}");
                _globalExpressions = ExpressionCatalog.Defaults();
            }
            _baseCharacter.Expressions = _globalExpressions;
            _baseCharacter.MotionDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
            MotionCatalog.FillMissingDescriptions(_baseCharacter.MotionDescriptions, _baseCharacter.Motions.Keys);
            MigrateMotions(_baseCharacter);
            _client = new LlmClient(Endpoint, Token);
            _ui = new DedicatedChatUi(Send, RunUiTest, RunDemo, Cancel, () => SetVisible(false),
                () => SwitchCharacter(-1), () => SwitchCharacter(1), ChooseCharacterFile, SavePersonalityPrompt, LoadPersonalityFile, SavePersonalityFile,
                LoadBackendSettings, SaveBackendSettings, TestBackendConnection, RestartBridge, () => _stage?.Rotate(-15f), () => _stage?.Rotate(15f),
                () => _stage?.Zoom(-1f), () => _stage?.Zoom(1f), AdjustFace,
                () => _stage?.ToggleEyeLookMode(), () => _stage?.ToggleNeckLookMode(), SwitchCoordinate, CyclePose, Settings);
            _stage = new DedicatedCharacterStage(_ui.Root, LogSource!, Settings);
            _titleEntry = new TitleEntryUi(_ui.Font, () => SetVisible(true));
            _ui.AddEnvironmentControls(OpenNativeOptions, p => _stage.SetCameraPreset(p),
                () => { Graphics.ShadowType.Value = (LightShadows)(((int)Graphics.ShadowType.Value + 1) % 3); _stage.RefreshGraphics(); },
                () => L.T("影: ", "Shadow: ") + Graphics.ShadowType.Value,
                CycleBackground, ChooseBackgroundImage, () => BackgroundBackdrop.Label(Settings.BackgroundImage.Value));
            _ui.AddModelPicker(RequestModelList);
            _ui.AddLightControls(
                () => (Settings.LightVertical.Value, Settings.LightHorizontal.Value, Settings.LightIntensity.Value,
                    ColorUtility.TryParseHtmlString(Settings.LightColor.Value, out var lightColor) ? lightColor : Color.white),
                AdjustLight, ChooseLightColor, ResetLight);
            _ui.AddInlineExpressionControls(GlobalExpressions, SaveCurrentExpressionPreset,
                ApplySavedExpressionPreset, AdjustFaceValue, DeleteExpressionPreset);
            var stageReady = _visible && _stage.Enter(CharacterCardPath);
            _adapter = new CharacterAdapter(_character, LogSource!, () => _stage?.Human, motionId => _stage?.PlayMotion(motionId) == true) { PoseSetter = ApplyPoseFromAi };
            _runner = new SequenceRunner(_adapter, text => { _stage.ShowText(text); Append($"{_stage.CharacterName ?? _character.Name}: {text}"); }, LogSource!);
            _ui.SetVisible(_visible);
            if (OpenOnStartup) SetVisible(true);
            if (DiagnosticsEnabled) _diagnostics = new DiagnosticsFile(LogSource);
            _status = _visible ? "loading dedicated stage" : L.T($"タイトルから{ModIdentity.Label}を開始できます", $"Start {ModIdentity.Label} from the title screen");
            LogSource?.LogInfo($"Dedicated UI initialized. visible={_visible}, screen={Screen.width}x{Screen.height}");
        }
        catch (Exception ex)
        {
            _status = "initialization failed";
            LogSource?.LogError(ex);
        }
    }

    private void Update()
    {
        UpdateNativeOptions();
        _titleEntry?.Tick(_visible, OptionsBusy);
        if (_visible && SceneManager.GetActiveScene().name != "Title")
            SetVisible(false);
        if (!OptionsBusy && Settings.OpenChat.Value.IsDown())
            SetVisible(!_visible);
        while (_mainThread.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Cancel(); _status = L.T("処理に失敗しました", "The operation failed"); LogSource?.LogError(ex); }
        }
        var sequenceWasRunning = _runner?.IsRunning == true;
        _runner?.SetPaused(OptionsBusy);
        _runner?.Update();
        _stage?.Maintain();
        if (_visible && !OptionsBusy && _stage?.IsReady == true
            && _ui?.InputFocused != true && _ui?.ModalOpen != true)
        {
            if (Settings.ToggleEyes.Value.IsDown() || Settings.ToggleEyesSecondary.Value.IsDown()) _stage.ToggleEyeLookMode();
            if (Settings.ToggleNeck.Value.IsDown() || Settings.ToggleNeckSecondary.Value.IsDown()) _stage.ToggleNeckLookMode();
            if (Settings.ToggleMenu.Value.IsDown()) _ui.SetMenuVisible(!_ui.MenuVisible);
        }
        if (_stage?.IsReady == true && _stage.ActiveCardPath != _profileCardPath)
        {
            try { BindCharacterProfile(); }
            catch (Exception ex) { _status = "character profile failed"; LogSource?.LogError(ex); }
        }
        _diagnostics?.Tick(ExecuteTestCommand, DiagnosticState);
        if (_visible) _stage?.UpdateView(_ui?.InputFocused == true || _ui?.ModalOpen == true || OptionsBusy);
        if (_greetingPending && _visible && !OptionsBusy && _stage?.IsReady == true && _client != null
            && !_requesting && !_bridgeRestarting && _runner is { IsRunning: false })
        {
            _greetingPending = false;
            RequestReply(GreetingInstruction, greeting: true);
        }
        if (_stage?.IsReady == true && (_status == "loading dedicated stage" || _status == "stage initialization failed"))
            _status = "ready / dedicated stage";
        if (sequenceWasRunning && _runner?.IsRunning == false)
            _status = _stage?.IsReady == true ? "ready / dedicated stage" : "loading dedicated stage";
        _ui?.Refresh(
            _status,
            L.T($"表示キャラクター: {_stage?.CharacterName ?? "読み込み中"}", $"Character: {_stage?.CharacterName ?? "loading"}"),
            _transcript,
            _stage?.IsReady == true && (!_requesting || _greetingInFlight) && !_bridgeRestarting && (_runner is { IsRunning: false } || _greetingInFlight),
            _stage?.IsReady == true && !_requesting && _runner is { IsRunning: false },
            _adapter?.HasCharacter == true,
            _stage?.EyesFollowCamera == true,
            _stage?.NeckFollowsCamera == true,
            _adapter?.FaceParts() ?? (-1, -1, -1, 0, 0, 0),
            _adapter?.CaptureExpression());
        _ui?.SetPose(_stage?.IsReady == true ? _stage.IdlePose : -1,
            _stage?.IsReady == true && !_requesting && _runner?.IsRunning != true);
        _ui?.SetCoordinate(_stage?.IsReady == true ? _stage.Coordinate : -1,
            _stage?.IsReady == true && !_requesting && _runner?.IsRunning != true);
    }

    [HideFromIl2Cpp]
    private IReadOnlyDictionary<string, ExpressionPreset> GlobalExpressions() => _globalExpressions;

    [HideFromIl2Cpp]
    private object DiagnosticState() => new { status = _status, requesting = _requesting,
            running = _runner?.IsRunning == true, historyCount = _history.Count,
            stage = _stage?.Diagnostics(), expression = _adapter?.ExpressionDiagnostics(),
            outfit = _adapter?.OutfitDiagnostics(), titleEntry = _titleEntry?.Visible,
            optionsBusy = OptionsBusy, nativeWindowVisible = _stage?.NativeWindowVisible, graphics = Graphics.Diagnostics() };

    private void SetVisible(bool visible)
    {
        if (visible && SceneManager.GetActiveScene().name != "Title") return;
        if (visible && (string.IsNullOrWhiteSpace(CharacterCardPath) || !File.Exists(CharacterCardPath)))
        {
            var selected = NativeFilePicker.ChooseCard(null);
            if (selected == null) return;
            CharacterCardPath = selected;
        }
        _visible = visible;
        if (visible) { _stage?.Enter(CharacterCardPath); _greetingPending = true; }
        else { Cancel(); _adapter?.RestoreOutfit(); _stage?.Exit(); }
        _ui?.SetVisible(visible);
    }

    private void OpenNativeOptions()
    {
        if (OptionsBusy) return;
        _optionsPending = true; _optionsSeen = false; _optionsDeadline = Time.unscaledTime + 15f;
        _ui.Suspend(true);
        try { AL.Config.ConfigWindow.Load(); }
        catch { _optionsPending = false; _ui.Suspend(false); throw; }
    }

    private void UpdateNativeOptions()
    {
        var active = AL.Config.ConfigWindow.IsActive || AL.Config.ConfigWindow.IsTransition;
        if (!_optionsPending)
        {
            if (_visible && active) { _optionsPending = true; _optionsSeen = true; _ui.Suspend(true); }
            return;
        }
        if (active) { _optionsSeen = true; return; }
        if (!_optionsSeen && Time.unscaledTime < _optionsDeadline) return;
        _optionsPending = false;
        if (_optionsSeen) { Graphics.OptionsClosed(); _stage?.RefreshGraphics(); }
        else { _status = L.T("環境設定を開けませんでした", "Could not open the options"); LogSource?.LogWarning(_status); }
        if (_visible) _ui.Suspend(false);
    }

    [HideFromIl2Cpp]
    private BackendSettings LoadBackendSettings() => Settings.BackendValues(Endpoint, Token);

    [HideFromIl2Cpp]
    private void SaveBackendSettings(BackendSettings settings)
    {
        settings.Save(Path.GetFullPath(BackendSettingsPath));
        Settings.Provider.Value = settings.Provider;
        Settings.UpstreamUrl.Value = settings.UpstreamUrl.Trim();
        Settings.Model.Value = settings.Model.Trim();
        Settings.ApiKey.Value = settings.ApiKey;
        Settings.LogThinking.Value = settings.LogThinking;
        Settings.MaxReplyTokens.Value = settings.MaxReplyTokens;
        Settings.ContextTokens.Value = settings.ContextTokens;
        Settings.HistoryMessages.Value = settings.HistoryMessages;
        Cancel();
        _client?.Dispose();
        EndpointConfig.Value = Endpoint = settings.BridgeEndpoint.Trim();
        TokenConfig.Value = Token = settings.BridgeToken;
        _client = new LlmClient(Endpoint, Token);
        _status = L.T("LLM接続設定を保存しました", "Saved the LLM connection settings");
        LogSource?.LogInfo($"LLM backend settings saved: provider={settings.Provider}, model={settings.Model}. API key not logged.");
    }

    private void RequestModelList()
    {
        var provider = _ui.SettingsProvider;
        var url = _ui.SettingsUpstreamUrl;
        var key = _ui.SettingsApiKey;
        _ui.ShowModelChoices(Array.Empty<string>(), L.T("モデル一覧を取得しています…", "Fetching the model list…"));
        _ = Task.Run(async () =>
        {
            string[] names;
            string message = null;
            try
            {
                names = await ModelCatalog.ListAsync(provider, url, key);
                if (names.Length == 0) message = L.T("導入済みのモデルが見つかりませんでした。", "No installed models were found.");
            }
            catch (Exception ex)
            {
                names = Array.Empty<string>();
                var reason = ex is HttpRequestException && ex.Message.StartsWith("HTTP ", StringComparison.Ordinal) ? ex.Message : ex.GetType().Name;
                message = L.T($"一覧を取得できませんでした（{reason}）。LLM API URL・APIキー・サーバーの起動を確認してください。", $"Could not fetch the list ({reason}). Check the LLM API URL, the API key and that the server is running.");
            }
            _mainThread.Enqueue(() => _ui?.ShowModelChoices(names, message));
        });
    }

    private void TestBackendConnection()
    {
        _ui?.SetSettingsFeedback(L.T("ブリッジを確認中…", "Checking the bridge…"));
        var endpoint = Endpoint;
        var bearer = Token;
        _ = Task.Run(async () =>
        {
            string result;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var health = new UriBuilder(endpoint) { Path = "/health", Query = "" }.Uri;
                using var request = new HttpRequestMessage(HttpMethod.Get, health);
                if (!string.IsNullOrWhiteSpace(bearer))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;
                var provider = root.TryGetProperty("provider", out var p) ? p.GetString() : "unknown";
                var model = root.TryGetProperty("model", out var m) ? m.GetString() : "unknown";
                var thinking = root.TryGetProperty("thinking_enabled", out var t)
                    ? (t.GetBoolean() ? L.T("思考ON", "thinking ON") : L.T("思考OFF", "thinking OFF")) : L.T("思考設定未対応の旧版", "old bridge without thinking setting");
                result = L.T($"ブリッジ応答あり: {provider} / {model} / {thinking}（モデル生成は未確認）", $"Bridge responded: {provider} / {model} / {thinking} (generation not tested)");
                try
                {
                    var installed = await ModelCatalog.ListAsync(Settings.Provider.Value, Settings.UpstreamUrl.Value, Settings.ApiKey.Value);
                    if (installed.Length > 0 && !installed.Contains(model, StringComparer.Ordinal))
                        result = L.T($"モデル「{model}」が見つかりません。『一覧から選ぶ…』で選び直してください。", $"Model \"{model}\" was not found. Pick one with \"Choose…\".");
                }
                catch (Exception listError) { result += L.T($" ／ モデル一覧は確認できませんでした（{listError.GetType().Name}）", $" / could not check the model list ({listError.GetType().Name})"); }
            }
            catch (Exception ex) { result = L.T($"接続失敗: {ex.GetType().Name}", $"Connection failed: {ex.GetType().Name}"); }
            _mainThread.Enqueue(() => _ui?.SetSettingsFeedback(result));
        });
    }

    private void RestartBridge()
    {
        if (_bridgeRestarting) return;
        if (_restartConfirmationUntil <= 0f || Time.realtimeSinceStartup > _restartConfirmationUntil)
        {
            _restartConfirmationUntil = Time.realtimeSinceStartup + 10f;
            _ui?.SetSettingsFeedback(L.T("ブリッジを再起動するには、10秒以内にもう一度押してください。", "Press again within 10 seconds to restart the bridge."));
            return;
        }
        _restartConfirmationUntil = 0f;
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
        {
            _ui?.SetSettingsFeedback(L.T("再起動できるのはローカルブリッジのみです。", "Only a local bridge can be restarted."));
            return;
        }
        var root = Directory.GetCurrentDirectory();
        // The Mod folder can be nested under plugins (e.g. plugins/SELF), so look beside this DLL first.
        var pluginDirectory = Path.GetDirectoryName(typeof(AiChatBehaviour).Assembly.Location) ?? root;
        var helper = new[]
        {
            Path.Combine(pluginDirectory, "AmanatsuAiChat", "restart_bridge.py"),
            Path.Combine(root, "BepInEx", "plugins", "AmanatsuAiChat", "restart_bridge.py"),
            Path.Combine(root, "ModSource", "AiChat", "restart_bridge.py")
        }.FirstOrDefault(File.Exists) ?? "";
        if (!File.Exists(helper))
        {
            _ui?.SetSettingsFeedback(L.T("再起動ヘルパーが見つかりません。", "The restart helper was not found."));
            return;
        }
        Cancel();
        _bridgeRestarting = true;
        _status = L.T("ブリッジ再起動中", "Restarting the bridge");
        _ui?.SetSettingsFeedback(L.T("対象を確認して再起動中…", "Checking the target and restarting…"));
        var config = Path.GetFullPath(BackendSettingsPath);
        var port = endpoint.Port;
        _ = Task.Run(async () =>
        {
            string result;
            try
            {
                var start = new ProcessStartInfo("python")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add(helper);
                start.ArgumentList.Add("--root");
                start.ArgumentList.Add(root);
                start.ArgumentList.Add("--config");
                start.ArgumentList.Add(config);
                start.ArgumentList.Add("--port");
                start.ArgumentList.Add(port.ToString());
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Restart helper could not start.");
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                using var document = JsonDocument.Parse(output);
                var response = document.RootElement;
                if (process.ExitCode != 0 || !response.GetProperty("ok").GetBoolean())
                    result = L.T("再起動失敗: ", "Restart failed: ") + response.GetProperty("error").GetString();
                else
                {
                    var bridge = response.GetProperty("health");
                    var model = bridge.GetProperty("model").GetString();
                    var thinking = bridge.GetProperty("thinking_enabled").GetBoolean() ? L.T("思考ON", "thinking ON") : L.T("思考OFF", "thinking OFF");
                    result = L.T($"再起動完了: {model} / {thinking}", $"Restarted: {model} / {thinking}");
                }
            }
            catch (Exception ex) { result = L.T($"再起動失敗: {ex.GetType().Name}", $"Restart failed: {ex.GetType().Name}"); }
            _mainThread.Enqueue(() =>
            {
                _bridgeRestarting = false;
                _status = result.StartsWith(L.T("再起動完了", "Restarted"), StringComparison.Ordinal) ? L.T("会話できます", "Ready to talk") : L.T("ブリッジ再起動失敗", "Bridge restart failed");
                _ui?.SetSettingsFeedback(result);
            });
        });
    }

    private void SwitchCharacter(int direction)
    {
        Cancel();
        try { _adapter?.RestoreOutfit(); _stage?.SelectCard(direction); BindCharacterProfile(); _history.Clear(); _status = L.T("会話できます", "Ready to talk"); }
        catch (Exception ex) { LogSource?.LogError(ex); _status = "character load failed"; }
    }

    // The AI's pose choice is persisted like the manual button so the idle stays after the reply.
    private bool ApplyPoseFromAi(int pose)
    {
        if (_stage?.IsReady != true || !_stage.SetIdlePose(pose)) return false;
        _character.IdlePose = pose;
        try
        {
            if (!string.IsNullOrWhiteSpace(_stage.ActiveCardPath))
                CharacterConfig.SaveForCard(Path.GetFullPath(CharacterConfigPath), _stage.ActiveCardPath, _character);
        }
        catch (Exception ex) { LogSource?.LogWarning("pose save failed: " + ex.Message); }
        return true;
    }

    private void CyclePose(int direction)
    {
        if (_stage?.IsReady != true || _requesting || _runner?.IsRunning == true) return;
        var poses = _stage.AvailableIdlePoses();
        if (poses.Length == 0) return;
        var index = Array.IndexOf(poses, _stage.IdlePose);
        var next = poses[((index < 0 ? 0 : index + direction) % poses.Length + poses.Length) % poses.Length];
        if (!_stage.SetIdlePose(next)) return;
        _character.IdlePose = next;
        try
        {
            if (!string.IsNullOrWhiteSpace(_stage.ActiveCardPath))
                CharacterConfig.SaveForCard(Path.GetFullPath(CharacterConfigPath), _stage.ActiveCardPath, _character);
            _status = L.T($"待機ポーズを {next + 1:00} にしました", $"Idle pose set to {next + 1:00}");
        }
        catch (Exception ex) { _status = L.T("ポーズの保存に失敗: ", "Could not save the pose: ") + ex.Message; LogSource?.LogWarning(_status); }
    }

    private void SwitchCoordinate()
    {
        if (_stage?.IsReady != true || _requesting || _runner?.IsRunning == true) return;
        var states = _adapter?.CaptureOutfitStates();
        if (!_stage.SwitchCoordinate()) { _status = L.T("衣装を切り替えられませんでした", "Could not switch the outfit"); return; }
        if (states != null) _adapter.ApplyOutfitStates(states);
        _status = L.T("衣装を切り替えました", "Switched the outfit");
    }

    private void ChooseCharacterFile()
    {
        if (_stage?.IsReady != true) { _status = L.T("キャラ画面の準備ができていません", "The character stage is not ready"); return; }
        try
        {
            var fullPath = NativeFilePicker.ChooseCard(_stage.ActiveCardPath);
            if (fullPath == null) return;
            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
                throw new InvalidDataException(L.T("存在するキャラカード（.png）を指定してください", "Choose an existing character card (.png)"));
            Cancel();
            _adapter?.RestoreOutfit();
            _stage.SelectCard(fullPath);
            BindCharacterProfile();
            _history.Clear();
            _status = L.T("キャラを読み込みました", "Loaded the character");
        }
        catch (Exception ex)
        {
            _status = L.T($"カード読込失敗: {ex.Message}", $"Could not load the card: {ex.Message}");
            LogSource?.LogError(ex);
        }
    }

    private void CycleBackground(int direction)
    {
        try
        {
            _stage.SetBackground(BackgroundBackdrop.Next(Settings.BackgroundImage.Value, direction));
            _status = L.T("背景を変更しました", "Changed the background");
        }
        catch (Exception ex) { _status = L.T($"背景変更失敗: {ex.Message}", $"Could not change the background: {ex.Message}"); LogSource?.LogWarning(_status); }
    }

    private void ChooseBackgroundImage()
    {
        try
        {
            var selected = NativeFilePicker.ChooseBackground(Settings.BackgroundImage.Value);
            if (selected == null) return;
            _stage.SetBackground(selected);
            _status = L.T("背景画像を変更しました", "Changed the background image");
        }
        catch (Exception ex) { _status = L.T($"背景変更失敗: {ex.Message}", $"Could not change the background: {ex.Message}"); LogSource?.LogWarning(_status); }
    }

    private void AdjustLight(string part, float delta)
    {
        switch (part)
        {
            case "vertical": Settings.LightVertical.Value = Mathf.Clamp(Settings.LightVertical.Value + delta, -90f, 90f); break;
            case "horizontal": Settings.LightHorizontal.Value = Mathf.Repeat(Settings.LightHorizontal.Value + delta + 180f, 360f) - 180f; break;
            case "intensity": Settings.LightIntensity.Value = Mathf.Clamp(Mathf.Round((Settings.LightIntensity.Value + delta) * 10f) / 10f, 0f, 3f); break;
        }
        _stage?.ApplyLightSettings();
    }

    private void ChooseLightColor()
    {
        try
        {
            var color = NativeFilePicker.ChooseColor(_stage?.LightColor ?? Color.white);
            if (color == null) return;
            Settings.LightColor.Value = "#" + ColorUtility.ToHtmlStringRGBA(color.Value);
            _stage?.ApplyLightSettings();
        }
        catch (Exception ex) { _status = ex.Message; LogSource?.LogWarning(ex.Message); }
    }

    private void ResetLight()
    {
        var d = DedicatedCharacterStage.LightDefaults;
        Settings.LightVertical.Value = d.Vertical;
        Settings.LightHorizontal.Value = d.Horizontal;
        Settings.LightIntensity.Value = d.Intensity;
        Settings.LightColor.Value = d.Color;
        _stage?.ApplyLightSettings();
    }

    private void AdjustFace(string part, int delta)
    {
        if (_adapter == null || _requesting || _runner?.IsRunning == true) return;
        var face = _adapter.FaceParts();
        var current = part switch { "eyebrow" => face.eyebrow, "eyes" => face.eyes, "mouth" => face.mouth, _ => -1 };
        if (current < 0) return;
        _adapter.SetFacePart(part, current + delta);
    }

    private bool SavePersonalityPrompt(string prompt)
    {
        if (_stage?.IsReady != true || _character == null || string.IsNullOrWhiteSpace(_stage.ActiveCardPath))
        {
            _status = L.T("キャラが選択されていません", "No character is selected");
            return false;
        }
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4000)
        {
            _status = L.T("性格・口調の指示は1～4000文字で入力してください", "The personality text must be 1 to 4000 characters");
            return false;
        }
        try
        {
            var previousPrompt = _character.SystemPrompt;
            _character.SystemPrompt = prompt.Trim();
            try { CharacterConfig.SaveForCard(Path.GetFullPath(CharacterConfigPath), _stage.ActiveCardPath, _character); }
            catch { _character.SystemPrompt = previousPrompt; throw; }
            _ui?.SetPersonalityPrompt(_character.SystemPrompt);
            _status = L.T("性格・口調の指示を保存しました（次の会話から反映）", "Saved the personality (used from the next message)");
            return true;
        }
        catch (Exception ex)
        {
            _status = L.T($"性格設定の保存に失敗: {ex.Message}", $"Could not save the personality: {ex.Message}");
            LogSource?.LogError(ex);
            return false;
        }
    }

    [HideFromIl2Cpp]
    private bool DeleteExpressionPreset(string key)
    {
        key = key?.Trim() ?? "";
        if (!_globalExpressions.ContainsKey(key))
        {
            _status = L.T($"表情プリセット '{key}' はありません", $"There is no expression preset '{key}'");
            return false;
        }
        if (_globalExpressions.Count <= 1)
        {
            _status = L.T("最後の表情プリセットは削除できません", "The last expression preset cannot be deleted");
            return false;
        }
        try
        {
            var next = new Dictionary<string, ExpressionPreset>(_globalExpressions, StringComparer.OrdinalIgnoreCase);
            next.Remove(key);
            GlobalExpressionPresets.Save(GlobalExpressionPresets.PathOnDisk, next);
            _globalExpressions = next;
            _baseCharacter.Expressions = next;
            if (_character != null) _character.Expressions = next;
            _status = L.T($"表情プリセット '{key}' を削除しました", $"Deleted the expression preset '{key}'");
            return true;
        }
        catch (Exception ex)
        {
            _status = L.T($"表情プリセット削除失敗: {ex.Message}", $"Could not delete the expression preset: {ex.Message}");
            LogSource?.LogError(ex);
            return false;
        }
    }

    [HideFromIl2Cpp]
    private bool SaveGlobalExpressionPreset(string key, ExpressionPreset preset)
    {
        if (!ExpressionPresetRules.Validate(key, preset, 11, 25, 29, out var error))
        {
            _status = error;
            return false;
        }
        try
        {
            var next = new Dictionary<string, ExpressionPreset>(_globalExpressions, StringComparer.OrdinalIgnoreCase)
            { [key] = preset };
            GlobalExpressionPresets.Save(GlobalExpressionPresets.PathOnDisk, next);
            _globalExpressions = next;
            _baseCharacter.Expressions = next;
            if (_character != null) _character.Expressions = next;
            _status = L.T($"グローバル表情プリセット '{key}' を保存しました", $"Saved the shared expression preset '{key}'");
            return true;
        }
        catch (Exception ex)
        {
            _status = L.T($"表情プリセット保存失敗: {ex.Message}", $"Could not save the expression preset: {ex.Message}");
            LogSource?.LogError(ex);
            return false;
        }
    }

    private bool SaveCurrentExpressionPreset(string key, string description)
    {
        if (_stage?.IsReady != true || _requesting || _runner?.IsRunning == true) return false;
        var current = _adapter?.CaptureExpression();
        if (current == null) return false;
        current.Description = description.Trim();
        return SaveGlobalExpressionPreset(key.Trim(), current);
    }

    private bool ApplySavedExpressionPreset(string key)
    {
        if (_stage?.IsReady != true || _requesting || _runner?.IsRunning == true) return false;
        return _adapter?.SetExpression(key) == true;
    }

    private void AdjustFaceValue(string part, float delta)
    {
        if (_stage?.IsReady != true || _adapter == null || _requesting || _runner?.IsRunning == true) return;
        if (!_adapter.AdjustFaceValue(part, delta)) _status = L.T("開閉値を変更できませんでした", "Could not change the openness");
    }

    private string LoadPersonalityFile()
    {
        try
        {
            Directory.CreateDirectory(PersonalityFile.Directory);
            var path = NativeFilePicker.OpenPersonality(PersonalityFile.Directory);
            if (path == null) return null;
            var prompt = PersonalityFile.Load(path);
            _status = L.T($"性格ファイルを読み込みました: {Path.GetFileName(path)}", $"Loaded the personality file: {Path.GetFileName(path)}");
            return prompt;
        }
        catch (Exception ex)
        {
            _status = L.T($"性格ファイル読込失敗: {ex.Message}", $"Could not load the personality file: {ex.Message}");
            LogSource?.LogError(ex);
            return "";
        }
    }

    private bool? SavePersonalityFile(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4000)
        {
            _status = L.T("性格・口調の指示は1～4000文字で入力してください", "The personality text must be 1 to 4000 characters");
            return false;
        }
        try
        {
            Directory.CreateDirectory(PersonalityFile.Directory);
            var path = NativeFilePicker.SavePersonality(PersonalityFile.Directory);
            if (path == null) return null;
            PersonalityFile.Save(path, prompt.Trim());
            _status = L.T($"性格ファイルを保存しました: {Path.GetFileName(path)}", $"Saved the personality file: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            _status = L.T($"性格ファイル保存失敗: {ex.Message}", $"Could not save the personality file: {ex.Message}");
            LogSource?.LogError(ex);
            return false;
        }
    }

    private void BindCharacterProfile()
    {
        if (_stage?.IsReady != true || string.IsNullOrWhiteSpace(_stage.ActiveCardPath)) return;
        _character = CharacterConfig.LoadOrCreateForCard(Path.GetFullPath(CharacterConfigPath),
            _baseCharacter, _stage.ActiveCardPath, _stage.CharacterName ?? L.T("キャラクター", "Character"));
        if (ExpressionCatalog.IsLegacyDefault(_character.Expressions))
            _character.Expressions = ExpressionCatalog.Defaults();
        _character.Expressions = _globalExpressions;
        _character.MotionDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        MigrateMotions(_character);
        _adapter = new CharacterAdapter(_character, LogSource!, () => _stage?.Human,
            motionId => _stage?.PlayMotion(motionId) == true) { PoseSetter = ApplyPoseFromAi };
        _runner = new SequenceRunner(_adapter,
            text => { _stage.ShowText(text); Append($"{_stage.CharacterName ?? _character.Name}: {text}"); }, LogSource!);
        _profileCardPath = _stage.ActiveCardPath;
        _history.Clear();
        _greetingPending = true;
        if (!string.IsNullOrWhiteSpace(_profileCardPath) && CardPathConfig != null)
            CharacterCardPath = CardPathConfig.Value = _profileCardPath;
        _ui?.SetSelectedCardPath(_profileCardPath);
        _ui?.SetPersonalityPrompt(_character.SystemPrompt);
        _ui?.ClosePersonalityEditor();
        _stage.SetIdlePose(_character.IdlePose);
        LogSource?.LogInfo($"Character profile ready: id={_character.Id}, expressions={_character.Expressions.Count}, motions={_character.Motions.Count}");
    }

    private static void MigrateMotions(CharacterConfig config)
    {
        // The original MVP placeholders nod=1/wave=2 were never real gestures; drop them instead of reinterpreting.
        if (config.Motions.GetValueOrDefault("nod", -1) == 1 && config.Motions.GetValueOrDefault("wave", -1) == 2)
        {
            config.Motions.Remove("nod");
            config.Motions.Remove("wave");
        }
        foreach (var (key, id) in MotionCatalog.Retired)
            if (config.Motions.TryGetValue(key, out var mapped) && mapped == id) config.Motions.Remove(key);
        foreach (var (key, id) in MotionCatalog.Defaults()) config.Motions.TryAdd(key, id);
        MotionCatalog.FillMissingDescriptions(config.MotionDescriptions, config.Motions.Keys);
    }

    private void RunUiTest()
    {
        if (_runner == null) return;
        var test = new SequenceEnvelope
        {
            Commands = new()
            {
                new() { Type = "text", Value = L.T("UIテスト: 1行目を表示しました。", "UI test: showing line 1.") },
                new() { Type = "wait", Duration = 1.5f },
                new() { Type = "text", Value = L.T("UIテスト: Wait後の2行目を表示しました。", "UI test: showing line 2 after the wait.") }
            }
        };
        _runner.Start(SequenceValidator.Validate(test,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            warning => LogSource?.LogWarning(warning)));
        _status = "running UI/Wait test";
        LogSource?.LogInfo("Dedicated UI/Wait test started.");
    }

    private void RunDemo()
    {
        if (_character == null || _runner == null) return;
        var demo = new SequenceEnvelope
        {
            Commands = new()
            {
                // "smile" may have been deleted by the user; any remaining preset will do.
                new() { Type = "expression", Value = _character.Expressions.ContainsKey("smile") ? "smile" : _character.Expressions.Keys.First() },
                new() { Type = "motion", Value = "stretch" },
                new() { Type = "text", Value = L.T("こんにちは", "Hello.") },
                new() { Type = "wait", Duration = 3f },
                new() { Type = "motion", Value = "idle" }
            }
        };
        _runner.Start(SequenceValidator.Validate(demo,
            _character.Expressions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _character.Motions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            warning => LogSource?.LogWarning(warning)));
        _status = "running fixed demo";
    }

    private void Send()
    {
        SendText(_ui?.InputText?.Trim() ?? "");
    }

    private void SendText(string text)
    {
        if (_greetingInFlight) Cancel();
        if (_character == null || _runner == null || _requesting || _bridgeRestarting || _runner.IsRunning || _stage?.IsReady != true) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        _ui.ClearInput();
        Append(L.T($"あなた: {text}", $"You: {text}"));
        LogDialogue("user", text, "input");
        RequestReply(text, greeting: false);
    }

    private void RequestReply(string text, bool greeting)
    {
        if (_client == null) return;
        _greetingInFlight = greeting;
        _requesting = true;
        _status = "waiting for bridge";
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        var historySnapshot = _history.TakeLast(Math.Clamp(Settings.HistoryMessages.Value, 0, 200)).ToArray();
        var outfitActions = _adapter?.AvailableOutfitActions() ?? Array.Empty<string>();
        // The AI needs the present state of each worn part to know what a request actually changes.
        var currentOutfit = (_adapter?.CaptureOutfitStates() ?? new())
            .Where(pair => (int)pair.Key < OutfitIntent.PartKeys.Length)
            .ToDictionary(pair => OutfitIntent.PartKeys[(int)pair.Key], pair =>
            {
                var state = pair.Value switch
                {
                    Character.HumanCloth.Define.ClothesState.Clothing => "on",
                    Character.HumanCloth.Define.ClothesState.HalfUndress => "half",
                    _ => "off"
                };
                var name = _adapter.ClothesName(pair.Key);
                return string.IsNullOrWhiteSpace(name) ? state : $"{state}({name})";
            });
        var character = new CharacterConfig
        {
            Id = _stage.CharacterName ?? _character.Id,
            Name = _stage.CharacterName ?? _character.Name,
            SystemPrompt = _character.SystemPrompt,
            Expressions = _character.Expressions,
            Motions = _character.Motions,
            MotionDescriptions = _character.MotionDescriptions
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var envelope = await _client.CompleteAsync(character, historySnapshot, text, outfitActions, currentOutfit, token);
                _mainThread.Enqueue(() =>
                {
                    if (generation != _requestGeneration || token.IsCancellationRequested) return;
                    if (!string.IsNullOrWhiteSpace(envelope.Thinking))
                        LogSource?.LogInfo($"[Ollama thinking]\n{envelope.Thinking}");
                    var commands = SequenceValidator.Validate(envelope,
                        character.Expressions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                        character.Motions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                        warning => LogSource?.LogWarning(warning)).ToList();
                    if (commands.Count == 0) throw new InvalidDataException("Bridge returned no valid commands.");
                    // Commit the single structured clothing action before speaking.
                    // Otherwise the generated dialogue could claim a change that the
                    // current card rejects when SequenceRunner reaches the action.
                    var outfitCommand = commands.FirstOrDefault(command => command.Type == "outfit");
                    if (outfitCommand != null)
                    {
                        var applied = _adapter?.SetOutfit(outfitCommand.Value!) == true;
                        commands.RemoveAll(command => command.Type == "outfit");
                        // Unsupported parts are skipped silently and the character keeps its own wording;
                        // only a change that did nothing at all is corrected so the reply cannot claim it.
                        if (!applied)
                        {
                            foreach (var command in commands.Where(command => command.Type == "text"))
                                command.Value = L.T("今の衣装では、変えられる部分がなかったよ。", "There was nothing on me that could change like that.");
                            LogSource?.LogInfo($"LLM outfit action had no applicable parts: {outfitCommand.Value}");
                        }
                    }
                    // The greeting instruction is not something the player said, so it stays out of history.
                    if (!greeting) _history.Add(new HistoryItem("user", text));
                    var reply = string.Join(" ", commands.Where(c => c.Type == "text").Select(c => c.Value));
                    if (!string.IsNullOrWhiteSpace(reply)) _history.Add(new HistoryItem("assistant", reply));
                    if (!string.IsNullOrWhiteSpace(reply)) LogDialogue("assistant", reply, greeting ? "llm-greeting" : "llm");
                    var keep = Math.Max(60, Math.Clamp(Settings.HistoryMessages.Value, 0, 200));
                    if (_history.Count > keep) _history.RemoveRange(0, _history.Count - keep);
                    _runner.Start(commands);
                    _requesting = false;
                    _greetingInFlight = false;
                    _status = "playing response";
                });
            }
            catch (Exception ex)
            {
                _mainThread.Enqueue(() =>
                {
                    if (generation != _requestGeneration || token.IsCancellationRequested) return;
                    _requesting = false;
                    _greetingInFlight = false;
                    _status = "request failed";
                    Append($"[error] {ex.Message}");
                    _stage?.ShowText(L.T($"接続できませんでした。{ex.Message}", $"Could not connect. {ex.Message}"));
                    LogSource?.LogError(ex);
                });
            }
        });
    }

    private static void LogDialogue(string role, string text, string source)
    {
        // JSON escapes newlines/control characters so dialogue cannot forge BepInEx log entries.
        LogSource?.LogInfo("[Conversation] " + JsonSerializer.Serialize(new { role, source, text }, DialogueLogOptions));
    }

    private void Cancel()
    {
        _requestGeneration++;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
        _runner?.Cancel();
        _requesting = false;
        _greetingInFlight = false;
        _status = "cancelled";
    }

    private void ExecuteTestCommand(string action, string value)
    {
        switch (action)
        {
            case "enter_preset":
                if (_visible) throw new InvalidOperationException("Close the current stage first");
                _profileCardPath = null; _visible = true; _stage.Enter(null, true); _ui.SetVisible(true); _greetingPending = true;
                break;
            case "exit_stage": SetVisible(false); break;
            case "open_options": OpenNativeOptions(); break;
            case "close_options": AL.Config.ConfigWindow.Unload(); break;
            case "camera_preset": _stage.SetCameraPreset(value); break;
            case "camera_reset": _stage.ResetView(); break;
            case "background": _stage.SetBackground(value); break;
            case "graphics_bloom":
                Manager.Config.GraphicData.Bloom = bool.Parse(value);
                _stage.RefreshGraphics();
                break;
            case "graphics_dof":
                Manager.Config.GraphicData.DepthOfField = bool.Parse(value);
                _stage.RefreshGraphics();
                break;
            case "graphics_vignette":
                Manager.Config.GraphicData.Vignette = bool.Parse(value);
                _stage.RefreshGraphics();
                break;
            case "dump_volumes": VolumeDump.Write(Path.GetFullPath(value)); break;
            case "face_adjust":
                var faceArgs = value.Split(',');
                if (!_adapter.AdjustFaceValue(faceArgs[0], float.Parse(faceArgs[1], System.Globalization.CultureInfo.InvariantCulture)))
                    throw new InvalidOperationException("Face value unavailable");
                break;
            case "menu": _ui.SetMenuVisible(bool.Parse(value)); break;
            case "light":
                var lightArgs = value.Split(',');
                if (lightArgs[0] == "reset") ResetLight();
                else AdjustLight(lightArgs[0], float.Parse(lightArgs[1], System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "coordinate": SwitchCoordinate(); break;
            case "model_picker": _ui.OpenSettingsPanel(LoadBackendSettings); RequestModelList(); break;
            case "test_connection": _ui.OpenSettingsPanel(LoadBackendSettings); TestBackendConnection(); break;
            case "pose": CyclePose(int.Parse(value)); break;
            case "screenshot": ScreenCapture.CaptureScreenshot(Path.GetFullPath(value)); break;
            case "quit_test": SetVisible(false); Application.Quit(); break;
            case "send":
                if ((_requesting && !_greetingInFlight) || _runner.IsRunning || _stage?.IsReady != true) throw new InvalidOperationException("Not ready");
                SendText(value);
                break;
            case "motion":
                if (_requesting || _runner.IsRunning || !_adapter.PlayMotion(value)) throw new InvalidOperationException("Motion unavailable or busy");
                break;
            case "expression":
                if (_requesting || _runner.IsRunning || !_adapter.SetExpression(value)) throw new InvalidOperationException("Expression unavailable or busy");
                break;
            case "eyebrow":
            case "eyes":
            case "mouth":
                if (_requesting || _runner.IsRunning || !int.TryParse(value, out var faceIndex)
                    || !_adapter.SetFacePart(action, faceIndex))
                    throw new InvalidOperationException("Face part unavailable, out of range, or busy");
                break;
            case "outfit":
                if (_requesting || _runner.IsRunning || !_adapter.SetOutfit(value))
                    throw new InvalidOperationException("Outfit unavailable or busy");
                break;
            case "next_character":
                if (_requesting || _runner.IsRunning || _stage?.IsReady != true) throw new InvalidOperationException("Not ready");
                SwitchCharacter(1);
                break;
            case "sequence_no_idle":
                if (_requesting || _runner.IsRunning || _stage?.IsReady != true) throw new InvalidOperationException("Not ready");
                _runner.Start(new[] { new SequenceCommand { Type = "motion", Value = "stretch" },
                    new SequenceCommand { Type = "text", Value = L.T("演出終了後に待機へ戻ります。", "After the acting she returns to idle.") },
                  new SequenceCommand { Type = "wait", Duration = 1f } });
                break;
            case "sequence_face_parts":
                if (_requesting || _runner.IsRunning || _stage?.IsReady != true) throw new InvalidOperationException("Not ready");
                _runner.Start(SequenceValidator.Validate(new SequenceEnvelope { Commands = new()
                {
                    new() { Type = "expression", Value = "neutral" },
                    new() { Type = "eyebrow", Value = "4" },
                    new() { Type = "eyes", Value = "3" },
                    new() { Type = "mouth", Value = "19" },
                    new() { Type = "text", Value = L.T("眉・目・口を個別に調整しました。", "Adjusted the brows, eyes and mouth individually.") }
                } }, _character.Expressions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    _character.Motions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    warning => LogSource?.LogWarning(warning)));
                break;
            case "demo":
                if (_requesting || _runner.IsRunning || _stage?.IsReady != true) throw new InvalidOperationException("Not ready");
                RunDemo();
                break;
            case "cancel": Cancel(); break;
            default: throw new ArgumentException("Unsupported diagnostic action");
        }
    }

    private void Append(string line)
    {
        _transcript += line + "\n";
        if (_transcript.Length > 12000)
            _transcript = _transcript[^10000..];
    }

    private void OnDestroy()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _client?.Dispose();
        _adapter?.RestoreOutfit();
        _stage?.Dispose();
        _ui?.Dispose();
        _titleEntry?.Dispose();
    }
}
