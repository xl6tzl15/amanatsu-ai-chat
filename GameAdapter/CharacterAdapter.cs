using AL;
using Character;
using BepInEx.Logging;
using Amanatsu.AiChat.Config;
using Amanatsu.AiChat.Sequence;
using UnityEngine;

namespace Amanatsu.AiChat.GameAdapter;

internal sealed class CharacterAdapter
{
    private readonly CharacterConfig _config;
    private readonly ManualLogSource _log;
    private readonly Func<Human> _dedicatedHuman;
    private readonly Func<int, bool> _dedicatedMotion;
    public Func<int, bool> PoseSetter { get; set; }

    public bool SetPose(string key)
    {
        var index = MotionCatalog.PoseIndex(key);
        if (index == null || PoseSetter == null) return false;
        var ok = PoseSetter(index.Value);
        if (ok) _log.LogInfo($"Pose applied: {key} -> Pose_D_{index.Value:00}");
        return ok;
    }
    private string _lastExpression = "neutral";
    private bool _lastBlushApplied;
    private float _lastBlushValue;
    public bool LastOutfitPartial { get; private set; }
    private readonly Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> _originalClothes = new();

    public CharacterAdapter(CharacterConfig config, ManualLogSource log, Func<Human> dedicatedHuman = null, Func<int, bool> dedicatedMotion = null)
    {
        _config = config;
        _log = log;
        _dedicatedHuman = dedicatedHuman;
        _dedicatedMotion = dedicatedMotion;
    }

    private static Heroine CurrentHeroine()
    {
        var game = Manager.Game.Instance;
        var heroines = game?._heroines;
        if (heroines == null || heroines.Count == 0)
            return null;
        for (var i = 0; i < heroines.Count; i++)
            if (heroines[i]?.Human?.Face != null)
                return heroines[i];
        return null;
    }

    private Human CurrentHuman() => _dedicatedHuman?.Invoke() ?? CurrentHeroine()?.Human;

    public bool HasCharacter => CurrentHuman() != null;

    public bool SetExpression(string logicalName)
    {
        if (!_config.Expressions.TryGetValue(logicalName, out var preset))
            return false;
        return ApplyExpressionPreset(preset, logicalName);
    }

    public bool ApplyExpressionPreset(ExpressionPreset preset, string logicalName = "preview")
    {
        if (preset == null) return false;
        var face = CurrentHuman()?.Face;
        if (face == null)
            return false;

        var limits = FaceParts();
        if (preset.Eyebrow < 0 || preset.Eyebrow >= limits.eyebrowCount
            || preset.Eyes < 0 || preset.Eyes >= limits.eyesCount
            || preset.Mouth < 0 || preset.Mouth >= limits.mouthCount)
            return false;
        face.ChangeEyebrowPtn(preset.Eyebrow, true);
        face.ChangeEyesPtn(preset.Eyes, true);
        face.ChangeMouthPtn(preset.Mouth, true);
        SetOpenness("eyes", preset.EyesOpen);
        SetOpenness("mouth", preset.MouthOpen);
        _lastBlushApplied = face.ChangeHohoAkaRate(new Il2CppSystem.Nullable<float>(preset.Blush));
        _lastBlushValue = preset.Blush;
        _lastExpression = logicalName;
        var applied = face.GetEyebrowPtn() == preset.Eyebrow && face.GetEyesPtn() == preset.Eyes
            && face.GetMouthPtn() == preset.Mouth;
        if (applied) _log.LogInfo($"Expression applied: {logicalName}, brow={preset.Eyebrow}, eyes={preset.Eyes}, mouth={preset.Mouth}, blush={preset.Blush}, blushCall={_lastBlushApplied}");
        return applied;
    }

    public (int eyebrow, int eyes, int mouth, int eyebrowCount, int eyesCount, int mouthCount) FaceParts()
    {
        var face = CurrentHuman()?.Face;
        if (face == null) return (-1, -1, -1, 0, 0, 0);
        return (face.GetEyebrowPtn(), face.GetEyesPtn(), face.GetMouthPtn(),
            face.eyebrowCtrl?.GetMaxPtn() ?? 0,
            Math.Min(face.GetEyesPtnNum(), face.eyesCtrl?.GetMaxPtn() ?? 0),
            face.mouthCtrl?.GetMaxPtn() ?? 0);
    }

