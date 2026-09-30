using BepInEx.Logging;
using Character;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Amanatsu.AiChat.GameAdapter;

// Starts the game's own H scene from the chat stage and comes back when it ends.
// The main game normally reaches AL.H.HScene.InitializeAsync from EventManager.HDetail on a
// loaded map; at the title screen no map is loaded, so the resident MapManager first loads one
// with H points. Polled from Update; every step runs on the main thread.
internal sealed class HTransition
{
    // Map IDs that have H points (h/list/hpoint/object): pool, beach, water cottage,
    // shopping area, waterfall, fountain square, esthetics, rock shade, hotel.
    public static readonly int[] Maps = { 0, 1, 2, 3, 4, 5, 10, 11, 12 };

    public static string MapName(int id) => id switch
    {
        0 => L.T("プール", "Pool"), 1 => L.T("ビーチ", "Beach"), 2 => L.T("水上コテージ", "Water cottage"),
        3 => L.T("ショッピングエリア", "Shopping area"), 4 => L.T("滝", "Waterfall"), 5 => L.T("噴水広場", "Fountain square"),
        10 => L.T("エステ", "Esthetics"), 11 => L.T("岩陰", "Rock shade"), 12 => L.T("ホテル", "Hotel"),
        _ => id.ToString()
    };

    private enum Phase { Idle, MapInit, MapLoad, Humans, Running, Release, Done }

    private readonly ManualLogSource _log;
    private Phase _phase = Phase.Idle;
    private Manager.MapManager _map;
    private UniTask _pending;
    private UniTask<AL.H.ResultInfo> _scene;
    private Human _female;
    private Human _male;
    private HumanData _femaleData;
    private int _mapId;
    private int _scenesBefore;
    private AL.H.PlaceType _place;
    private AL.H.Define.PostureCategory? _category;
    private AL.H.Parameter.HState _state;
    private float _deadline;
    private Action _onFinished;

    public HTransition(ManualLogSource log) { _log = log; }

    public bool Active => _phase is not (Phase.Idle or Phase.Done);
    public string Status { get; private set; } = "idle";
    public string Error { get; private set; }

    public void Start(HumanData female, int mapId, AL.H.PlaceType place, AL.H.Define.PostureCategory? category,
        bool lewd, Action onFinished)
    {
        if (Active) throw new InvalidOperationException("H transition already running");
        if (Array.IndexOf(Maps, mapId) < 0) throw new ArgumentException($"map {mapId} has no H points");
        _femaleData = female ?? throw new ArgumentNullException(nameof(female));
        _mapId = mapId;
        _place = place;
        _category = category;
        _state = lewd ? AL.H.Parameter.HState.Lewdness : AL.H.Parameter.HState.Normal;
        _onFinished = onFinished;
        Error = null;
        _map = null;
        Enter(Phase.MapInit, 30f);
    }

