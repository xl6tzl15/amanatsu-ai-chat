using Amanatsu.AiChat.GameAdapter;
using BepInEx.Logging;
using UnityEngine;

namespace Amanatsu.AiChat.Sequence;

internal sealed class SequenceRunner
{
    private readonly Queue<SequenceCommand> _queue = new();
    private readonly CharacterAdapter _character;
    private readonly ManualLogSource _log;
    private readonly Action<string> _showText;
    private float _resumeAt;
    private bool _wasRunning;
    private bool _activeMotion;
    private float _lastMotionAt;
    private bool _paused;
    private float _pausedAt;

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        if (paused) _pausedAt = Time.realtimeSinceStartup;
        else { var elapsed = Time.realtimeSinceStartup - _pausedAt; if (_resumeAt > 0) _resumeAt += elapsed; _lastMotionAt += elapsed; }
        _paused = paused;
    }

    public SequenceRunner(CharacterAdapter character, Action<string> showText, ManualLogSource log)
    {
        _character = character;
        _showText = showText;
        _log = log;
    }

    public bool IsRunning => _queue.Count > 0 || _resumeAt > 0;

    public void Start(IEnumerable<SequenceCommand> commands)
    {
        Cancel();
        foreach (var command in commands)
            _queue.Enqueue(command);
        _wasRunning = _queue.Count > 0;
    }

    public void Cancel()
    {
        _queue.Clear();
        _resumeAt = 0;
        _wasRunning = false;
        if (_activeMotion) _character.PlayMotion("idle");
        _activeMotion = false;
    }

    public void Update()
    {
        if (_paused) return;
        if (_resumeAt > 0)
        {
            if (Time.realtimeSinceStartup < _resumeAt)
                return;
            _resumeAt = 0;
        }

        for (var budget = 0; budget < 12 && _queue.Count > 0; budget++)
        {
            var command = _queue.Dequeue();
            switch (command.Type)
            {
                case "expression":
                    if (!_character.SetExpression(command.Value!))
                        _log.LogWarning($"Expression could not be applied: {command.Value}");
                    break;
                case "eyebrow":
                case "eyes":
                case "mouth":
                    if (!_character.SetFacePart(command.Type, int.Parse(command.Value!)))
                        _log.LogWarning($"Face part could not be applied: {command.Type}={command.Value}");
                    break;
                case "outfit":
                    if (!_character.SetOutfit(command.Value!))
                        _log.LogWarning($"Outfit could not be applied: {command.Value}");
                    break;
                case "pose":
                    if (!_character.SetPose(command.Value!))
                        _log.LogWarning($"Pose could not be applied: {command.Value}");
                    break;
                case "motion":
                    if (!_character.PlayMotion(command.Value!))
                        _log.LogWarning($"Motion could not be applied: {command.Value}");
                    else
                    {
                        _activeMotion = !string.Equals(command.Value, "idle", StringComparison.OrdinalIgnoreCase);
                        if (_activeMotion) _lastMotionAt = Time.realtimeSinceStartup;
                    }
                    break;
                case "text":
                    _showText(command.Value!);
                    break;
                case "wait":
                    _resumeAt = Time.realtimeSinceStartup + command.Duration!.Value;
                    return;
            }
        }

        if (_wasRunning && !IsRunning)
        {
            if (_activeMotion && Time.realtimeSinceStartup < _lastMotionAt + 1.5f)
            {
                _resumeAt = _lastMotionAt + 1.5f;
                return;
            }
            if (_activeMotion) _character.PlayMotion("idle");
            _activeMotion = false;
            _wasRunning = false;
            _log.LogInfo("Sequence completed.");
        }
    }
}