    public ExpressionPreset CaptureExpression()
    {
        var face = CurrentHuman()?.Face;
        if (face == null) return null;
        return new ExpressionPreset(face.GetEyebrowPtn(), face.GetEyesPtn(), face.GetMouthPtn(), _lastBlushValue)
        {
            EyesOpen = FixedOpenness(face.eyesCtrl), MouthOpen = FixedOpenness(face.mouthCtrl)
        };
    }

    private static float? FixedOpenness(ILLGAMES.Unity.FBSBase ctrl) =>
        ctrl == null || ctrl.FixedRate < 0f ? null : ctrl.FixedRate;

    // FBSBase.FixedRate pins the blend-shape openness; a negative value returns control to
    // blinking (eyes) or the animation / lip sync (mouth). OpenMax only caps that range.
    public bool SetOpenness(string part, float? value)
    {
        var face = CurrentHuman()?.Face;
        var ctrl = part switch { "eyes" => (ILLGAMES.Unity.FBSBase)face?.eyesCtrl, "mouth" => face?.mouthCtrl, _ => null };
        if (ctrl == null) return false;
        if (part == "eyes") face.ChangeEyesOpenMax(1f);
        else { face.ChangeMouthOpenMin(0f); face.ChangeMouthOpenMax(1f); }
        var fixedValue = value.HasValue ? Math.Clamp(value.Value, 0f, 1f) + 0f : -0.1f;
        ctrl.SetFixedRate(fixedValue);
        if (part == "eyes") face.ChangeEyesBlinkFlag(!value.HasValue);
        _lastExpression = "custom";
        return Math.Abs(ctrl.FixedRate - fixedValue) < .001f;
    }

    public bool AdjustFaceValue(string part, float delta)
    {
        var face = CurrentHuman()?.Face;
        if (face == null) return false;
        static float Step(float current, float change) =>
            Math.Clamp(MathF.Round((current + change) * 10f) / 10f, 0f, 1f) + 0f;
        switch (part)
        {
            case "blush":
                var target = Step(_lastBlushValue, delta);
                _lastBlushApplied = face.ChangeHohoAkaRate(new Il2CppSystem.Nullable<float>(target));
                if (_lastBlushApplied) _lastBlushValue = target;
                _lastExpression = "custom";
                return _lastBlushApplied;
            case "eyes_open":
            case "mouth_open":
                var key = part == "eyes_open" ? "eyes" : "mouth";
                ILLGAMES.Unity.FBSBase ctrl = key == "eyes" ? face.eyesCtrl : face.mouthCtrl;
                if (ctrl == null) return false;
                // From "auto", start at what is currently visible so the first step is small.
                var current = ctrl.FixedRate >= 0f ? ctrl.FixedRate : Math.Clamp(ctrl._openRate, 0f, 1f);
                return SetOpenness(key, Step(current, delta));
            case "eyes_auto":
                return SetOpenness("eyes", null);
            case "mouth_auto":
                return SetOpenness("mouth", null);
            default: return false;
        }
    }

    public bool SetFacePart(string part, int index)
    {
        var face = CurrentHuman()?.Face;
        if (face == null || index < 0) return false;
        var limits = FaceParts();
        switch (part)
        {
            case "eyebrow" when index < limits.eyebrowCount:
                face.ChangeEyebrowPtn(index, true);
                if (face.GetEyebrowPtn() != index) return false;
                break;
            case "eyes" when index < limits.eyesCount:
                face.ChangeEyesPtn(index, true);
                if (face.GetEyesPtn() != index) return false;
                break;
            case "mouth" when index < limits.mouthCount:
                face.ChangeMouthPtn(index, true);
                if (face.GetMouthPtn() != index) return false;
                break;
            default:
                return false;
        }
        _lastExpression = "custom";
        _log.LogInfo($"Face part applied: {part}={index}");
        return true;
    }

