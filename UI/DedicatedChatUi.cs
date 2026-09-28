using TMPro;
using System.Globalization;
using Amanatsu.AiChat.Config;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Amanatsu.AiChat.UI;

internal sealed class DedicatedChatUi : IDisposable
{
    private static readonly Color InputTextColor = new(0.98f, 0.97f, 0.93f, 1f);
    private static readonly Color InputHintColor = new(0.78f, 0.83f, 0.89f, 0.95f);
    private static TMP_FontAsset _dynamicJapaneseFont;
    private readonly ModSettings _settings;
    private readonly GameObject _root;
    private readonly TMP_InputField _input;
    private readonly TMP_Text _selectedCard;
    private readonly GameObject _personalityOverlay;
    private readonly TMP_InputField _personalityInput;
    private readonly TMP_Text _personalityFeedback;
    private readonly GameObject _settingsOverlay;
    private readonly TMP_InputField _bridgeEndpointInput;
    private readonly TMP_InputField _bridgeTokenInput;
    private readonly TMP_InputField _upstreamUrlInput;
    private readonly TMP_InputField _modelInput;
    private GameObject _modelOverlay;
    private TMP_Text _modelPickerMessage;
    private readonly List<GameObject> _modelItems = new();
    private IReadOnlyList<string> _modelNames = Array.Empty<string>();
    private string _modelMessage;
    private int _modelPage;
    private const int ModelsPerPage = 24;
    public string SettingsProvider => _provider;
    public string SettingsUpstreamUrl => _upstreamUrlInput.text.Trim();
    // The key being typed may not be saved yet; listing models must use it as entered.
    public string SettingsApiKey => _apiKeyInput.text;

