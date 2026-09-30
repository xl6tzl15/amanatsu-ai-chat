using BepInEx.Logging;
using Amanatsu.AiChat.Config;
using Character;
using ILLGAMES.ADV;
using ILLGAMES.Unity.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Amanatsu.AiChat.GameAdapter;

// Owns a standalone instance of the game's ADV presentation.
internal sealed class DedicatedCharacterStage : IDisposable
{
    private readonly ManualLogSource _log;
    private readonly ModSettings _settings;
    private ADVCore _core;
    private Human _human;
    private bool _requested;
    private bool _loaded;
    private bool _ready;
    private bool _failed;
    private float _nextAttempt;
    private string _cardPath;
    private string _activeCardPath;
    private readonly List<(CanvasGroup Group, bool Added, float Alpha, bool Interactable, bool Raycasts)> _titleGroups = new();
    private readonly HeroineDialogueFrame _dialogueFrame = new();
    private readonly BackgroundBackdrop _background = new();
    private readonly ConversationPostProcessing _postProcessing = new();
    private bool _cameraGraphicsCaptured;
    private bool _oldPostProcessing;
    private LayerMask _oldVolumeMask;
    private Transform _oldVolumeTrigger;
    private AntialiasingMode _oldAntialiasing;
    private Camera _camera;
    private GameObject _light;
    private Light _oldSun;
    private Animator _animator;
    private string[] _cards = Array.Empty<string>();
    private int _cardIndex;
    private float _distance = 0.75f;
    private const float MinimumDistance = 0.05f;
    private const float MaximumDistance = 2.5f;
    private float _height = 16f;
    private float _yaw;
    private float _pitch = 3f;
    private Vector3 _pan;
    private bool _viewUnlocked;
    private CursorLockMode _oldCursorLock;
    private bool _oldCursorVisible;
    private readonly GameObject _uiRoot;
    private string _lastText = "";
    private int _requestedMotion;
    private bool _eyesFollowCamera = true;
    private bool _neckFollowsCamera;
    private bool _builtInPreset;
    private float _targetHeight = .72f;
    private bool _cameraInitialized;
    private int _dragButton = -1;
    private Canvas _nativeCanvas;
    private CanvasGroup _nativeCanvasGroup;
    public bool NativeWindowVisible => _nativeCanvas != null && _nativeCanvas.enabled &&
        _nativeCanvas.gameObject.activeInHierarchy && _nativeCanvasGroup != null && _nativeCanvasGroup.alpha > .01f;

    public DedicatedCharacterStage(GameObject uiRoot, ManualLogSource log, ModSettings settings)
    { _log = log; _uiRoot = uiRoot; _settings = settings; }
    public Human Human => _human;
    public string CharacterName => _human?.Data?.Parameter?.fullname;
    public string ActiveCardPath => _activeCardPath;
    public bool IsReady => _ready && !_failed;
    public bool EyesFollowCamera => _eyesFollowCamera;
    public bool NeckFollowsCamera => _neckFollowsCamera;
    public string Status => _failed ? "ADV initialization failed" : IsReady ? "ADV ready" : "loading ADV";

    public bool Enter(string cardPath, bool builtInPreset = false)
    {
        _cardPath = cardPath;
        _builtInPreset = builtInPreset;
        _requested = true;
        _failed = false;
        return IsReady;
    }