    public object ExpressionDiagnostics()
    {
        var face = CurrentHuman()?.Face;
        if (face == null) return new { ready = false };
        return new { ready = true, name = _lastExpression,
            eyebrow = face.GetEyebrowPtn(), eyes = face.GetEyesPtn(), mouth = face.GetMouthPtn(),
            eyePatternCount = face.GetEyesPtnNum(),
            eyebrowPatternMax = face.eyebrowCtrl?.GetMaxPtn() ?? -1,
            eyesPatternMax = face.eyesCtrl?.GetMaxPtn() ?? -1,
            mouthPatternMax = face.mouthCtrl?.GetMaxPtn() ?? -1,
            eyesFixed = face.eyesCtrl?.FixedRate, eyesOpenRate = face.eyesCtrl?._openRate,
            mouthFixed = face.mouthCtrl?.FixedRate, mouthOpenRate = face.mouthCtrl?._openRate,
            blink = face.GetEyesBlinkFlag(),
            blushApplied = _lastBlushApplied };
    }

    public bool PlayMotion(string logicalName)
    {
        if (!_config.Motions.TryGetValue(logicalName, out var motionId))
            return false;
        if (_dedicatedHuman?.Invoke() != null)
        {
            var applied = _dedicatedMotion?.Invoke(motionId) == true;
            if (applied) _log.LogDebug($"Dedicated motion '{logicalName}' -> pose index {motionId}");
            return applied;
        }
        var heroine = CurrentHeroine();
        if (heroine?._state == null)
            return false;

        heroine._state.PlayIfDifferent(motionId, false);
        _log.LogDebug($"Motion '{logicalName}' -> {motionId}");
        return true;
    }

    static int Coverage(HumanCloth.Define.ClothesState state) => state switch
    {
        HumanCloth.Define.ClothesState.Clothing => 2,
        HumanCloth.Define.ClothesState.HalfUndress => 1,
        _ => 0
    };

    // Undressing presets only take clothes off: parts the character already removed stay off,
    // so e.g. "bottomless" after "topless" does not put the bra back on.
    static Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> OnlyRemoving(
        HumanCloth cloth, string name, Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> targets) =>
        name == "dressed" ? targets
            : targets.Where(pair => Coverage(pair.Value) <= Coverage(cloth.GetClothesStateType(pair.Key)))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

