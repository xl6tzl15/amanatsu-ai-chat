using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BeautifyVolume = Beautify.Universal.Beautify;

namespace Amanatsu.AiChat.GameAdapter;

// The standalone ADV stage has no map Volume. This rebuilds the game's map look
// (Beautify + URP Bloom + color balance, values read from map005 "morning") and
// maps the native graphics options onto it every refresh.
internal sealed class ConversationPostProcessing : IDisposable
{
    private const int VolumeLayer = 29;
    private GameObject _root;
    private VolumeProfile _profile;
    private BeautifyVolume _beautify;
    private Bloom _bloom;
    private Vignette _vignette;
    private ScriptableRendererFeature _ssaoFeature;
    private bool _oldSsaoActive;

    public int LayerMask => 1 << VolumeLayer;
    public bool Ready => _root != null && _profile != null;
    public object Diagnostics() => new { ready = Ready,
        beautifyBloom = _beautify?.bloomIntensity.value, urpBloom = _bloom?.active == true,
        depthOfField = _beautify?.depthOfField.value, vignette = _vignette?.active == true,
        ssaoFeature = _ssaoFeature?.name, ssaoActive = _ssaoFeature?.isActive };

    // Off while another scene (H) renders, so this global Volume does not blur or tint it.
    public void SetActive(bool active) { if (_root != null) _root.SetActive(active); }

    public void Ensure(Camera camera, ManualLogSource log)
    {
        if (Ready) return;
        var extra = camera.GetComponent<UniversalAdditionalCameraData>();
        if (extra == null) throw new InvalidOperationException(L.T("専用カメラにURP描画データがありません。", "The chat camera has no URP render data."));
        _root = new GameObject("AiConversationGraphicsVolume");
        _root.layer = VolumeLayer;
        var volume = _root.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        volume.weight = 1f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "AiConversationGraphicsProfile";
        _beautify = _profile.Add<BeautifyVolume>(false);
        var b = _beautify;
        b.tonemap.Override(Beautify.Universal.Beautify.TonemapOperator.ACES);
        b.tonemapExposurePre.Override(0.58f);
        b.tonemapBrightnessPost.Override(1.3f);
        b.saturate.Override(2.5f);
        b.contrast.Override(1.03f);
        b.sharpenIntensity.Override(3f);
        b.bloomIntensity.Override(1f);
        b.bloomThreshold.Override(0.85f);
        b.depthOfField.Override(true);
        b.depthOfFieldFocusMode.Override(Beautify.Universal.Beautify.DoFFocusMode.AutoFocus);
        b.depthOfFieldDistance.Override(12f);
        b.depthOfFieldAperture.Override(10f);
        b.depthOfFieldFocalLength.Override(0.009f);
        b.vignettingOuterRing.Override(-1f);
        b.vignettingInnerRing.Override(1f);
        b.vignettingFade.Override(0f);
        _bloom = _profile.Add<Bloom>(false);
        _bloom.threshold.Override(0.4f);
        _bloom.intensity.Override(0.6f);
        _bloom.scatter.Override(0.6f);
        _bloom.clamp.Override(3f);
        var balance = _profile.Add<ShadowsMidtonesHighlights>(false);
        balance.shadows.Override(new Vector4(0.97f, 0.85f, 1f, 0f));
        balance.midtones.Override(new Vector4(0.99f, 1f, 0.97f, 0f));
        balance.highlights.Override(new Vector4(0.94f, 0.96f, 1f, 0.01f));
        _vignette = _profile.Add<Vignette>(false);
        _vignette.intensity.Override(0.2f);
        _vignette.smoothness.Override(0.5f);
        volume.sharedProfile = _profile;
        var features = extra.scriptableRenderer?.TryCast<UniversalRenderer>()?.rendererFeatures;
        if (features != null)
            for (var i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                if (feature == null || feature.TryCast<ScreenSpaceAmbientOcclusion>() == null) continue;
                _ssaoFeature = feature;
                _oldSsaoActive = feature.isActive;
                break;
            }
        log.LogInfo($"Conversation Volume ready: Beautify/Bloom/color balance; SSAO feature={_ssaoFeature?.name ?? "none"}.");
    }

    public void Refresh(Camera camera)
    {
        if (!Ready || Manager.Config.GraphicData == null) return;
        var settings = Manager.Config.GraphicData;
        _beautify.bloomIntensity.value = settings.Bloom ? 1f : 0f;
        _bloom.active = settings.Bloom;
        _beautify.depthOfField.value = settings.DepthOfField;
        _vignette.active = settings.Vignette;
        if (_ssaoFeature != null) _ssaoFeature.SetActive(settings.SSAO);
        var extra = camera.GetComponent<UniversalAdditionalCameraData>();
        if (extra != null)
        {
            extra.renderPostProcessing = true;
            extra.volumeLayerMask = LayerMask;
            extra.volumeTrigger = camera.transform;
            extra.antialiasing = (AntialiasingMode)Mathf.Clamp(settings.Antialiasing, 0, 3);
        }
    }

    public void Dispose()
    {
        if (_ssaoFeature != null) _ssaoFeature.SetActive(_oldSsaoActive);
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_profile != null) UnityEngine.Object.Destroy(_profile);
        _root = null; _profile = null; _beautify = null; _bloom = null; _vignette = null; _ssaoFeature = null;
    }
}