    public void AddModelPicker(Action requestModels)
    {
        var panel = _settingsOverlay.transform.Find("BackendSettings").GetComponent<RectTransform>();
        _modelInput.GetComponent<RectTransform>().sizeDelta = new Vector2(560, 40);
        MakeButton(panel, L.T("一覧から選ぶ…", "Choose…"), new Vector2(590, -412), new Vector2(148, 40), requestModels);
        var overlay = MakeRect("ModelPicker", _settingsOverlay.transform, new Vector2(420, -105), new Vector2(760, 690));
        overlay.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 1f);
        _modelOverlay = overlay.gameObject;
        MakeText("ModelPickerTitle", overlay, L.T("導入済みのモデルから選ぶ", "Choose an installed model"), 22, new Vector2(22, -18), new Vector2(600, 32));
        MakeButton(overlay, L.T("閉じる", "Close"), new Vector2(610, -16), new Vector2(128, 36), () => _modelOverlay.SetActive(false));
        _modelPickerMessage = MakeText("ModelPickerMessage", overlay, "", 15, new Vector2(22, -60), new Vector2(716, 26));
        MakeButton(overlay, L.T("前のページ", "Prev page"), new Vector2(22, -636), new Vector2(160, 38), () => ShowModelPage(_modelPage - 1));
        MakeButton(overlay, L.T("次のページ", "Next page"), new Vector2(578, -636), new Vector2(160, 38), () => ShowModelPage(_modelPage + 1));
        _modelOverlay.SetActive(false);
    }

    public void ShowModelChoices(IReadOnlyList<string> names, string message)
    {
        if (_modelOverlay == null) return;
        _modelNames = names ?? Array.Empty<string>();
        _modelMessage = message;
        ShowModelPage(0);
    }

    private void ShowModelPage(int page)
    {
        foreach (var item in _modelItems) UnityEngine.Object.Destroy(item);
        _modelItems.Clear();
        var names = _modelNames;
        var pages = Math.Max(1, (names.Count + ModelsPerPage - 1) / ModelsPerPage);
        _modelPage = Math.Clamp(page, 0, pages - 1);
        var overlay = _modelOverlay.GetComponent<RectTransform>();
        var first = _modelPage * ModelsPerPage;
        for (var i = 0; i < ModelsPerPage && first + i < names.Count; i++)
        {
            var name = names[first + i];
            var button = MakeButton(overlay, name, new Vector2(22 + (i % 2) * 362, -96 - (i / 2) * 44), new Vector2(354, 38), () =>
            {
                _modelInput.SetTextWithoutNotify(name);
                _modelOverlay.SetActive(false);
                _settingsFeedback.text = L.T($"モデル「{name}」を選びました。『保存して適用』で反映します。", $"Selected \"{name}\". Press \"Save & apply\" to use it.");
            });
            var label = button.GetComponentInChildren<TMP_Text>();
            label.fontSize = 14;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            _modelItems.Add(button.gameObject);
        }
        _modelOverlay.transform.SetAsLastSibling();
        _modelPickerMessage.text = _modelMessage ?? (pages > 1
            ? L.T($"使うモデルを押してください（{_modelPage + 1}/{pages}ページ、全{names.Count}件）。", $"Click the model to use (page {_modelPage + 1}/{pages}, {names.Count} models).")
            : L.T("使うモデルを押してください。", "Click the model to use."));
        _modelOverlay.SetActive(true);
    }
    private readonly TMP_InputField _apiKeyInput;
    private GameObject _advancedOverlay;
    private TMP_InputField _maxReplyInput;
    private TMP_InputField _contextInput;
    private TMP_InputField _historyInput;
    private readonly TMP_Text _providerLabel;
    private readonly TMP_Text _thinkingLogLabel;
    private readonly TMP_Text _settingsFeedback;
    private string _provider = "ollama";
    private bool _logThinking;
    private string _savedPersonalityPrompt = "";
    private readonly TMP_Text _status;
    private readonly TMP_Text _help;
    private readonly TMP_Text _character;
    private readonly Button _send;
    private readonly Button _uiTest;
    private readonly Button _performanceTest;
    private readonly TMP_Text _eyebrowValue;
    private readonly TMP_Text _eyesValue;
    private readonly TMP_Text _mouthValue;
    private TMP_Text _blushValue;
    private TMP_Text _eyesOpenValue;
    private TMP_Text _mouthOpenValue;
    private TMP_InputField _inlineExpressionKey;
    private TMP_InputField _inlineExpressionDescription;
    private TMP_Text _inlineExpressionFeedback;
    private TMP_Text _inlineExpressionSelected;
    private string[] _inlineExpressionKeys = Array.Empty<string>();
    private int _inlineExpressionIndex;
    private Func<IReadOnlyDictionary<string, ExpressionPreset>> _inlineLoadExpressions;
    private Func<string, bool> _inlineApplyExpression;
    private readonly List<Button> _inlineFaceButtons = new();
    private string _deleteArmedKey;
    private float _deleteArmedUntil;
    private readonly Button _eyeMode;
    private readonly TMP_Text _eyeModeLabel;
    private readonly Button _neckMode;
    private readonly TMP_Text _neckModeLabel;
    private readonly Button[] _faceButtons;
    private readonly TMP_FontAsset _font;
    private readonly Button _menuButton;
    private readonly Button _coordinateButton;
    private readonly Button _poseButton;
    private readonly TMP_Text _poseLabel;

    public void SetPose(int pose, bool interactable)
    {
        _poseLabel.text = pose >= 0 ? L.T($"ポーズ {pose + 1:00}", $"Pose {pose + 1:00}") : L.T("ポーズ -", "Pose -");
        _poseButton.interactable = interactable;
    }
    private readonly TMP_Text _coordinateLabel;

    public void SetCoordinate(int coordinate, bool interactable)
    {
        _coordinateLabel.text = coordinate switch { 0 => L.T("衣装: 水着", "Outfit: swimsuit"), 1 => L.T("衣装: 風呂上がり", "Outfit: after bath"), _ => L.T("衣装: -", "Outfit: -") };
        _coordinateButton.interactable = interactable;
    }
    private GameObject _lightPanel;
    private TMP_Text _lightVerticalValue;
    private TMP_Text _lightHorizontalValue;
    private TMP_Text _lightIntensityValue;
    private Image _lightSwatch;
    private Func<(float Vertical, float Horizontal, float Intensity, Color Color)> _readLight;
    public TMP_FontAsset Font => _font;
    public bool ModalOpen => _settingsOverlay.activeSelf || _personalityOverlay.activeSelf;
    public bool MenuVisible => _root.transform.Find("Panel").gameObject.activeSelf;

    public void SetMenuVisible(bool visible)
    {
        _root.transform.Find("Panel").gameObject.SetActive(visible);
        _menuButton.gameObject.SetActive(!visible);
    }
    public void Suspend(bool suspended) { _root.SetActive(!suspended); }

    public DedicatedChatUi(Action send, Action uiTest, Action performanceTest, Action cancel, Action close, Action previous, Action next, Action chooseCard, Func<string, bool> applyPersonality, Func<string> loadPersonality, Func<string, bool?> savePersonalityFile, Func<BackendSettings> loadBackendSettings, Action<BackendSettings> saveBackendSettings, Action testBackendConnection, Action restartBridge, Action left, Action right, Action near, Action far, Action<string, int> adjustFace, Action toggleEyeMode, Action toggleNeckMode, Action switchCoordinate, Action<int> cyclePose, ModSettings settings)
    {
        _settings = settings;
        _font = FindFont(settings.UiFontFile.Value);
        _root = new GameObject("AmanatsuAiChatCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        var panel = MakeRect("Panel", _root.transform, new Vector2(24, -24), new Vector2(480, 330));
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

        MakeText("Title", panel, ModIdentity.Label, 16, new Vector2(14, -8), new Vector2(216, 30));
        MakeButton(panel, L.T("接続設定", "Connection"), new Vector2(348, -8), new Vector2(84, 28), () => OpenSettings(loadBackendSettings));
        MakeButton(panel, "－", new Vector2(437, -8), new Vector2(28, 28), () => SetMenuVisible(false)).name = "CloseMenu";
        _menuButton = MakeButton(_root.GetComponent<RectTransform>(), L.T("メニュー", "Menu"), new Vector2(24, -24), new Vector2(110, 32), () => SetMenuVisible(true));
        _menuButton.gameObject.SetActive(false);
        _status = MakeText("Status", panel, L.T("準備中", "Preparing"), 14, new Vector2(14, -42), new Vector2(450, 22));
        _character = MakeText("Character", panel, L.T("キャラ読み込み中", "Loading character"), 17, new Vector2(14, -66), new Vector2(300, 26));
        MakeButton(panel, "◀", new Vector2(318, -64), new Vector2(30, 26), () => cyclePose(-1));
        _poseButton = MakeButton(panel, L.T("ポーズ", "Pose"), new Vector2(351, -64), new Vector2(82, 26), () => cyclePose(1));
        _poseLabel = _poseButton.GetComponentInChildren<TMP_Text>();
        _poseLabel.fontSize = 14;
        MakeButton(panel, "▶", new Vector2(436, -64), new Vector2(30, 26), () => cyclePose(1));
        _input = MakeInput(_root.GetComponent<RectTransform>(), new Vector2(310, -850), new Vector2(840, 46),
            L.T("ここにメッセージを入力", "Type a message here"), Mathf.Clamp(settings.InputFontSize.Value, 16, 30));
        _input.onSubmit.AddListener((UnityAction<string>)(_ => send()));

        _send = MakeButton(_root.GetComponent<RectTransform>(), L.T("送信", "Send"), new Vector2(1160, -850), new Vector2(120, 42), send);
        MakeButton(panel, L.T("前のキャラ", "Prev char"), new Vector2(14, -100), new Vector2(105, 32), previous);
        MakeButton(panel, L.T("次のキャラ", "Next char"), new Vector2(126, -100), new Vector2(105, 32), next);
        _uiTest = MakeButton(panel, L.T("セリフ確認", "Test line"), new Vector2(238, -100), new Vector2(105, 32), uiTest);
        _performanceTest = MakeButton(panel, L.T("演出確認", "Test acting"), new Vector2(350, -100), new Vector2(115, 32), performanceTest);
        MakeButton(panel, L.T("左回転", "Turn L"), new Vector2(14, -138), new Vector2(78, 28), left);
        MakeButton(panel, L.T("右回転", "Turn R"), new Vector2(98, -138), new Vector2(78, 28), right);
        MakeButton(panel, L.T("寄る", "Closer"), new Vector2(182, -138), new Vector2(65, 28), near);
        MakeButton(panel, L.T("引く", "Farther"), new Vector2(253, -138), new Vector2(65, 28), far);
        MakeButton(panel, L.T("中止", "Stop"), new Vector2(324, -138), new Vector2(65, 28), cancel);
        MakeButton(panel, L.T("終了", "Exit"), new Vector2(395, -138), new Vector2(70, 28), close);
        _help = MakeText("Help", panel, "", 13, new Vector2(14, -170), new Vector2(450, 34));
        MakeText("FaceHelp", panel, L.T("表情の個別調整", "Face parts"), 14, new Vector2(14, -207), new Vector2(145, 22));
        _eyeMode = MakeButton(panel, L.T("視線: 追従 (1)", "Eyes: follow (1)"), new Vector2(163, -203), new Vector2(145, 26), toggleEyeMode);
        _eyeModeLabel = _eyeMode.GetComponentInChildren<TMP_Text>();
        _eyeModeLabel.fontSize = 14;
        _neckMode = MakeButton(panel, L.T("首: アニメ (2)", "Neck: anim (2)"), new Vector2(314, -203), new Vector2(151, 26), toggleNeckMode);
        _neckModeLabel = _neckMode.GetComponentInChildren<TMP_Text>();
        _neckModeLabel.fontSize = 14;
        _eyebrowValue = MakeText("EyebrowValue", panel, L.T("眉 -", "Brow -"), 14, new Vector2(14, -234), new Vector2(62, 24));
        _eyesValue = MakeText("EyesValue", panel, L.T("目 -", "Eyes -"), 14, new Vector2(164, -234), new Vector2(62, 24));
        _mouthValue = MakeText("MouthValue", panel, L.T("口 -", "Mouth -"), 14, new Vector2(314, -234), new Vector2(62, 24));
        _faceButtons = new[]
        {
            MakeButton(panel, "-", new Vector2(77, -230), new Vector2(30, 26), () => adjustFace("eyebrow", -1)),
            MakeButton(panel, "+", new Vector2(111, -230), new Vector2(30, 26), () => adjustFace("eyebrow", 1)),
            MakeButton(panel, "-", new Vector2(227, -230), new Vector2(30, 26), () => adjustFace("eyes", -1)),
            MakeButton(panel, "+", new Vector2(261, -230), new Vector2(30, 26), () => adjustFace("eyes", 1)),
            MakeButton(panel, "-", new Vector2(377, -230), new Vector2(30, 26), () => adjustFace("mouth", -1)),
            MakeButton(panel, "+", new Vector2(411, -230), new Vector2(30, 26), () => adjustFace("mouth", 1)),
        };

        _selectedCard = MakeText("SelectedCard", panel, L.T("カード: 未選択", "Card: none"), 14, new Vector2(14, -265), new Vector2(450, 22));
        MakeButton(panel, L.T("ファイルから選択…", "Open card…"), new Vector2(14, -290), new Vector2(150, 30), chooseCard);
        MakeButton(panel, L.T("性格・口調を編集", "Personality…"), new Vector2(170, -290), new Vector2(150, 30), OpenPersonalityEditor);
        _coordinateButton = MakeButton(panel, L.T("衣装: -", "Outfit: -"), new Vector2(326, -290), new Vector2(140, 30), switchCoordinate);
        _coordinateLabel = _coordinateButton.GetComponentInChildren<TMP_Text>();

        var overlay = MakeRect("PersonalityEditorOverlay", _root.transform, Vector2.zero, new Vector2(1600, 900));
        _personalityOverlay = overlay.gameObject;
        overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        var editor = MakeRect("PersonalityEditor", overlay, new Vector2(500, -155), new Vector2(600, 560));
        editor.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 0.98f);
        MakeText("PersonalityTitle", editor, L.T("性格・口調の指示", "Personality and speaking style"), 22, new Vector2(20, -14), new Vector2(560, 32));
        MakeText("PersonalityHelp", editor, L.T("キャラの振る舞いだけを記述します。出力形式・動作ルールは編集対象外です。", "Describe only how the character behaves. Output format and action rules are not editable here."), 14,
            new Vector2(20, -50), new Vector2(560, 24));
        _personalityInput = MakeInput(editor, new Vector2(20, -82), new Vector2(560, 350), L.T("性格、口調、相手への接し方など", "Personality, speaking style, how she treats you…"), 16);
        _personalityInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        _personalityInput.characterLimit = 4000;
        _personalityInput.textComponent.alignment = TextAlignmentOptions.TopLeft;
        MakeButton(editor, L.T("カードへ適用", "Apply to card"), new Vector2(20, -447), new Vector2(125, 34), () =>
        {
            if (applyPersonality(_personalityInput.text))
            {
                _savedPersonalityPrompt = _personalityInput.text.Trim();
                _personalityFeedback.text = L.T("カードに適用しました", "Applied to the card.");
            }
            else _personalityFeedback.text = L.T("適用できませんでした。入力とログを確認してください", "Could not apply. Check the text and the log.");
        });
        MakeButton(editor, L.T("ファイルを開く…", "Open file…"), new Vector2(151, -447), new Vector2(140, 34), () =>
        {
            var prompt = loadPersonality();
            if (prompt != null)
            {
                if (prompt.Length == 0) _personalityFeedback.text = L.T("読み込めませんでした。ログを確認してください", "Could not load. Check the log.");
                else
                {
                    _personalityInput.SetTextWithoutNotify(prompt);
                    _personalityFeedback.text = L.T("読み込みました。『カードへ適用』で反映します", "Loaded. Press \"Apply to card\" to use it.");
                }
            }
        });
        MakeButton(editor, L.T("別名で保存…", "Save as…"), new Vector2(297, -447), new Vector2(140, 34), () =>
        {
            var saved = savePersonalityFile(_personalityInput.text);
            if (saved.HasValue)
                _personalityFeedback.text = saved.Value ? L.T("性格ファイルに保存しました", "Saved the personality file.") : L.T("保存できませんでした。入力とログを確認してください", "Could not save. Check the text and the log.");
        });
        MakeButton(editor, L.T("閉じる", "Close"), new Vector2(443, -447), new Vector2(137, 34), () => _personalityOverlay.SetActive(false));
        _personalityFeedback = MakeText("PersonalityFeedback", editor, "", 14, new Vector2(20, -493), new Vector2(560, 42));
        _personalityOverlay.SetActive(false);

        var settingsOverlay = MakeRect("BackendSettingsOverlay", _root.transform, Vector2.zero, new Vector2(1600, 900));
        _settingsOverlay = settingsOverlay.gameObject;
        _settingsOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var settingsPanel = MakeRect("BackendSettings", settingsOverlay, new Vector2(420, -105), new Vector2(760, 690));
        settingsPanel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 0.98f);
        MakeText("SettingsTitle", settingsPanel, L.T("LLM接続設定", "LLM connection"), 22, new Vector2(22, -18), new Vector2(560, 32));
        MakeButton(settingsPanel, L.T("詳細設定…", "Advanced…"), new Vector2(590, -16), new Vector2(148, 36), () =>
        {
            _advancedOverlay.transform.SetAsLastSibling();
            _advancedOverlay.SetActive(true);
        });
        MakeText("BridgeEndpointLabel", settingsPanel, L.T("ブリッジURL（ゲームからの接続先）", "Bridge URL (where the game connects)"), 16, new Vector2(22, -65), new Vector2(700, 25));
        _bridgeEndpointInput = MakeInput(settingsPanel, new Vector2(22, -92), new Vector2(716, 40), "http://127.0.0.1:38429/v1/chat", 17);
        MakeText("BridgeTokenLabel", settingsPanel, L.T("ブリッジ用Bearerトークン（必要な場合のみ）", "Bridge bearer token (only if needed)"), 16, new Vector2(22, -145), new Vector2(700, 25));
        _bridgeTokenInput = MakeInput(settingsPanel, new Vector2(22, -172), new Vector2(716, 40), L.T("任意", "Optional"), 17);
        _bridgeTokenInput.contentType = TMP_InputField.ContentType.Password;
        MakeText("ProviderLabel", settingsPanel, L.T("接続方式", "Provider"), 16, new Vector2(22, -225), new Vector2(200, 25));
        var providerButton = MakeButton(settingsPanel, "Ollama", new Vector2(22, -252), new Vector2(240, 40), () =>
        {
            _provider = _provider == "ollama" ? "chat-completions" : "ollama";
            _providerLabel.text = _provider == "ollama" ? "Ollama" : L.T("OpenAI互換API", "OpenAI-compatible API");
        });
        _providerLabel = providerButton.GetComponentInChildren<TMP_Text>();
        var thinkingButton = MakeButton(settingsPanel, L.T("Ollama思考: OFF", "Ollama thinking: OFF"), new Vector2(278, -252), new Vector2(260, 40), () =>
        {
            _logThinking = !_logThinking;
            _thinkingLogLabel.text = _logThinking ? L.T("Ollama思考: ON (ログ記録)", "Ollama thinking: ON (logged)") : L.T("Ollama思考: OFF", "Ollama thinking: OFF");
        });
        _thinkingLogLabel = thinkingButton.GetComponentInChildren<TMP_Text>();
        _thinkingLogLabel.fontSize = 16;
        MakeText("UpstreamUrlLabel", settingsPanel, "LLM API URL", 16, new Vector2(22, -305), new Vector2(700, 25));
        _upstreamUrlInput = MakeInput(settingsPanel, new Vector2(22, -332), new Vector2(716, 40), "http://127.0.0.1:11434/api/chat", 17);
        MakeText("ModelLabel", settingsPanel, L.T("モデル名", "Model"), 16, new Vector2(22, -385), new Vector2(700, 25));
        _modelInput = MakeInput(settingsPanel, new Vector2(22, -412), new Vector2(716, 40), L.T("モデル名", "Model"), 17);
        MakeText("ApiKeyLabel", settingsPanel, L.T("APIキー（Ollamaでは通常不要）", "API key (usually not needed for Ollama)"), 16, new Vector2(22, -465), new Vector2(700, 25));
        _apiKeyInput = MakeInput(settingsPanel, new Vector2(22, -492), new Vector2(716, 40), L.T("任意", "Optional"), 17);
        _apiKeyInput.contentType = TMP_InputField.ContentType.Password;
        MakeText("SettingsNote", settingsPanel, L.T("設定はBepInExのcfgに保存し、ブリッジ用JSONに同期します。APIキー・トークンは平文です。", "Settings are saved in the BepInEx cfg and mirrored to the bridge JSON. API keys and tokens are stored as plain text."), 13,
            new Vector2(22, -540), new Vector2(716, 38));
        MakeButton(settingsPanel, L.T("保存して適用", "Save & apply"), new Vector2(22, -596), new Vector2(180, 40), () =>
        {
            try
            {
                saveBackendSettings(new BackendSettings
                {
                    BridgeEndpoint = _bridgeEndpointInput.text.Trim(), BridgeToken = _bridgeTokenInput.text,
                    Provider = _provider, UpstreamUrl = _upstreamUrlInput.text.Trim(),
                    Model = _modelInput.text.Trim(), ApiKey = _apiKeyInput.text, LogThinking = _logThinking,
                    MaxReplyTokens = ReadNumber(_maxReplyInput, 0), ContextTokens = ReadNumber(_contextInput, 0),
                    HistoryMessages = ReadNumber(_historyInput, 30)
                });
                _settingsFeedback.text = L.T("保存しました。旧ブリッジには『ブリッジ再起動』で反映してください。", "Saved. Use \"Restart bridge\" to apply it to a running bridge.");
            }
            catch (Exception ex) { _settingsFeedback.text = ex.Message; }
        });
        MakeButton(settingsPanel, L.T("接続確認", "Test connection"), new Vector2(210, -596), new Vector2(160, 40), testBackendConnection);
        MakeButton(settingsPanel, L.T("ブリッジ再起動", "Restart bridge"), new Vector2(378, -596), new Vector2(222, 40), restartBridge);
        MakeButton(settingsPanel, L.T("閉じる", "Close"), new Vector2(610, -596), new Vector2(128, 40), () => _settingsOverlay.SetActive(false));
        _settingsFeedback = MakeText("SettingsFeedback", settingsPanel, "", 14, new Vector2(22, -643), new Vector2(716, 28));

        var advanced = MakeRect("AdvancedSettings", settingsOverlay, new Vector2(520, -215), new Vector2(560, 420));
        advanced.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.17f, 1f);
        _advancedOverlay = advanced.gameObject;
        MakeText("AdvancedTitle", advanced, L.T("詳細設定", "Advanced"), 22, new Vector2(22, -18), new Vector2(400, 32));
        MakeText("AdvancedNote", advanced, L.T("通常は変更不要です。空欄は初期値を使います。変更後は『保存して適用』と『ブリッジ再起動』を押してください。",
            "You normally don't need to change these. Leave a field empty for the default. After changing, press \"Save & apply\" and \"Restart bridge\"."),
            14, new Vector2(22, -58), new Vector2(516, 44));
        MakeText("MaxReplyLabel", advanced, L.T("返事の上限（トークン、初期値 512）", "Max reply tokens (default 512)"), 16, new Vector2(22, -112), new Vector2(516, 25));
        _maxReplyInput = MakeInput(advanced, new Vector2(22, -138), new Vector2(516, 40), L.T("空欄で初期値", "Empty for default"), 17);
        MakeText("ContextLabel", advanced, L.T("文脈の長さ（トークン、初期値 Ollama 4096 / API 16384）", "Context length (tokens; default Ollama 4096 / API 16384)"), 16, new Vector2(22, -190), new Vector2(516, 25));
        _contextInput = MakeInput(advanced, new Vector2(22, -216), new Vector2(516, 40), L.T("空欄で初期値", "Empty for default"), 17);
        MakeText("HistoryLabel", advanced, L.T("モデルに渡す履歴の件数（1往復で2件、初期値 30）", "History messages sent to the model (2 per exchange, default 30)"), 16, new Vector2(22, -268), new Vector2(516, 25));
        _historyInput = MakeInput(advanced, new Vector2(22, -294), new Vector2(516, 40), "30", 17);
        foreach (var field in new[] { _maxReplyInput, _contextInput, _historyInput })
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
        MakeButton(advanced, L.T("閉じる", "Close"), new Vector2(410, -360), new Vector2(128, 40), () => _advancedOverlay.SetActive(false));
        _advancedOverlay.SetActive(false);
        _settingsOverlay.SetActive(false);

    }

    public string InputText => _input?.text ?? "";
    public bool InputFocused => _input?.isFocused == true || _personalityInput?.isFocused == true
        || _bridgeEndpointInput?.isFocused == true || _bridgeTokenInput?.isFocused == true
        || _upstreamUrlInput?.isFocused == true || _modelInput?.isFocused == true || _apiKeyInput?.isFocused == true
        || _maxReplyInput?.isFocused == true || _contextInput?.isFocused == true || _historyInput?.isFocused == true
        || _inlineExpressionKey?.isFocused == true || _inlineExpressionDescription?.isFocused == true;
    public GameObject Root => _root;

    public void ClearInput()
    {
        _input.SetTextWithoutNotify("");
        _input.ActivateInputField();
    }

    public void SetSelectedCardPath(string path)
    {
        _selectedCard.text = string.IsNullOrWhiteSpace(path) ? L.T("カード: 未選択", "Card: none") : L.T($"カード: {Path.GetFileName(path)}", $"Card: {Path.GetFileName(path)}");
    }

    public void SetPersonalityPrompt(string prompt)
    {
        _savedPersonalityPrompt = prompt ?? "";
        _personalityInput.SetTextWithoutNotify(_savedPersonalityPrompt);
    }

    public void ClosePersonalityEditor() => _personalityOverlay.SetActive(false);

    public void SetSettingsFeedback(string message) => _settingsFeedback.text = message;

    public void AddEnvironmentControls(Action openOptions, Action<string> preset, Action cycleShadow, Func<string> shadowLabel,
        Action<int> cycleBackground, Action chooseBackground, Func<string> backgroundLabel)
    {
        var panel = _root.transform.Find("Panel").GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(480, 415);
        MakeButton(panel, L.T("環境設定", "Options"), new Vector2(232, -8), new Vector2(110, 28), openOptions);
        MakeButton(panel, L.T("顔", "Face"), new Vector2(14, -332), new Vector2(66, 28), () => preset("face"));
        MakeButton(panel, L.T("胸元", "Bust"), new Vector2(86, -332), new Vector2(66, 28), () => preset("portrait"));
        MakeButton(panel, L.T("全身", "Full"), new Vector2(158, -332), new Vector2(66, 28), () => preset("full"));
        Button shadow = null;
        shadow = MakeButton(panel, shadowLabel(), new Vector2(232, -332), new Vector2(113, 28), () =>
        { cycleShadow(); shadow.GetComponentInChildren<TMP_Text>().text = shadowLabel(); });
        MakeButton(panel, L.T("ライト", "Light"), new Vector2(351, -332), new Vector2(114, 28), ToggleLightPanel);
        Button background = null;
        background = MakeButton(panel, backgroundLabel(), new Vector2(14, -373), new Vector2(170, 30), () =>
        { cycleBackground(1); background.GetComponentInChildren<TMP_Text>().text = backgroundLabel(); });
        background.GetComponentInChildren<TMP_Text>().fontSize = 14;
        MakeButton(panel, L.T("前", "Prev"), new Vector2(190, -373), new Vector2(48, 30), () =>
        { cycleBackground(-1); background.GetComponentInChildren<TMP_Text>().text = backgroundLabel(); });
        MakeButton(panel, L.T("次", "Next"), new Vector2(244, -373), new Vector2(48, 30), () =>
        { cycleBackground(1); background.GetComponentInChildren<TMP_Text>().text = backgroundLabel(); });
        MakeButton(panel, L.T("画像を選択…", "Choose image…"), new Vector2(298, -373), new Vector2(167, 30), () =>
        { chooseBackground(); background.GetComponentInChildren<TMP_Text>().text = backgroundLabel(); });
    }

    // Same controls as the game's H-scene light setting: vertical, horizontal, intensity, color.
    public void AddLightControls(Func<(float Vertical, float Horizontal, float Intensity, Color Color)> read,
        Action<string, float> adjust, Action chooseColor, Action reset)
    {
        _readLight = read;
        var panel = MakeRect("LightPanel", _root.transform, new Vector2(1296, -24), new Vector2(280, 238));
        panel.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.94f);
        _lightPanel = panel.gameObject;
        MakeText("LightTitle", panel, L.T("ライト（キャラ用）", "Character light"), 16, new Vector2(14, -10), new Vector2(200, 26));
        MakeButton(panel, "×", new Vector2(238, -8), new Vector2(28, 28), ToggleLightPanel);
        TMP_Text Row(string label, string part, float y, float step)
        {
            MakeText(part + "Label", panel, label, 14, new Vector2(14, y), new Vector2(70, 26));
            var value = MakeText(part + "Value", panel, "-", 14, new Vector2(84, y), new Vector2(64, 26));
            MakeButton(panel, "-", new Vector2(150, y - 2), new Vector2(54, 27), () => { adjust(part, -step); UpdateLightPanel(); });
            MakeButton(panel, "+", new Vector2(210, y - 2), new Vector2(54, 27), () => { adjust(part, step); UpdateLightPanel(); });
            return value;
        }
        _lightVerticalValue = Row(L.T("縦の向き", "Vertical"), "vertical", -46, 5f);
        _lightHorizontalValue = Row(L.T("横の向き", "Horizontal"), "horizontal", -80, 5f);
        _lightIntensityValue = Row(L.T("強さ", "Intensity"), "intensity", -114, .1f);
        MakeText("LightColorLabel", panel, L.T("色", "Color"), 14, new Vector2(14, -150), new Vector2(40, 26));
        var swatch = MakeRect("LightSwatch", panel, new Vector2(84, -150), new Vector2(60, 24));
        _lightSwatch = swatch.gameObject.AddComponent<Image>();
        MakeButton(panel, L.T("色を選択…", "Pick color…"), new Vector2(150, -150), new Vector2(114, 27), () => { chooseColor(); UpdateLightPanel(); });
        MakeButton(panel, L.T("初期値に戻す", "Reset to default"), new Vector2(14, -192), new Vector2(250, 30), () => { reset(); UpdateLightPanel(); });
        _lightPanel.SetActive(false);
    }

    private void ToggleLightPanel()
    {
        if (_lightPanel == null) return;
        _lightPanel.SetActive(!_lightPanel.activeSelf);
        UpdateLightPanel();
    }

    private void UpdateLightPanel()
    {
        if (_readLight == null || _lightPanel == null || !_lightPanel.activeSelf) return;
        var light = _readLight();
        _lightVerticalValue.text = light.Vertical.ToString("0", CultureInfo.InvariantCulture) + "°";
        _lightHorizontalValue.text = light.Horizontal.ToString("0", CultureInfo.InvariantCulture) + "°";
        _lightIntensityValue.text = light.Intensity.ToString("0.0", CultureInfo.InvariantCulture);
        _lightSwatch.color = light.Color;
    }

    public void AddInlineExpressionControls(Func<IReadOnlyDictionary<string, ExpressionPreset>> load,
        Func<string, string, bool> saveCurrent, Func<string, bool> apply,
        Action<string, float> adjust, Func<string, bool> delete)
    {
        _inlineLoadExpressions = load;
        _inlineApplyExpression = apply;
        var panel = _root.transform.Find("Panel").GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(480, 780);
        _blushValue = AddInlineValueControl(panel, L.T("赤面", "Blush"), "blush", 14, -413, adjust);
        _eyesOpenValue = AddInlineValueControl(panel, L.T("目の開き", "Eyes open"), "eyes_open", 14, -452, adjust, "eyes_auto");
        _mouthOpenValue = AddInlineValueControl(panel, L.T("口の開き", "Mouth open"), "mouth_open", 240, -452, adjust, "mouth_auto");
        MakeText("ExpressionSaveTitle", panel, L.T("現在の表情を全キャラ共通プリセットに保存", "Save the current face as a shared expression preset"), 15,
            new Vector2(14, -530), new Vector2(450, 26));
        _inlineExpressionKey = MakeInput(panel, new Vector2(14, -556), new Vector2(451, 34),
            L.T("表情ID（小文字英数字と _）", "Expression ID (lowercase letters, digits, _)"), 16);
        _inlineExpressionDescription = MakeInput(panel, new Vector2(14, -602), new Vector2(451, 36),
            L.T("AIに伝える表情の説明（日本語）", "Description the AI will read"), 16);
        MakeButton(panel, L.T("現在の表情を保存", "Save current face"), new Vector2(14, -650), new Vector2(240, 34), () =>
        {
            var key = _inlineExpressionKey.text.Trim();
            var description = _inlineExpressionDescription.text.Trim();
            if (saveCurrent(key, description))
            {
                RefreshInlineExpressionList(key);
                _inlineExpressionFeedback.text = L.T("全キャラ共通の表情として保存しました", "Saved as a shared expression.");
            }
            else _inlineExpressionFeedback.text = L.T("保存できませんでした。状態表示とログを確認してください", "Could not save. Check the status line and the log.");
        });
        MakeButton(panel, L.T("新規", "New"), new Vector2(261, -650), new Vector2(98, 34), () =>
        {
            _inlineExpressionIndex = -1;
            _inlineExpressionKey.SetTextWithoutNotify("");
            _inlineExpressionDescription.SetTextWithoutNotify("");
            _inlineExpressionSelected.text = L.T("新規", "New");
            _inlineExpressionKey.ActivateInputField();
        });
        // Deleting needs a second press within 10 seconds, like the bridge restart.
        MakeButton(panel, L.T("削除", "Delete"), new Vector2(366, -650), new Vector2(99, 34), () =>
        {
            var key = _inlineExpressionKey.text.Trim();
            if (key.Length == 0) { _inlineExpressionFeedback.text = L.T("削除する表情を『前』『次』で選んでください", "Choose the expression to delete with Prev / Next"); return; }
            if (_deleteArmedKey != key || Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedKey = key;
                _deleteArmedUntil = Time.unscaledTime + 10f;
                _inlineExpressionFeedback.text = L.T($"もう一度押すと『{key}』を削除します", $"Press again to delete \"{key}\"");
                return;
            }
            _deleteArmedKey = null;
            if (delete(key))
            {
                RefreshInlineExpressionList(null);
                _inlineExpressionFeedback.text = L.T($"『{key}』を削除しました", $"Deleted \"{key}\"");
            }
            else _inlineExpressionFeedback.text = L.T("削除できませんでした。状態表示を確認してください", "Could not delete. Check the status line.");
        });
        _inlineExpressionFeedback = MakeText("InlineExpressionFeedback", panel, "", 13,
            new Vector2(14, -689), new Vector2(451, 25));
        MakeButton(panel, L.T("前", "Prev"), new Vector2(14, -723), new Vector2(70, 30), () =>
            SelectInlineExpression(_inlineExpressionIndex < 0 ? _inlineExpressionKeys.Length - 1 : _inlineExpressionIndex - 1, true));
        MakeButton(panel, L.T("次", "Next"), new Vector2(90, -723), new Vector2(70, 30), () =>
            SelectInlineExpression(_inlineExpressionIndex + 1, true));
        _inlineExpressionSelected = MakeText("InlineExpressionSelected", panel, "", 14,
            new Vector2(169, -724), new Vector2(296, 28));
        RefreshInlineExpressionList(null);
    }

    private TMP_Text AddInlineValueControl(RectTransform panel, string label, string part,
        float x, float y, Action<string, float> adjust, string autoPart = null)
    {
        if (autoPart != null)
        {
            var auto = MakeButton(panel, L.T("自動", "Auto"), new Vector2(x + 112, y - 33), new Vector2(90, 25), () => adjust(autoPart, 0f));
            auto.gameObject.name = part + "Auto";
            _inlineFaceButtons.Add(auto);
        }
        // English labels need more room than the two-character Japanese ones.
        var labelWidth = L.En ? 74f : 62f;
        MakeText(part + "Label", panel, label, 14, new Vector2(x, y), new Vector2(labelWidth - 2, 27));
        var value = MakeText(part + "Value", panel, "-", 14,
            new Vector2(x + labelWidth, y), new Vector2(111 - labelWidth, 27));
        var minus = MakeButton(panel, "-", new Vector2(x + 112, y - 2),
            new Vector2(42, 27), () => adjust(part, -.1f));
        minus.gameObject.name = part + "Minus";
        _inlineFaceButtons.Add(minus);
        var plus = MakeButton(panel, "+", new Vector2(x + 160, y - 2),
            new Vector2(42, 27), () => adjust(part, .1f));
        plus.gameObject.name = part + "Plus";
        _inlineFaceButtons.Add(plus);
        return value;
    }

    private void RefreshInlineExpressionList(string selected)
    {
        _inlineExpressionKeys = _inlineLoadExpressions()?.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray()
            ?? Array.Empty<string>();
        if (selected == null)
        {
            _inlineExpressionIndex = -1;
            _inlineExpressionKey.SetTextWithoutNotify("");
            _inlineExpressionDescription.SetTextWithoutNotify("");
            _inlineExpressionSelected.text = L.T("新規 / 保存名を入力", "New / enter a name to save");
            return;
        }
        _inlineExpressionIndex = Array.FindIndex(_inlineExpressionKeys,
            key => string.Equals(key, selected, StringComparison.OrdinalIgnoreCase));
        if (_inlineExpressionIndex < 0) _inlineExpressionIndex = 0;
        SelectInlineExpression(_inlineExpressionIndex, false);
    }

    private void SelectInlineExpression(int index, bool apply)
    {
        if (_inlineExpressionKeys.Length == 0) return;
        _inlineExpressionIndex = (index % _inlineExpressionKeys.Length + _inlineExpressionKeys.Length) % _inlineExpressionKeys.Length;
        var key = _inlineExpressionKeys[_inlineExpressionIndex];
        if (!_inlineLoadExpressions().TryGetValue(key, out var preset)) return;
        _inlineExpressionKey.SetTextWithoutNotify(key);
        _inlineExpressionDescription.SetTextWithoutNotify(preset.Description ?? "");
        _inlineExpressionSelected.text = $"{_inlineExpressionIndex + 1}/{_inlineExpressionKeys.Length} {key}";
        if (apply) _inlineExpressionFeedback.text = _inlineApplyExpression(key)
            ? L.T("保存済みの表情を適用しました", "Applied the saved expression.") : L.T("現在は表情を切り替えられません", "The expression cannot be changed right now.");
    }

    private void OpenPersonalityEditor()
    {
        _personalityInput.SetTextWithoutNotify(_savedPersonalityPrompt);
        _personalityFeedback.text = "";
        _personalityOverlay.SetActive(true);
        _personalityInput.ActivateInputField();
    }

    public void OpenSettingsPanel(Func<BackendSettings> load) => OpenSettings(load);

    private void OpenSettings(Func<BackendSettings> load)
    {
        try
        {
            var settings = load();
            _bridgeEndpointInput.SetTextWithoutNotify(settings.BridgeEndpoint);
            _bridgeTokenInput.SetTextWithoutNotify(settings.BridgeToken);
            _provider = settings.Provider;
            _providerLabel.text = _provider == "ollama" ? "Ollama" : L.T("OpenAI互換API", "OpenAI-compatible API");
            _logThinking = settings.LogThinking;
            _thinkingLogLabel.text = _logThinking ? L.T("Ollama思考: ON (ログ記録)", "Ollama thinking: ON (logged)") : L.T("Ollama思考: OFF", "Ollama thinking: OFF");
            _upstreamUrlInput.SetTextWithoutNotify(settings.UpstreamUrl);
            _modelInput.SetTextWithoutNotify(settings.Model);
            _apiKeyInput.SetTextWithoutNotify(settings.ApiKey);
            _maxReplyInput.SetTextWithoutNotify(settings.MaxReplyTokens > 0 ? settings.MaxReplyTokens.ToString(CultureInfo.InvariantCulture) : "");
            _contextInput.SetTextWithoutNotify(settings.ContextTokens > 0 ? settings.ContextTokens.ToString(CultureInfo.InvariantCulture) : "");
            _historyInput.SetTextWithoutNotify(settings.HistoryMessages.ToString(CultureInfo.InvariantCulture));
            _advancedOverlay.SetActive(false);
            _settingsFeedback.text = "";
            _settingsOverlay.SetActive(true);
        }
        catch (Exception ex)
        {
            _settingsFeedback.text = ex.Message;
            _settingsOverlay.SetActive(true);
        }
    }

    private static int ReadNumber(TMP_InputField field, int empty) =>
        int.TryParse(field.text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : empty;

    public void SetVisible(bool visible)
    {
        if (!visible) _personalityOverlay.SetActive(false);
        if (!visible) _settingsOverlay.SetActive(false);
        if (!visible && _modelOverlay != null) _modelOverlay.SetActive(false);
        _root.SetActive(visible);
        if (visible) _input.ActivateInputField();
    }

    public void Refresh(string status, string character, string transcript, bool canSend, bool canTest, bool hasCharacter, bool eyesFollowCamera, bool neckFollowsCamera,
        (int eyebrow, int eyes, int mouth, int eyebrowCount, int eyesCount, int mouthCount) face,
        ExpressionPreset currentExpression)
    {
        _input.textComponent.color = InputTextColor;
        _status.text = status;
        _character.text = character;
        _send.interactable = canSend && !string.IsNullOrWhiteSpace(InputText);
        _uiTest.interactable = canTest;
        _performanceTest.interactable = canTest && hasCharacter;
        _eyeMode.interactable = canTest && hasCharacter;
        var eyeKey = _settings.ToggleEyes.Value.ToString().Replace("Alpha", "");
        var neckKey = _settings.ToggleNeck.Value.ToString().Replace("Alpha", "");
        _eyeModeLabel.text = eyesFollowCamera ? L.T($"視線: 追従 ({eyeKey})", $"Eyes: follow ({eyeKey})") : L.T($"視線: アニメ ({eyeKey})", $"Eyes: anim ({eyeKey})");
        _neckMode.interactable = canTest && hasCharacter;
        _neckModeLabel.text = neckFollowsCamera ? L.T($"首: 追従 ({neckKey})", $"Neck: follow ({neckKey})") : L.T($"首: アニメ ({neckKey})", $"Neck: anim ({neckKey})");
        _help.text = L.T($"{_settings.UnlockViewPrimary.Value}＋ボタン{_settings.OrbitMouseButton.Value}: 回転 / ボタン{_settings.PanMouseButton.Value}: 移動 / ホイール: ズーム\n{_settings.ResetViewPrimary.Value}: 視点リセット（文字入力中を除く）", $"{_settings.UnlockViewPrimary.Value}+button {_settings.OrbitMouseButton.Value}: orbit / button {_settings.PanMouseButton.Value}: pan / wheel: zoom\n{_settings.ResetViewPrimary.Value}: reset view (not while typing)");
        _eyebrowValue.text = face.eyebrowCount > 0 ? L.T($"眉 {face.eyebrow}/{face.eyebrowCount - 1}", $"Brow {face.eyebrow}/{face.eyebrowCount - 1}") : L.T("眉 -", "Brow -");
        _eyesValue.text = face.eyesCount > 0 ? L.T($"目 {face.eyes}/{face.eyesCount - 1}", $"Eyes {face.eyes}/{face.eyesCount - 1}") : L.T("目 -", "Eyes -");
        _mouthValue.text = face.mouthCount > 0 ? L.T($"口 {face.mouth}/{face.mouthCount - 1}", $"Mouth {face.mouth}/{face.mouthCount - 1}") : L.T("口 -", "Mouth -");
        foreach (var button in _faceButtons) button.interactable = canTest && hasCharacter;
        foreach (var button in _inlineFaceButtons) button.interactable = canTest && hasCharacter;
        if (currentExpression != null)
        {
            _blushValue.text = currentExpression.Blush.ToString("0.0", CultureInfo.InvariantCulture);
            _eyesOpenValue.text = OptionalText(currentExpression.EyesOpen);
            _mouthOpenValue.text = OptionalText(currentExpression.MouthOpen);
        }
    }

    private static string OptionalText(float? value) =>
        value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : L.T("自動", "Auto");

    public void Dispose()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private TMP_InputField MakeInput(RectTransform parent, Vector2 position, Vector2 size, string placeholderText = null, float fontSize = 20)
    {
        var rect = MakeRect("Input", parent, position, size);
        rect.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.10f, 0.15f,
            Mathf.Clamp(_settings.InputBackgroundOpacity.Value, 0.4f, 1f));
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 500;

        var viewport = MakeRect("Text Area", rect, new Vector2(12, -5), new Vector2(size.x - 24, size.y - 10));
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(12, 5);
        viewport.offsetMax = new Vector2(-12, -5);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = MakeText("Text", viewport, "", fontSize, Vector2.zero, viewport.sizeDelta);
        text.color = InputTextColor;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(text.rectTransform);
        var placeholder = MakeText("Placeholder", viewport, placeholderText ?? L.T("ここにメッセージを入力", "Type a message here"), fontSize, Vector2.zero, viewport.sizeDelta);
        placeholder.color = InputHintColor;
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(placeholder.rectTransform);
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.customCaretColor = true;
        input.caretColor = InputTextColor;
        input.selectionColor = new Color(0.37f, 0.62f, 0.86f, 0.55f);
        return input;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private Button MakeButton(RectTransform parent, string label, Vector2 position, Vector2 size, Action callback)
    {
        var rect = MakeRect(label, parent, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.15f, 0.4f, 0.62f, 0.96f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener((UnityAction)(() => callback()));
        var text = MakeText("Label", rect, label, 18, Vector2.zero, size, TextAlignmentOptions.Center);
        if (L.En)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = 10;
            text.fontSizeMax = 18;
        }
        return button;
    }

    private TMP_Text MakeText(string name, RectTransform parent, string value, float size, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var rect = MakeRect(name, parent, position, dimensions);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) text.font = _font;
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        // English labels run longer; keep single-line labels on one line by shrinking them.
        if (L.En && dimensions.y <= 30)
        {
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 9;
            text.fontSizeMax = size;
        }
        return text;
    }

    internal static RectTransform MakeRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var componentTypes = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
        componentTypes[0] = Il2CppType.Of<RectTransform>();
        var go = new GameObject(name, componentTypes);
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_FontAsset FindFont(string configuredPath)
    {
        if (_dynamicJapaneseFont != null) return _dynamicJapaneseFont;
        try
        {
            var fontPath = configuredPath;
            if (File.Exists(fontPath))
            {
                _dynamicJapaneseFont = TMP_FontAsset.CreateFontAsset(
                    fontPath,
                    0,
                    48,
                    5,
                    GlyphRenderMode.SDFAA,
                    2048,
                    2048,
                    AtlasPopulationMode.Dynamic,
                    true);
                if (_dynamicJapaneseFont != null)
                {
                    _dynamicJapaneseFont.TryAddCharacters(
                        "AI会話専用モード本編を開始せずこの画面だけで会話できますタイトル画面から利用できますセーブ進行は変更しません表示キャラクター読み込み中ここにメッセージを入力送信動作テスト演出中止閉じる検出時のみ有効こんにちは待機後の行目を表示しました。あなた：");
                    return _dynamicJapaneseFont;
                }
            }
        }
        catch (Exception ex)
        {
            AiChatBehaviour.LogSource?.LogError($"Japanese TMP font creation failed: {ex}");
        }

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (var i = 0; i < fonts.Length; i++)
            if (fonts[i] != null && fonts[i].HasCharacter('あ'))
                return fonts[i];

        var texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
        for (var i = 0; i < texts.Length; i++)
        {
            var value = texts[i]?.text;
            if (texts[i]?.font != null && !string.IsNullOrEmpty(value) && value.Any(ch => ch >= '\u3000'))
                return texts[i].font;
        }
        for (var i = 0; i < texts.Length; i++)
            if (texts[i]?.font != null)
                return texts[i].font;
        return TMP_Settings.defaultFontAsset;
    }
}