    public bool SetOutfit(string action)
    {
        LastOutfitPartial = false;
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null) return false;
        if (action.StartsWith("preset:", StringComparison.Ordinal))
        {
            if (!TryResolvePreset(cloth, action[7..], out var resolved, out var skipped)) return false;
            var targets = OnlyRemoving(cloth, action[7..], resolved);
            var previous = targets.ToDictionary(pair => pair.Key, pair => cloth.GetClothesStateType(pair.Key));
            foreach (var (kind, state) in targets)
                cloth.SetClothesState(kind, state, true);
            // A later garment update can change an earlier slot indirectly.
            var applied = 0;
            foreach (var (kind, state) in targets)
                if (cloth.GetClothesStateType(kind) != state)
                {
                    cloth.SetClothesState(kind, previous[kind], true);
                    skipped++;
                }
                else
                {
                    applied++;
                    if (!_originalClothes.ContainsKey(kind)) _originalClothes[kind] = previous[kind];
                }
            LastOutfitPartial = skipped > 0;
            _log.LogInfo($"Outfit preset applied: {action}; parts={applied}, skipped={skipped}");
            return applied > 0;
        }
        var partAction = action.Split(':');
        if (partAction.Length == 2)
        {
            var index = Array.IndexOf(OutfitIntent.PartKeys, partAction[0]);
            if (index < 0) return false;
            var kind = (HumanCloth.Define.ClothesKind)index;
            var state = partAction[1] switch
            {
                "undress" => HumanCloth.Define.ClothesState.Naked,
                "half_undress" => HumanCloth.Define.ClothesState.HalfUndress,
                "dress" => HumanCloth.Define.ClothesState.Clothing,
                _ => (HumanCloth.Define.ClothesState?)null
            };
            if (state == null || !cloth.IsClothesStateKind(kind) || !cloth.IsExist(kind) || !cloth.IsClothesStateType(kind, state.Value))
                return false;
            if (!_originalClothes.ContainsKey(kind))
                _originalClothes[kind] = cloth.GetClothesStateType(kind);
            var previous = cloth.GetClothesStateType(kind);
            cloth.SetClothesState(kind, state.Value, true);
            if (cloth.GetClothesStateType(kind) != state.Value)
            {
                cloth.SetClothesState(kind, previous, true);
                _log.LogWarning($"Part outfit state rejected: {action}");
                return false;
            }
            _log.LogInfo($"Part outfit applied: {action}");
            return true;
        }
        var target = action switch
        {
            "undress" => HumanCloth.Define.ClothesState.Naked,
            "dress" => HumanCloth.Define.ClothesState.Clothing,
            "half_undress" => HumanCloth.Define.ClothesState.HalfUndress,
            _ => (HumanCloth.Define.ClothesState?)null
        };
        if (target == null) return false;
        // Whole-outfit changes must respect each part's supported states. SetClothesStateAll
        // can leave one unsupported accessory unchanged; treating that as total failure also
        // undid every successfully removed garment.
        var kinds = Enum.GetValues(typeof(HumanCloth.Define.ClothesKind))
            .Cast<HumanCloth.Define.ClothesKind>()
            .Where(kind => cloth.IsClothesStateKind(kind) && cloth.IsExist(kind)
                && cloth.IsClothesStateType(kind, target.Value))
            .ToArray();
        if (kinds.Length == 0) return false;
        var previousStates = kinds.ToDictionary(kind => kind, kind => cloth.GetClothesStateType(kind));
        foreach (var kind in kinds)
            cloth.SetClothesState(kind, target.Value, true);
        // Parts that the game refuses (or that a later garment changed back) are skipped, not fatal.
        var appliedCount = 0;
        foreach (var kind in kinds)
        {
            if (cloth.GetClothesStateType(kind) != target.Value)
            {
                cloth.SetClothesState(kind, previousStates[kind], true);
                continue;
            }
            appliedCount++;
            if (!_originalClothes.ContainsKey(kind)) _originalClothes[kind] = previousStates[kind];
        }
        LastOutfitPartial = appliedCount < kinds.Length;
        _log.LogInfo($"Outfit applied: {action}; parts={appliedCount}, skipped={kinds.Length - appliedCount}");
        return appliedCount > 0;
    }

    private static bool TryResolvePreset(HumanCloth cloth, string name,
        out Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> resolved,
        out int skipped)
    {
        resolved = new();
        skipped = 0;
        if (!OutfitPresets.TryGet(name, out var targets)) return false;
        foreach (var (part, target) in targets)
        {
            var kind = (HumanCloth.Define.ClothesKind)Array.IndexOf(OutfitIntent.PartKeys, part);
            if (!cloth.IsClothesStateKind(kind) || !cloth.IsExist(kind)) continue;
            var state = target == "dress" ? HumanCloth.Define.ClothesState.Clothing : HumanCloth.Define.ClothesState.Naked;
            if (!cloth.IsClothesStateType(kind, state))
            {
                skipped++;
                continue;
            }
            resolved[kind] = state;
        }
        return resolved.Count > 0;
    }

    public bool CanHalfUndress()
    {
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null) return false;
        foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            if (cloth.IsClothesStateKind(kind)
                && cloth.IsClothesStateType(kind, HumanCloth.Define.ClothesState.HalfUndress))
                return true;
        return false;
    }

    // Only values that would change at least one worn part right now are offered to the AI;
    // no-op choices (e.g. "show bra" without a top) confuse small models.
    public string[] AvailableOutfitActions()
    {
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null) return Array.Empty<string>();
        bool Changes(HumanCloth.Define.ClothesKind kind, HumanCloth.Define.ClothesState state) =>
            cloth.IsClothesStateKind(kind) && cloth.IsExist(kind) && cloth.IsClothesStateType(kind, state)
            && cloth.GetClothesStateType(kind) != state;
        var kinds = Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)).Cast<HumanCloth.Define.ClothesKind>().ToArray();
        var actions = new List<string>();
        foreach (var name in OutfitPresets.Keys)
            if (TryResolvePreset(cloth, name, out var targets, out _)
                && OnlyRemoving(cloth, name, targets).Any(pair => cloth.GetClothesStateType(pair.Key) != pair.Value))
                actions.Add($"preset:{name}");
        var verbs = new[]
        {
            ("undress", HumanCloth.Define.ClothesState.Naked),
            ("half_undress", HumanCloth.Define.ClothesState.HalfUndress),
            ("dress", HumanCloth.Define.ClothesState.Clothing)
        };
        foreach (var (name, state) in verbs)
            if (kinds.Any(kind => Changes(kind, state))) actions.Add(name);
        for (var i = 0; i < OutfitIntent.PartKeys.Length; i++)
            foreach (var (name, state) in verbs)
                if (Changes((HumanCloth.Define.ClothesKind)i, state))
                    actions.Add($"{OutfitIntent.PartKeys[i]}:{name}");
        return actions.ToArray();
    }

    static readonly Character.List.Define.CategoryNo[] ClothesCategories =
    {
        Character.List.Define.CategoryNo.co_top, Character.List.Define.CategoryNo.co_bot,
        Character.List.Define.CategoryNo.co_bra, Character.List.Define.CategoryNo.co_shorts,
        Character.List.Define.CategoryNo.co_gloves, Character.List.Define.CategoryNo.co_panst,
        Character.List.Define.CategoryNo.co_socks, Character.List.Define.CategoryNo.co_shoes
    };

    // The garment's display name (e.g. フリルビキニ) tells the AI what a slot actually holds.
    public string ClothesName(HumanCloth.Define.ClothesKind kind)
    {
        var human = _dedicatedHuman?.Invoke();
        var index = (int)kind;
        if (human == null || index >= ClothesCategories.Length) return "";
        try
        {
            var parts = human.Coorde.Now.Clothes.parts;
            if (index >= parts.Length) return "";
            var table = Human.LstCtrl.GetCategoryInfo(ClothesCategories[index]);
            return table != null && table.TryGetValue(parts[index].id, out var info) ? info?.Name ?? "" : "";
        }
        catch { return ""; }
    }

    public Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> CaptureOutfitStates()
    {
        var result = new Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState>();
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null) return result;
        foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            if (cloth.IsClothesStateKind(kind) && cloth.IsExist(kind)) result[kind] = cloth.GetClothesStateType(kind);
        return result;
    }

    // Re-applies per-part states after a coordinate change; parts the new outfit lacks or
    // cannot put into that state are skipped.
    public int ApplyOutfitStates(Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState> states)
    {
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null) return 0;
        var applied = 0;
        foreach (var (kind, state) in states)
        {
            if (!cloth.IsClothesStateKind(kind) || !cloth.IsExist(kind) || !cloth.IsClothesStateType(kind, state)) continue;
            var current = cloth.GetClothesStateType(kind);
            if (current == state) continue;
            if (!_originalClothes.ContainsKey(kind)) _originalClothes[kind] = current;
            cloth.SetClothesState(kind, state, true);
            if (cloth.GetClothesStateType(kind) == state) applied++;
        }
        _log.LogInfo($"Outfit states carried over after coordinate change: applied={applied}");
        return applied;
    }

    public void RestoreOutfit()
    {
        var cloth = _dedicatedHuman?.Invoke()?.Cloth;
        if (cloth == null || _originalClothes.Count == 0) return;
        foreach (var (kind, state) in _originalClothes)
            cloth.SetClothesState(kind, state, true);
        _originalClothes.Clear();
        _log.LogInfo("Original outfit restored on stage exit or character switch.");
    }

    public object OutfitDiagnostics()
    {
        var human = _dedicatedHuman?.Invoke();
        var cloth = human?.Cloth;
        if (cloth == null) return new { ready = false };
        var states = new Dictionary<string, string>();
        var halfUndressParts = new List<string>();
        foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            if (cloth.IsClothesStateKind(kind))
            {
                states[kind.ToString()] = cloth.GetClothesStateType(kind).ToString();
                if (cloth.IsClothesStateType(kind, HumanCloth.Define.ClothesState.HalfUndress))
                    halfUndressParts.Add(kind.ToString());
            }
        var renderers = human.GameObject.GetComponentsInChildren<Renderer>(true);
        var activeRenderers = 0;
        for (var i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
                activeRenderers++;
        return new { ready = true, modified = _originalClothes.Count > 0, states, halfUndressParts, availableActions = AvailableOutfitActions(),
            activeRenderers, totalRenderers = renderers.Length };
    }

    public string Status()
    {
        var dedicated = _dedicatedHuman?.Invoke();
        if (dedicated != null)
            return $"character: dedicated / {dedicated.GameObject?.name}";
        var heroine = CurrentHeroine();
        return heroine == null
            ? "character: not ready"
            : $"character: ready / state={heroine.CurrentStateID}";
    }
}