    public void Maintain()
    {
        if (!_requested || _failed || _ready || Time.realtimeSinceStartup < _nextAttempt) return;
        _nextAttempt = Time.realtimeSinceStartup + 1f;
        try
        {
            if (!_loaded)
            {
                if (SceneManager.GetActiveScene().name != "Title") return;
                ADVSetup.Load();
                _loaded = true;
                _log.LogInfo("Standalone ADV load requested.");
                return;
            }
            _core = ADVSetup.Core;
            if (_core?.Scenario?.TextController == null) return;
            var scenario = _core.Scenario;
            _nativeCanvas = scenario._windowImage?.GetComponentInParent<Canvas>();
            if (_nativeCanvas != null)
            {
                _nativeCanvasGroup = _nativeCanvas.GetComponent<CanvasGroup>();
                if (_nativeCanvasGroup == null) _nativeCanvasGroup = _nativeCanvas.gameObject.AddComponent<CanvasGroup>();
                _nativeCanvasGroup.alpha = 1f;
                _nativeCanvasGroup.blocksRaycasts = true;
                _nativeCanvasGroup.interactable = true;
            }
            _log.LogInfo($"ADV bound: object={_core.gameObject.name}, scene={_core.gameObject.scene.name}, textInitialized={scenario.TextController.Initialized}");
            scenario.enabled = false;
            var data = new HumanData((byte)1);
            var path = string.IsNullOrWhiteSpace(_cardPath) ? null : Path.GetFullPath(_cardPath);
            if (_builtInPreset)
            { if (!data.LoadFromPreset(1)) throw new InvalidDataException("Built-in character preset could not be loaded."); }
            else if (path == null || !data.LoadCharaFile(path)) throw new InvalidDataException("Female character card could not be loaded.");
            _activeCardPath = path;
            _log.LogInfo($"ADV creating character: {path}");
            _human = Character.Human.Create(data);
            _log.LogInfo($"ADV character created: {_human?.GameObject?.name}");
            _cards = Directory.GetFiles(Path.GetFullPath("UserData/chara/female"), "*.png").OrderBy(p => p).ToArray();
            _cardIndex = Array.FindIndex(_cards, p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            SetupCharacter();
            _core.gameObject.SetActive(true);
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                if (canvas.gameObject.scene.name != "Title" || canvas.transform.IsChildOf(_uiRoot.transform)) continue;
                var group = canvas.GetComponent<CanvasGroup>();
                var added = group == null;
                if (added) group = canvas.gameObject.AddComponent<CanvasGroup>();
                if (group == null) continue;
                _titleGroups.Add((group, added, group.alpha, group.interactable, group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            _camera = _core.CharaCamera ?? _core.EnvCamera;
            _log.LogInfo($"ADV camera={_camera?.name}, env={_core.EnvCamera?.name}, chara={_core.CharaCamera?.name}");
            if (_camera != null)
            {
                _camera.gameObject.SetActive(true);
                _camera.enabled = true;
                _camera.cullingMask = ~(1 << 5);
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.14f, 0.22f, 0.27f);
                _camera.fieldOfView = Mathf.Clamp(_settings.FieldOfView.Value, 20f, 70f);
                _camera.nearClipPlane = 0.03f;
                _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, 200f);
                SetCameraPreset(_settings.CameraPreset.Value);
                UpdateCamera(true);
                ApplyGazeMode();
                try { _background.Apply(_camera, _settings.BackgroundImage.Value); }
                catch (Exception ex) { _log.LogWarning($"Background image unavailable: {ex.Message}"); }
            }
            // Mirrors the map's CameraLight prefab: a camera-relative key light that
            // only affects character layers (7, 9), with the game's soft shadows.
            _light = new GameObject("AiConversationLight");
            var light = _light.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = (1 << 7) | (1 << 9);
            light.shadows = AiChatBehaviour.Graphics.ShadowType.Value;
            light.shadowStrength = 1f;
            if (_camera != null) _light.transform.SetParent(_camera.transform, false);
            ApplyLightSettings();
            _oldSun = RenderSettings.sun;
            RenderSettings.sun = light;
            scenario.VisibleWindowForce = true;
            scenario.TextController.Set(CharacterName ?? L.T("AI会話", "AI Chat"), "……");
            scenario.TextController.ForceCompleteDisplayText();
            _dialogueFrame.Apply(scenario._windowImage, _log);
            _ready = true;
            AiChatBehaviour.Graphics.Enter();
            RefreshGraphics();
            _log.LogInfo("Native ADV message window ready.");
        }
        catch (Exception ex)
        {
            _failed = true;
            _log.LogError($"Standalone ADV initialization failed: {ex}");
            Exit(); _failed = true;
        }
    }

    public bool ShowText(string text)
    {
        if (!IsReady) return false;
        _pages.Clear();
        _pages.AddRange(Sequence.DialoguePages.Split(text ?? ""));
        _page = 0;
        _lastText = text;
        DisplayPage();
        _log.LogInfo($"Native ADV text displayed: speaker={CharacterName}, characters={text?.Length ?? 0}, pages={_pages.Count}");
        return true;
    }

    // Long replies are split into pages; the window shows one page and ▼ while more follow.
    private readonly List<string> _pages = new();
    private int _page;
    public bool HasMorePages => _page + 1 < _pages.Count;

    public bool NextPage()
    {
        if (!IsReady || !HasMorePages) return false;
        _page++;
        DisplayPage();
        return true;
    }

    private void DisplayPage()
    {
        _core.Scenario.VisibleWindowForce = true;
        var text = _pages[_page] + (HasMorePages ? " ▼" : "");
        _core.Scenario.TextController.Set(CharacterName ?? L.T("AI会話", "AI Chat"), text);
    }

    // True when the screen point is on the native dialogue window.
    public bool DialogueWindowContains(Vector2 screenPoint)
    {
        var image = IsReady ? _core.Scenario._windowImage : null;
        if (image == null) return false;
        var canvas = image.canvas;
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(image.rectTransform, screenPoint, camera);
    }

    public void SetBackground(string spec)
    {
        if (!IsReady || _camera == null) throw new InvalidOperationException(L.T("会話画面の準備ができていません。", "The chat stage is not ready."));
        _background.Apply(_camera, spec);
        _settings.BackgroundImage.Value = spec;
        _log.LogInfo($"Conversation background selected: {BackgroundBackdrop.Label(spec)}");
    }

    private void SetupCharacter()
    {
        _human.Transform.position = Vector3.zero;
        _human.Transform.rotation = Quaternion.identity;
        _human.Body.LoadAnimation("ani/mot/1/000_00.unity3d", "f_00");
        _animator = _human.Body.animBody;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PlayMotion(0);
        var renderers = _human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var top = 0f;
        foreach (var renderer in renderers)
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                top = Mathf.Max(top, renderer.bounds.max.y);
        // Accessory bounds can be much taller than the body and otherwise push
        // both the orbit target and the closest camera position away from the face.
        if (top > 0.5f) _height = Mathf.Clamp(top, 14f, 21f);
        foreach (var bone in _human.GameObject.GetComponentsInChildren<Transform>(true))
            if (bone.name.Equals("cf_J_Head", StringComparison.OrdinalIgnoreCase))
            { if (bone.position.y > 1f) _height = bone.position.y * 1.08f; break; }
        _log.LogInfo($"ADV character height={_height}, modelScale={Character.Human.MODEL_SCALE}");
        UpdateCamera();
    }

    private int _idlePose = -1;
    public int IdlePose => _idlePose >= 0 ? _idlePose : _human?.Data?.Parameter?.personality ?? 0;

    public int[] AvailableIdlePoses()
    {
        if (_animator?.runtimeAnimatorController == null) return Array.Empty<int>();
        return Enumerable.Range(0, 12).Where(i => _animator.HasState(0, Animator.StringToHash(MotionCatalog.StateFor(0, i)))).ToArray();
    }

    // Changes the standing pose used for idle; returns false if the state is missing.
    public bool SetIdlePose(int pose, bool play = true)
    {
        if (pose >= 0 && _animator != null && !_animator.HasState(0, Animator.StringToHash(MotionCatalog.StateFor(0, pose)))) return false;
        _idlePose = pose;
        if (play && _requestedMotion == 0) PlayMotion(0);
        return true;
    }

    public bool PlayMotion(int motionIndex)
    {
        if (_animator?.runtimeAnimatorController == null) return false;
        // Only verified states from the game's controller may be requested.
        var state = MotionCatalog.StateFor(motionIndex, _idlePose >= 0 ? _idlePose : _human?.Data?.Parameter?.personality ?? 0);
        if (state == null) return false;
        var hash = Animator.StringToHash(state);
        if (!_animator.HasState(0, hash)) return false;
        _animator.CrossFadeInFixedTime(hash, 0.15f, 0, 0f);
        _requestedMotion = motionIndex;
        if (motionIndex == 0) ApplyGazeMode();
        _log.LogInfo($"Motion requested: id={motionIndex}, state={state}, hash={hash}");
        return true;
    }

    public int Coordinate => _human == null ? -1 : (int)_human.FileStatus.coordinateType;

    // Swimsuit <-> after-bath through the game's own reload; the rebuilt model needs the
    // conversation's gaze mode and idle motion again.
    public bool SwitchCoordinate()
    {
        if (!IsReady || _human == null) return false;
        var next = Coordinate == 0 ? HumanCoordinate.Define.CoordinateType.AfterBath : HumanCoordinate.Define.CoordinateType.Swimsuit;
        if (!_human.Coorde.ChangeCoordinateTypeAndReload(next, true)) return false;
        PlayMotion(0);
        ApplyGazeMode();
        _log.LogInfo($"Conversation coordinate switched to {next}.");
        return true;
    }

    public void ToggleEyeLookMode()
    {
        if (!IsReady) return;
        _eyesFollowCamera = !_eyesFollowCamera;
        ApplyGazeMode();
    }

    public void ToggleNeckLookMode()
    {
        if (!IsReady) return;
        _neckFollowsCamera = !_neckFollowsCamera;
        ApplyGazeMode();
    }

    private void ApplyGazeMode()
    {
        if (_camera == null || _human?.Face == null) return;
        try
        {
            var face = _human.Face;
            var eyes = face.eyeLookCtrl;
            var neck = face.neckLookCtrl;
            var eyePattern = -1;
            var neckPattern = -1;
            if (eyes != null)
            {
                if (_eyesFollowCamera) eyes.Target = _camera.transform;
                var states = eyes.EyeLookScript?.EyeTypeStates;
                if (states != null)
                    for (var i = 0; i < states.Length; i++)
                        if (states[i] != null && states[i].LookType == (_eyesFollowCamera
                            ? EYE_LOOK_TYPE.TARGET : EYE_LOOK_TYPE.NO_LOOK))
                        {
                            eyes.PtnNo = eyePattern = i;
                            break;
                        }
            }
            if (neck != null)
            {
                if (_neckFollowsCamera) neck.Target = _camera.transform;
                var states = neck.NeckLookScript?.neckTypeStates;
                if (states != null)
                    for (var i = 0; i < states.Length; i++)
                        if (states[i] != null && states[i].lookType == (_neckFollowsCamera
                            ? NECK_LOOK_TYPE_VER2.TARGET : NECK_LOOK_TYPE_VER2.ANIMATION))
                        {
                            neck.PtnNo = neckPattern = i;
                            break;
                        }
            }
            _log.LogInfo($"Gaze mode: eyes={(_eyesFollowCamera ? "camera" : "animation")}, neck={(_neckFollowsCamera ? "camera" : "animation")}, eyePattern={eyePattern}, neckPattern={neckPattern}.");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Camera gaze setup skipped: {ex}");
        }
    }

    private Transform _hips;

    // Signed yaw (degrees) between the pelvis' facing and the direction to the camera; 0 = facing the camera.
    public float BodyYawToCamera()
    {
        if (_human == null || _camera == null) return float.NaN;
        if (_hips == null || !_hips.IsChildOf(_human.Transform))
            _hips = _human.GameObject.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Equals("cf_J_Hips", StringComparison.OrdinalIgnoreCase));
        if (_hips == null) return float.NaN;
        var forward = Vector3.ProjectOnPlane(_hips.forward, Vector3.up);
        var toCamera = Vector3.ProjectOnPlane(_camera.transform.position - _hips.position, Vector3.up);
        return Vector3.SignedAngle(toCamera, forward, Vector3.up);
    }