    public void Update()
    {
        if (!Active) return;
        try
        {
            switch (_phase)
            {
                case Phase.MapInit:
                    // The MapManager lives from startup; ChangeMapAsync silently does nothing until its map list is read.
                    _map = Manager.MapManager.Initialized ? Manager.MapManager.Instance : null;
                    var table = _map?._mapListTable;
                    if (table == null || !table.ContainsKey(_mapId)) { Timeout(); return; }
                    _scenesBefore = UnityEngine.SceneManagement.SceneManager.sceneCount;
                    _pending = _map.ChangeMapAsync(_mapId, 0, true, null, true, false);
                    Enter(Phase.MapLoad, 60f);
                    break;
                case Phase.MapLoad:
                    if (!Completed(_pending)) { Timeout(); return; }
                    if (_map._mapID != _mapId) throw new InvalidOperationException($"map {_mapId} did not load (current {_map._mapID})");
                    _log.LogInfo($"H map loaded: id={_mapId}, scenes {_scenesBefore} -> {UnityEngine.SceneManagement.SceneManager.sceneCount}");
                    Enter(Phase.Humans, 10f);
                    break;
                case Phase.Humans:
                    _male = Human.Create(MaleData());
                    _female = Human.Create(_femaleData);
                    var humans = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Human>(3);
                    humans[0] = _male;
                    humans[1] = _female;
                    var category = _category.HasValue
                        ? new Il2CppSystem.Nullable<AL.H.Define.PostureCategory>(_category.Value)
                        : new Il2CppSystem.Nullable<AL.H.Define.PostureCategory>();
                    var parameter = new AL.H.Parameter(_state, AL.H.Parameter.HPattern.Normal, category, _place, null, humans);
                    _scene = AL.H.HScene.InitializeAsync(parameter, new Il2CppSystem.Threading.CancellationToken());
                    _log.LogInfo($"H scene requested: place={_place}, category={_category?.ToString() ?? "any"}, state={_state}");
                    Enter(Phase.Running, float.PositiveInfinity);
                    break;
                case Phase.Running:
                    if (!_scene.GetAwaiter().IsCompleted) return;
                    _log.LogInfo("H scene ended.");
                    Release();
                    break;
                case Phase.Release:
                    // The release task unloads the map scene and then keeps waiting on asset cleanup
                    // without reporting completion at the title; the map scene being gone is enough.
                    if (!Completed(_pending) && UnityEngine.SceneManagement.SceneManager.sceneCount > _scenesBefore)
                    {
                        if (Time.realtimeSinceStartup <= _deadline) return;
                        _log.LogWarning("H map release did not finish; returning to the chat anyway.");
                    }
                    Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _log.LogError($"H transition failed in {_phase}: {ex}");
            try { Release(); Finish(); } catch (Exception cleanup) { _log.LogError($"H cleanup failed: {cleanup}"); _phase = Phase.Done; }
        }
    }

    private static bool Completed(UniTask task)
    {
        var status = task.Status;
        if (status == UniTaskStatus.Pending) return false;
        if (status != UniTaskStatus.Succeeded) throw new InvalidOperationException($"game task ended as {status}");
        return true;
    }

    private void Enter(Phase phase, float seconds)
    {
        _phase = phase;
        Status = phase.ToString();
        _deadline = Time.realtimeSinceStartup + seconds;
    }

    private void Timeout()
    {
        if (Time.realtimeSinceStartup > _deadline) throw new TimeoutException($"{_phase} did not finish");
    }

    // The player's own card if there is one, otherwise the game's default male.
    // Male card path from the H settings; empty for the game's default male.
    public string MaleCard { get; set; }

    private HumanData MaleData()
    {
        var data = new HumanData((byte)0);
        var card = string.IsNullOrWhiteSpace(MaleCard) || !File.Exists(MaleCard) ? null : MaleCard;
        _log.LogInfo($"H male: {card ?? "default"}");
        if (card != null && data.LoadCharaFile(card)) return data;
        // The game's built-in male preset (an empty HumanData has no proper male look).
        data = new HumanData((byte)0);
        if (!data.LoadFromPreset(0)) _log.LogWarning("Built-in male preset could not be loaded.");
        return data;
    }

    private void Release()
    {
        foreach (var human in new[] { _female, _male })
            if (human != null) try { human.Dispose(); } catch { }
        _female = _male = null;
        _pending = _map != null ? _map.ReleaseMapObject() : UniTask.CompletedTask;
        Enter(Phase.Release, 30f);
    }

    private void Finish()
    {
        // H ends by covering the screen (Fade.In); the main game uncovers it after restoring the map.
        try { Manager.Scene.StartFade(FadeCanvas.Fade.Out, false); }
        catch (Exception ex) { _log.LogWarning($"Scene fade could not be cleared: {ex.Message}"); }
        var scenes = new List<string>();
        for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            scenes.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
        _log.LogInfo($"H finished; loaded scenes: {string.Join(", ", scenes)}");
        _map = null;
        _phase = Phase.Done;
        Status = Error == null ? "done" : "failed";
        _onFinished?.Invoke();
    }
}
