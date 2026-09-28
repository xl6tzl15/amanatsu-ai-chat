using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace Amanatsu.AiChat.GameAdapter;

// Recolors only the dedicated ADV message-frame sprite. The game's sprite asset is never modified.
internal sealed class HeroineDialogueFrame : IDisposable
{
    private Image _image;
    private Sprite _originalSprite;
    private Sprite _originalOverrideSprite;
    private Color _originalColor;
    private Sprite _pinkSprite;
    private Texture2D _pinkTexture;

    public bool Apply(Image image, ManualLogSource log)
    {
        if (image == null) { log.LogWarning("ADV message-frame image is unavailable; pink frame was not applied."); return false; }
        Restore();
        var source = image.overrideSprite ?? image.sprite;
        if (source == null || source.texture == null)
        {
            log.LogWarning("ADV message-frame sprite is unavailable; pink frame was not applied.");
            return false;
        }

        Texture2D texture = null;
        Sprite sprite = null;
        RenderTexture temporary = null;
        var previousActive = RenderTexture.active;
        try
        {
            var rect = source.textureRect;
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            if (width < 1 || height < 1) throw new InvalidDataException("ADV frame sprite has no pixels.");
            temporary = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source.texture, temporary);
            RenderTexture.active = temporary;
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
            texture.Apply();

            var pixels = texture.GetPixels();
            var changed = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a < 0.01f) continue;
                Color.RGBToHSV(pixel, out var hue, out var saturation, out var value);
                if (saturation < 0.08f || hue < 0.42f || hue > 0.73f) continue;
                var pink = Color.HSVToRGB(0.93f, Mathf.Clamp(saturation * 0.65f, 0.32f, 0.7f),
                    Mathf.Clamp01(value * 1.15f + 0.13f));
                pink.a = pixel.a;
                pixels[i] = pink;
                changed++;
            }
            if (changed == 0) throw new InvalidDataException("ADV frame sprite has no blue pixels to recolor.");
            texture.SetPixels(pixels);
            texture.Apply();
            var pivot = new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height);
            sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot,
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);

            _image = image;
            _originalSprite = image.sprite;
            _originalOverrideSprite = image.overrideSprite;
            _originalColor = image.color;
            _pinkTexture = texture;
            _pinkSprite = sprite;
            image.sprite = sprite;
            image.overrideSprite = sprite;
            image.color = new Color(1f, 1f, 1f, _originalColor.a);
            log.LogInfo($"Pink heroine ADV frame applied: pixels={changed}, size={width}x{height}.");
            return true;
        }
        catch (Exception ex)
        {
            if (_image != null)
            {
                Restore();
                sprite = null;
                texture = null;
            }
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            log.LogWarning($"Pink heroine ADV frame could not be applied: {ex}");
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
        }
    }

    public void Restore()
    {
        if (_image != null)
        {
            _image.sprite = _originalSprite;
            _image.overrideSprite = _originalOverrideSprite;
            _image.color = _originalColor;
        }
        if (_pinkSprite != null) UnityEngine.Object.Destroy(_pinkSprite);
        if (_pinkTexture != null) UnityEngine.Object.Destroy(_pinkTexture);
        _image = null;
        _pinkSprite = null;
        _pinkTexture = null;
        _originalSprite = null;
        _originalOverrideSprite = null;
    }

    public void Dispose() => Restore();
}
