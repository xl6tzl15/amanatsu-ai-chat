using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP.Configuration;
using UnityEngine;

namespace Amanatsu.AiChat.Config;

internal sealed class ModSettings
{
    public readonly ConfigEntry<KeyboardShortcut> OpenChat;
    public readonly ConfigEntry<KeyboardShortcut> ToggleMenu;
    public readonly ConfigEntry<float> LightVertical;
    public readonly ConfigEntry<float> LightHorizontal;
    public readonly ConfigEntry<float> LightIntensity;
    public readonly ConfigEntry<string> LightColor;
    public readonly ConfigEntry<KeyboardShortcut> ToggleEyes;
    public readonly ConfigEntry<KeyboardShortcut> ToggleEyesSecondary;
    public readonly ConfigEntry<KeyboardShortcut> ToggleNeck;
    public readonly ConfigEntry<KeyboardShortcut> ToggleNeckSecondary;
    public readonly ConfigEntry<KeyCode> UnlockViewPrimary;
    public readonly ConfigEntry<KeyCode> UnlockViewSecondary;
    public readonly ConfigEntry<KeyCode> ResetViewPrimary;
    public readonly ConfigEntry<KeyCode> ResetViewSecondary;
    public readonly ConfigEntry<int> OrbitMouseButton;
    public readonly ConfigEntry<int> PanMouseButton;
    public readonly ConfigEntry<int> AlternatePanMouseButton;
    public readonly ConfigEntry<float> OrbitSensitivity;
    public readonly ConfigEntry<float> PanSensitivity;
    public readonly ConfigEntry<string> CameraPreset;
    public readonly ConfigEntry<string> BackgroundImage;
    public readonly ConfigEntry<float> CameraSmoothing;
    public readonly ConfigEntry<float> FieldOfView;
    public readonly ConfigEntry<string> UiFontFile;
    public readonly ConfigEntry<int> InputFontSize;
    public readonly ConfigEntry<float> InputBackgroundOpacity;
    public readonly ConfigEntry<string> Provider;
    public readonly ConfigEntry<string> UpstreamUrl;
    public readonly ConfigEntry<string> Model;
    public readonly ConfigEntry<string> ApiKey;
    public readonly ConfigEntry<bool> LogThinking;
    public readonly ConfigEntry<int> MaxReplyTokens;
    public readonly ConfigEntry<int> ContextTokens;
    public readonly ConfigEntry<int> HistoryMessages;