    public object Diagnostics()
    {
        if (!IsReady || _animator == null) return new { ready = false, status = Status };
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        var bones = new Dictionary<string, object>();
        var renderers = _human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var sampled = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            foreach (var t in renderer.bones)
            {
                if (t == null || bones.ContainsKey(t.name)) continue;
                var p = t.position;
                var q = t.rotation;
                bones[t.name] = new { position = new[] { p.x, p.y, p.z }, rotation = new[] { q.x, q.y, q.z, q.w } };
                if (++sampled >= 40) break;
            }
            if (sampled >= 40) break;
        }
        return new { ready = true, character = CharacterName,
            cardId = Path.GetFileNameWithoutExtension(_activeCardPath), personality = _human.Data.Parameter.personality,
            requestedMotion = _requestedMotion, bodyYaw = BodyYawToCamera(), rootYaw = _human.Transform.eulerAngles.y,
            eyesFollowCamera = _eyesFollowCamera, neckFollowsCamera = _neckFollowsCamera,
            eyePattern = _human.Face?.eyeLookCtrl?.PtnNo ?? -1,
            neckPattern = _human.Face?.neckLookCtrl?.PtnNo ?? -1,
            stateHash = state.fullPathHash, expectedHash = Animator.StringToHash(MotionCatalog.StateFor(_requestedMotion, _human.Data.Parameter.personality)),
            normalizedTime = state.normalizedTime, inTransition = _animator.IsInTransition(0),
            camera = _camera == null ? null : new { distance = _distance, yaw = _yaw, pitch = _pitch,
                targetHeight = _targetHeight, fov = _camera.fieldOfView, height = _height,
                position = new[] { _camera.transform.position.x, _camera.transform.position.y, _camera.transform.position.z } },
            bones, text = _lastText,
            background = new { spec = _background.CurrentSpec, visible = _background.Visible },
            postProcessing = PostProcessingDiagnostics() };
    }

    public void SelectCard(int direction)
    {
        if (!IsReady || _cards.Length == 0) return;
        var next = _cardIndex < 0
            ? (direction >= 0 ? 0 : _cards.Length - 1)
            : (_cardIndex + direction + _cards.Length) % _cards.Length;
        SelectCard(_cards[next]);
    }

    public void SelectCard(string path)
    {
        if (!IsReady) throw new InvalidOperationException("Conversation stage is not ready.");
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Character card must be a PNG file.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Character card not found.", fullPath);
        var data = new HumanData((byte)1);
        if (!data.LoadCharaFile(fullPath)) throw new InvalidDataException("Character card load failed.");
        var replacement = Character.Human.Create(data);
        if (replacement == null) throw new InvalidDataException("Character could not be created from card.");
        var previous = _human;
        _human = replacement;
        try { SetupCharacter(); }
        catch
        {
            _human = previous;
            _animator = previous?.Body?.animBody;
            replacement.Dispose();
            throw;
        }
        _cardIndex = Array.FindIndex(_cards, card => string.Equals(card, fullPath, StringComparison.OrdinalIgnoreCase));
        _activeCardPath = fullPath;
        previous?.Dispose();
        ShowText("……");
        _log.LogInfo($"ADV character selected: {fullPath}");
    }

    public void Rotate(float degrees)
    {
        if (_human != null) _human.Transform.Rotate(0f, degrees, 0f);
    }

    public void Zoom(float steps)
    {
        _distance = Mathf.Clamp(_distance * Mathf.Pow(1.25f, steps), MinimumDistance, MaximumDistance);
        UpdateCamera();
    }

    public void UpdateView(bool typing)
    {
        if (!IsReady) return;
        if (typing) { _dragButton = -1; RestoreCursor(); UpdateCamera(); return; }
        var unlocked = ModSettings.Held(_settings.UnlockViewPrimary.Value)
            || ModSettings.Held(_settings.UnlockViewSecondary.Value);
        if (unlocked && !_viewUnlocked)
        {
            _oldCursorLock = Cursor.lockState;
            _oldCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _viewUnlocked = true;
        }
        if (!unlocked) RestoreCursor();
        if (!typing && (ModSettings.Pressed(_settings.ResetViewPrimary.Value)
            || ModSettings.Pressed(_settings.ResetViewSecondary.Value)))
        {
            ResetView();
            return;
        }
        if (!unlocked) { _dragButton = -1; UpdateCamera(); return; }
        var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (_dragButton >= 0 && !Input.GetMouseButton(_dragButton)) _dragButton = -1;
        if (_dragButton < 0 && !overUi)
            foreach (var button in new[] { _settings.OrbitMouseButton.Value, _settings.PanMouseButton.Value, _settings.AlternatePanMouseButton.Value })
                if (button is >= 0 and <= 2 && Input.GetMouseButtonDown(button)) { _dragButton = button; break; }
        var x = Input.GetAxis("Mouse X");
        var y = Input.GetAxis("Mouse Y");
        var nativeCamera = Manager.Config.CameraData;
        if (nativeCamera != null)
        {
            x *= Mathf.Clamp(nativeCamera.SensitivityX * 2f, .02f, 4f) * (nativeCamera.InvertMoveX ? -1f : 1f);
            y *= Mathf.Clamp(nativeCamera.SensitivityY * 2f, .02f, 4f) * (nativeCamera.InvertMoveY ? -1f : 1f);
        }
        if (_dragButton >= 0 && _dragButton == _settings.OrbitMouseButton.Value)
        {
            var sensitivity = Mathf.Clamp(_settings.OrbitSensitivity.Value, 0.1f, 10f);
            _yaw += x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - y * sensitivity, -65f, 65f);
        }
        else if (_dragButton >= 0)
        {
            var sensitivity = Mathf.Clamp(_settings.PanSensitivity.Value, 0.001f, 0.05f);
            _pan += (_camera.transform.right * -x + _camera.transform.up * -y) * _height * sensitivity;
        }
        if (!overUi) _distance = Mathf.Clamp(_distance * Mathf.Pow(1.25f, -Input.mouseScrollDelta.y), MinimumDistance, MaximumDistance);
        UpdateCamera();
    }

    public void ResetView()
    {
        SetCameraPreset(_settings.CameraPreset.Value);
    }

    public void SetCameraPreset(string preset)
    {
        (_distance, _targetHeight) = preset switch
        {
            "face" => (.30f, .89f),
            "full" => (1.95f, .50f),
            _ => (.75f, .84f)
        };
        _yaw = 0f;
        _pitch = 3f;
        _pan = Vector3.zero;
        UpdateCamera();
    }

    private void RestoreCursor()
    {
        if (!_viewUnlocked) return;
        Cursor.lockState = _oldCursorLock;
        Cursor.visible = _oldCursorVisible;
        _viewUnlocked = false;
    }

    public static readonly (float Vertical, float Horizontal, float Intensity, string Color) LightDefaults = (25f, -30f, 1f, "#DFE6E6FF");

    public void ApplyLightSettings()
    {
        var light = _light?.GetComponent<Light>();
        if (light == null) return;
        light.intensity = Mathf.Clamp(_settings.LightIntensity.Value, 0f, 3f);
        light.color = ColorUtility.TryParseHtmlString(_settings.LightColor.Value, out var color) ? color : new Color(0.873f, 0.901f, 0.901f);
        _light.transform.localRotation = Quaternion.Euler(Mathf.Clamp(_settings.LightVertical.Value, -90f, 90f),
            Mathf.Repeat(_settings.LightHorizontal.Value + 180f, 360f) - 180f, 0f);
    }

    public Color LightColor => _light?.GetComponent<Light>()?.color ?? Color.white;

    public void RefreshGraphics()
    {
        if (_camera != null)
        {
            var extra = _camera.GetComponent<UniversalAdditionalCameraData>();
            if (extra != null)
            {
                if (!_cameraGraphicsCaptured)
                {
                    _oldPostProcessing = extra.renderPostProcessing;
                    _oldVolumeMask = extra.volumeLayerMask;
                    _oldVolumeTrigger = extra.volumeTrigger;
                    _oldAntialiasing = extra.antialiasing;
                    _cameraGraphicsCaptured = true;
                }
                _postProcessing.Ensure(_camera, _log);
                _postProcessing.Refresh(_camera);
            }
            if (Manager.Config.GraphicData != null) _camera.backgroundColor = Manager.Config.GraphicData.BackColor;
        }
        if (_light != null) _light.GetComponent<Light>().shadows = AiChatBehaviour.Graphics.ShadowType.Value;
        GraphicPreferences.Refresh();
    }

    private object PostProcessingDiagnostics()
    {
        var extra = _camera == null ? null : _camera.GetComponent<UniversalAdditionalCameraData>();
        var volumes = UnityEngine.Object.FindObjectsOfType<Volume>(true);
        return new { enabled = extra?.renderPostProcessing == true,
            mask = extra == null ? 0 : extra.volumeLayerMask.value,
            applied = _postProcessing.Diagnostics(),
            volumes = volumes.Take(24).Select(v => new { name = v.name,
                active = v.isActiveAndEnabled, layer = v.gameObject.layer,
                global = v.isGlobal, weight = v.weight,
                components = VolumeComponentNames(v) }).ToArray() };
    }

    private static string[] VolumeComponentNames(Volume volume)
    {
        if (volume.profileRef == null) return Array.Empty<string>();
        var components = volume.profileRef.components;
        var result = new string[components.Count];
        for (var i = 0; i < components.Count; i++) result[i] = components[i]?.name ?? "null";
        return result;
    }

    private void UpdateCamera(bool snap = false)
    {
        if (_camera == null) return;
        var target = new Vector3(0f, _height * _targetHeight, 0f) + _pan;
        var position = target + Quaternion.Euler(-_pitch, _yaw, 0f) * new Vector3(0f, 0f, _height * _distance);
        var rotation = Quaternion.LookRotation(target - position);
        var smoothing = Mathf.Clamp(_settings.CameraSmoothing.Value, 0f, .5f);
        var t = snap || !_cameraInitialized || smoothing <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothing);
        _camera.transform.position = Vector3.Lerp(_camera.transform.position, position, t);
        _camera.transform.rotation = Quaternion.Slerp(_camera.transform.rotation, rotation, t);
        _cameraInitialized = true;
        _background.RefreshFrame();
    }

    // Hides the stage while another scene (H) owns the screen, without releasing it.
    public bool Suspended { get; private set; }

    public void Suspend()
    {
        if (!IsReady || Suspended) return;
        Suspended = true;
        if (_human != null) _human.GameObject.SetActive(false);
        if (_camera != null) _camera.enabled = false;
        if (_light != null) { RenderSettings.sun = _oldSun; _light.SetActive(false); }
        if (_nativeCanvasGroup != null) { _nativeCanvasGroup.alpha = 0f; _nativeCanvasGroup.blocksRaycasts = false; _nativeCanvasGroup.interactable = false; }
        _uiRoot.SetActive(false);
        _postProcessing.SetActive(false);
        AiChatBehaviour.Graphics.Exit();
        RestoreCursor();
    }

    public void Resume()
    {
        if (!Suspended) return;
        Suspended = false;
        _uiRoot.SetActive(true);
        AiChatBehaviour.Graphics.Enter();
        _postProcessing.SetActive(true);
        if (_human != null) _human.GameObject.SetActive(true);
        if (_camera != null) _camera.enabled = true;
        if (_light != null) { _light.SetActive(true); RenderSettings.sun = _light.GetComponent<Light>(); }
        if (_nativeCanvasGroup != null) { _nativeCanvasGroup.alpha = 1f; _nativeCanvasGroup.blocksRaycasts = true; _nativeCanvasGroup.interactable = true; }
        PlayMotion(_requestedMotion);
        RefreshGraphics();
    }

    public void Exit()
    {
        Suspended = false;
        _requested = false;
        AiChatBehaviour.Graphics.Exit();
        _postProcessing.Dispose();
        if (_cameraGraphicsCaptured && _camera != null)
        {
            var extra = _camera.GetComponent<UniversalAdditionalCameraData>();
            if (extra != null)
            {
                extra.renderPostProcessing = _oldPostProcessing;
                extra.volumeLayerMask = _oldVolumeMask;
                extra.volumeTrigger = _oldVolumeTrigger;
                extra.antialiasing = _oldAntialiasing;
            }
        }
        _cameraGraphicsCaptured = false;
        _cameraInitialized = false;
        _dragButton = -1;
        RestoreCursor();
        _dialogueFrame.Restore();
        _background.Dispose();
        if (_core?.Scenario != null) _core.Scenario.VisibleWindowForce = false;
        if (_nativeCanvasGroup != null)
        {
            _nativeCanvasGroup.alpha = 0f;
            _nativeCanvasGroup.blocksRaycasts = false;
            _nativeCanvasGroup.interactable = false;
        }
        if (_camera != null) _camera.enabled = false;
        if (_core != null) _core.gameObject.SetActive(false);
        _ready = false;
        _activeCardPath = null;
        if (_human != null) { _human.Dispose(); _human = null; }
        if (_light != null) { RenderSettings.sun = _oldSun; UnityEngine.Object.Destroy(_light); _light = null; }
        foreach (var state in _titleGroups)
        {
            if (state.Group == null) continue;
            if (state.Added) UnityEngine.Object.Destroy(state.Group);
            else { state.Group.alpha = state.Alpha; state.Group.interactable = state.Interactable; state.Group.blocksRaycasts = state.Raycasts; }
        }
        _titleGroups.Clear();
        if (_loaded) ADVSetup.Dispose();
        _core = null;
        _nativeCanvas = null;
        _nativeCanvasGroup = null;
        _loaded = false;
    }

    public void Dispose() => Exit();
}
