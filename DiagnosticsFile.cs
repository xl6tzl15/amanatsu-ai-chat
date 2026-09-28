using System.Text.Json;
using BepInEx.Logging;
using UnityEngine;

namespace Amanatsu.AiChat;

// Explicitly enabled local test interface; all Unity operations remain on the main thread.
internal sealed class DiagnosticsFile
{
    private readonly string _directory = Path.GetFullPath("BepInEx/config/amanatsu.ai-chat/diagnostics");
    private readonly ManualLogSource _log;
    private DateTime _lastWrite;
    private float _next;
    private string _lastId = "";
    private string _error;
    private bool _failed;

    public DiagnosticsFile(ManualLogSource log)
    {
        _log = log;
        Directory.CreateDirectory(_directory);
        // Never replay a command left behind by an earlier test run.
        _lastWrite = File.GetLastWriteTimeUtc(Path.Combine(_directory, "command.json"));
    }

    public void Tick(Action<string, string> execute, Func<object> state)
    {
        if (_failed || Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 0.5f;
        try
        {
            var path = Path.Combine(_directory, "command.json");
            var stamp = File.GetLastWriteTimeUtc(path);
            if (File.Exists(path) && stamp != _lastWrite)
            {
                _lastWrite = stamp;
                try
                {
                    if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Test command too large");
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var root = doc.RootElement;
                    var id = root.GetProperty("id").GetString();
                    if (!string.IsNullOrWhiteSpace(id) && id != _lastId)
                    {
                        _lastId = id;
                        _error = null;
                        execute(root.GetProperty("action").GetString(), root.TryGetProperty("value", out var value) ? value.GetString() : "");
                    }
                }
                catch (Exception ex) { _error = ex.Message; _log.LogWarning($"Diagnostic command: {ex.Message}"); }
            }
            var snapshot = new { utc = DateTime.UtcNow, commandId = _lastId, error = _error, state = state() };
            var temp = Path.Combine(_directory, "state.tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, Path.Combine(_directory, "state.json"), true);
        }
        catch (IOException)
        {
            // A concurrent reader can briefly hold state.json open on Windows.
            // Retry on the next tick instead of disabling diagnostics.
        }
        catch (UnauthorizedAccessException)
        {
            // File replacement can also report a sharing race as access denied.
        }
        catch (Exception ex)
        {
            _failed = true;
            _log.LogWarning($"Diagnostic readback disabled after failure: {ex.Message}");
        }
    }
}