    public ModSettings(ConfigFile config, BackendSettings backendDefaults)
    {
        OpenChat = config.Bind("Keybinds", "OpenChat", new KeyboardShortcut(KeyCode.F10), "Open or close AI chat on the title screen.");
        ToggleMenu = config.Bind("Keybinds", "ToggleMenu", new KeyboardShortcut(KeyCode.H), "Show or hide the chat menu panel; ignored while typing.");
        LightVertical = config.Bind("Light", "Vertical", 25f, "Character light elevation relative to the camera, -90 to 90 degrees.");
        LightHorizontal = config.Bind("Light", "Horizontal", -30f, "Character light direction relative to the camera, -180 to 180 degrees.");
        LightIntensity = config.Bind("Light", "Intensity", 1f, "Character light intensity, 0 to 3.");
        LightColor = config.Bind("Light", "Color", "#DFE6E6FF", "Character light color in HTML RGBA format.");
        ToggleEyes = config.Bind("Keybinds", "ToggleEyes", new KeyboardShortcut(KeyCode.Alpha1), "Switch eye tracking; ignored while typing.");
        ToggleEyesSecondary = config.Bind("Keybinds", "ToggleEyesSecondary", new KeyboardShortcut(KeyCode.Keypad1), "Optional second eye-tracking shortcut.");
        ToggleNeck = config.Bind("Keybinds", "ToggleNeck", new KeyboardShortcut(KeyCode.Alpha2), "Switch neck tracking; ignored while typing.");
        ToggleNeckSecondary = config.Bind("Keybinds", "ToggleNeckSecondary", new KeyboardShortcut(KeyCode.Keypad2), "Optional second neck-tracking shortcut.");
        UnlockViewPrimary = config.Bind("Keybinds", "UnlockViewPrimary", KeyCode.LeftShift, "Hold to operate the camera.");
        UnlockViewSecondary = config.Bind("Keybinds", "UnlockViewSecondary", KeyCode.RightShift, "Optional second camera-unlock key; set None to disable.");
        ResetViewPrimary = config.Bind("Keybinds", "ResetViewPrimary", KeyCode.LeftControl, "Reset the camera view; ignored while typing.");
        ResetViewSecondary = config.Bind("Keybinds", "ResetViewSecondary", KeyCode.RightControl, "Optional second reset key; set None to disable.");
        OrbitMouseButton = config.Bind("Keybinds", "OrbitMouseButton", 0, "Mouse button used to orbit while view is unlocked: 0=left, 1=right, 2=middle.");
        PanMouseButton = config.Bind("Keybinds", "PanMouseButton", 1, "Mouse button used to pan while view is unlocked: 0=left, 1=right, 2=middle.");
        AlternatePanMouseButton = config.Bind("Keybinds", "AlternatePanMouseButton", 2, "Optional second pan mouse button; set -1 to disable.");
        OrbitSensitivity = config.Bind("Camera", "OrbitSensitivity", 3f, "Camera orbit degrees per mouse-axis unit; 0.1 to 10.");
        PanSensitivity = config.Bind("Camera", "PanSensitivity", 0.012f, "Camera pan factor relative to character height; 0.001 to 0.05.");
        CameraPreset = config.Bind("Camera", "DefaultPreset", "portrait", "Initial/reset framing: face, portrait, or full.");
        BackgroundImage = config.Bind("Appearance", "BackgroundImage", "game:0", "Conversation backdrop: none, game:0..9, or an absolute PNG/JPEG path. Use the in-game background picker to change it.");
        CameraSmoothing = config.Bind("Camera", "SmoothingSeconds", 0.12f, "Camera interpolation time, 0 (instant) to 0.5 seconds.");
        FieldOfView = config.Bind("Camera", "FieldOfView", 32f, "Conversation camera vertical field of view, 20 to 70 degrees.");
        UiFontFile = config.Bind("Appearance", "JapaneseFontFile", @"C:\Windows\Fonts\NotoSansJP-VF.ttf", "Optional Japanese TTF/OTF path; falls back to game fonts if missing.");
        InputFontSize = config.Bind("Appearance", "InputFontSize", 21, "Input text size, clamped to 16..30.");
        InputBackgroundOpacity = config.Bind("Appearance", "InputBackgroundOpacity", 0.82f, "Dark input-field opacity, clamped to 0.4..1.0.");
        Provider = config.Bind("Bridge", "Provider", backendDefaults.Provider, "ollama or chat-completions.");
        UpstreamUrl = config.Bind("Bridge", "UpstreamUrl", backendDefaults.UpstreamUrl, "Ollama or OpenAI-compatible API URL.");
        Model = config.Bind("Bridge", "Model", backendDefaults.Model, "Model name sent to the upstream API.");
        ApiKey = config.Bind("Bridge", "ApiKey", backendDefaults.ApiKey, "Optional API key; stored as plain text in this local cfg file.");
        LogThinking = config.Bind("Bridge", "LogThinking", backendDefaults.LogThinking, "Request and log Ollama thinking content when available.");
        MaxReplyTokens = config.Bind("Bridge", "MaxReplyTokens", backendDefaults.MaxReplyTokens, "Upper limit of reply tokens; 0 uses the default (512).");
        ContextTokens = config.Bind("Bridge", "ContextTokens", backendDefaults.ContextTokens, "Context length in tokens; 0 uses the default (Ollama 4096, OpenAI-compatible 16384). Larger values keep more history but use more GPU memory with Ollama.");
        HistoryMessages = config.Bind("Bridge", "HistoryMessages", backendDefaults.HistoryMessages, "Most recent conversation messages sent to the model (one exchange is two messages), 0 to 200.");
    }

    public BackendSettings BackendValues(string endpoint, string token) => new()
    {
        BridgeEndpoint = endpoint,
        BridgeToken = token,
        Provider = Provider.Value,
        UpstreamUrl = UpstreamUrl.Value,
        Model = Model.Value,
        ApiKey = ApiKey.Value,
        LogThinking = LogThinking.Value,
        MaxReplyTokens = MaxReplyTokens.Value,
        ContextTokens = ContextTokens.Value,
        HistoryMessages = HistoryMessages.Value
    };

    public static bool Held(KeyCode key) => key != KeyCode.None && Input.GetKey(key);
    public static bool Pressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);
    public static bool MouseHeld(int button) => button is >= 0 and <= 2 && Input.GetMouseButton(button);
}
