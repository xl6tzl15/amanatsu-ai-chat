using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace Amanatsu.AiChat.GameAdapter;

// An unlit camera-facing quad behind the character. The game's own sample
// backgrounds are read from DefaultData, never copied into the Mod package.
internal sealed class BackgroundBackdrop : IDisposable
{
    private GameObject _quad;
    private Texture2D _texture;
    private Material _material;
    private Camera _camera;

    public string CurrentSpec { get; private set; } = "none";
    public bool Visible => _quad != null && _quad.activeInHierarchy;

    private static string[] GameImages()
    {
        var directory = Path.GetFullPath("DefaultData/common/bg");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "al_mapsample*.png*").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
    }

    public static string Next(string spec, int direction)
    {
        var count = GameImages().Length;
        if (count == 0) return "none";
        var current = int.TryParse(spec?.Replace("game:", ""), out var index) &&
            spec?.StartsWith("game:", StringComparison.OrdinalIgnoreCase) == true && index >= 0 && index < count
            ? index + 1 : 0;
        var next = (current + (direction >= 0 ? 1 : -1) + count + 1) % (count + 1);
        return next == 0 ? "none" : $"game:{next - 1}";
    }

    public static string Label(string spec)
    {
        if (string.Equals(spec, "none", StringComparison.OrdinalIgnoreCase)) return L.T("背景: 単色", "Background: plain");
        var files = GameImages();
        if (spec?.StartsWith("game:", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(spec[5..], out var index) && index >= 0 && index < files.Length)
            return L.T($"背景: ゲーム {index + 1}/{files.Length}", $"Background: game {index + 1}/{files.Length}");
        return L.T("背景: ", "Background: ") + Path.GetFileName(spec);
    }

    public static string Resolve(string spec)
    {
        if (string.Equals(spec, "none", StringComparison.OrdinalIgnoreCase)) return null;
        var files = GameImages();
        if (spec?.StartsWith("game:", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!int.TryParse(spec[5..], out var index) || index < 0 || index >= files.Length)
                throw new InvalidDataException(L.T("指定したゲーム背景がありません。", "That game background does not exist."));
            return files[index];
        }
        if (string.IsNullOrWhiteSpace(spec)) throw new InvalidDataException(L.T("背景画像を指定してください。", "Choose a background image."));
        var path = Path.GetFullPath(spec);
        if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("背景画像はPNG/JPEGにしてください。", "The background image must be PNG or JPEG."));
        return path;
    }

    public void Apply(Camera camera, string spec)
    {
        if (camera == null) throw new InvalidOperationException(L.T("背景用カメラがありません。", "There is no camera for the background."));
        var path = Resolve(spec);
        if (path == null)
        {
            Dispose();
            CurrentSpec = "none";
            return;
        }
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 32 * 1024 * 1024)
            throw new InvalidDataException(L.T("背景画像がないか、32MBを超えています。", "The background image is missing or larger than 32 MB."));
        Texture2D texture = null;
        Material material = null;
        GameObject quad = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(File.ReadAllBytes(path)), true))
                throw new InvalidDataException(L.T("背景画像を読み込めませんでした。", "Could not read the background image."));
            if (texture.width > 8192 || texture.height > 8192)
                throw new InvalidDataException(L.T("背景画像は8192ピクセル以下にしてください。", "The background image must be at most 8192 pixels per side."));
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException(L.T("背景描画用シェーダーがありません。", "The background shader is missing."));
            material = new Material(shader);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)CullMode.Off);
            if (material.HasProperty("_CullMode")) material.SetInt("_CullMode", (int)CullMode.Off);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "AiConversationBackground";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            quad.transform.SetParent(camera.transform, false);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        catch
        {
            if (quad != null) UnityEngine.Object.Destroy(quad);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            throw;
        }
        Dispose();
        _camera = camera;
        _texture = texture;
        _material = material;
        _quad = quad;
        CurrentSpec = spec;
        RefreshFrame();
    }

    public void RefreshFrame()
    {
        if (_camera == null || _quad == null || _texture == null) return;
        const float distance = 100f;
        _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, 200f);
        _quad.transform.localPosition = new Vector3(0f, 0f, distance);
        var height = 2f * distance * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * .5f);
        var aspect = Mathf.Max(.1f, _camera.aspect);
        _quad.transform.localScale = new Vector3(height * aspect, height, 1f);
        var imageAspect = (float)_texture.width / _texture.height;
        var scale = imageAspect > aspect
            ? new Vector2(aspect / imageAspect, 1f)
            : new Vector2(1f, imageAspect / aspect);
        var offset = (Vector2.one - scale) * .5f;
        _material.mainTextureScale = scale;
        _material.mainTextureOffset = offset;
        if (_material.HasProperty("_BaseMap"))
        {
            _material.SetTextureScale("_BaseMap", scale);
            _material.SetTextureOffset("_BaseMap", offset);
        }
    }

    public void Dispose()
    {
        if (_quad != null) UnityEngine.Object.Destroy(_quad);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        _quad = null; _material = null; _texture = null; _camera = null;
        CurrentSpec = "none";
    }
}
