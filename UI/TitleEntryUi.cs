using AL.Title;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Amanatsu.AiChat.UI;

internal sealed class TitleEntryUi : IDisposable
{
    private readonly GameObject _root;
    private readonly Button _button;
    private TitleScene _title;
    private float _nextSearch;
    public bool Visible => _root != null && _root.activeSelf;

    public TitleEntryUi(TMP_FontAsset font, Action open)
    {
        _root = new GameObject("AiChatTitleEntry");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = _root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        _root.AddComponent<GraphicRaycaster>();
        var rect = DedicatedChatUi.MakeRect("AmanatsuAiChatTitleButton", _root.transform, Vector2.zero, new Vector2(340, 52));
        rect.anchorMin = rect.anchorMax = new Vector2(0, 0); rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(36, 36);
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.18f, .12f, .22f, .94f);
        _button = rect.gameObject.AddComponent<Button>(); _button.targetGraphic = image;
        _button.onClick.AddListener((UnityAction)(() => open()));
        var label = DedicatedChatUi.MakeRect("Label", rect, Vector2.zero, rect.sizeDelta).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = ModIdentity.Label; label.fontSize = 20; label.color = new Color(1f,.88f,.94f); label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        _root.SetActive(false);
    }

    public void Tick(bool chatOpen, bool modalOpen)
    {
        var show = !chatOpen && !modalOpen && SceneManager.GetActiveScene().name == "Title";
        if (show && (_title == null || Time.unscaledTime >= _nextSearch))
        { _title = UnityEngine.Object.FindObjectOfType<TitleScene>(); _nextSearch = Time.unscaledTime + 1f; }
        show = show && _title != null && _title._menuButtons != null
            && _title._menuButtons.Any(b => b != null && b.gameObject.activeInHierarchy && b.IsInteractable());
        if (_root.activeSelf != show) _root.SetActive(show);
    }

    public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root); }
}
