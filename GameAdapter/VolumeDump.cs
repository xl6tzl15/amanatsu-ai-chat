using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Amanatsu.AiChat.GameAdapter;

internal static class VolumeDump
{
    public static void Write(string path)
    {
        var volumes = UnityEngine.Object.FindObjectsOfType<Volume>(true).Select(v => new
        {
            name = v.name, path = PathOf(v.transform), scene = v.gameObject.scene.name, layer = v.gameObject.layer,
            active = v.isActiveAndEnabled, global = v.isGlobal, priority = v.priority, weight = v.weight,
            profile = v.sharedProfile?.name,
            siblings = v.GetComponents<Component>().Select(c => c.GetIl2CppType().FullName).ToArray(),
            components = Components(v.sharedProfile)
        }).ToArray();
        var effectors = UnityEngine.Object.FindObjectsOfType<AL.Config.ConfigEffectorVolume>(true).Select(e => new
        {
            path = PathOf(e.transform), active = e.isActiveAndEnabled,
            volumes = e.Volumes == null ? null : Enumerable.Range(0, e.Volumes.Count).Select(i => e.Volumes[i]?.name).ToArray()
        }).ToArray();
        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true).Select(c =>
        {
            var x = c.GetComponent<UniversalAdditionalCameraData>();
            return new { name = c.name, path = PathOf(c.transform), active = c.isActiveAndEnabled, depth = c.depth,
                mask = c.cullingMask, fov = c.fieldOfView, near = c.nearClipPlane, far = c.farClipPlane,
                components = c.GetComponents<Component>().Select(x => x.GetIl2CppType().FullName).ToArray(), renderType = x?.renderType.ToString(), post = x?.renderPostProcessing,
                volumeMask = x?.volumeLayerMask.value, trigger = x?.volumeTrigger?.name, aa = x?.antialiasing.ToString(),
                stack = x == null || x.renderType != CameraRenderType.Base || x.cameraStack == null ? null : Enumerable.Range(0, x.cameraStack.Count).Select(i => x.cameraStack[i]?.name).ToArray() };
        }).ToArray();
        var lights = UnityEngine.Object.FindObjectsOfType<Light>(true).Select(l => new { path = PathOf(l.transform),
            active = l.isActiveAndEnabled, type = l.type.ToString(), intensity = l.intensity, color = l.color.ToString(),
            shadows = l.shadows.ToString(), strength = l.shadowStrength, mask = l.cullingMask,
            rotation = l.transform.eulerAngles.ToString(), sun = RenderSettings.sun == l }).ToArray();
        var humans = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>(true).Where(r => r.name.Contains("body", StringComparison.OrdinalIgnoreCase))
            .Take(6).Select(r => new { path = PathOf(r.transform), layer = r.gameObject.layer, cast = r.shadowCastingMode.ToString(), receive = r.receiveShadows }).ToArray();
        var g = Manager.Config.GraphicData;
        var graphics = g == null ? null : new { g.Bloom, g.DepthOfField, g.Vignette, g.SSAO, g.Fog, g.Map, g.Shield, g.Quality, aa = g.Antialiasing };
        var ambient = new { mode = RenderSettings.ambientMode.ToString(), sky = RenderSettings.ambientSkyColor.ToString(),
            light = RenderSettings.ambientLight.ToString(), intensity = RenderSettings.ambientIntensity,
            fog = RenderSettings.fog, skybox = RenderSettings.skybox?.name };
        File.WriteAllText(path, JsonSerializer.Serialize(new { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            graphics, ambient, humans, volumes, effectors, cameras, lights }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Components(VolumeProfile profile)
    {
        if (profile == null) return null;
        var list = new List<object>();
        for (var i = 0; i < profile.components.Count; i++)
        {
            var c = profile.components[i];
            if (c == null) continue;
            var type = c.GetIl2CppType().FullName;
            var b = c.TryCast<Beautify.Universal.Beautify>();
            if (b != null)
            {
                list.Add(new { type, active = c.active,
                    bloomIntensity = P(b.bloomIntensity), bloomThreshold = P(b.bloomThreshold),
                    depthOfField = P(b.depthOfField), dofDistance = P(b.depthOfFieldDistance), dofFocusMode = P(b.depthOfFieldFocusMode),
                    dofAperture = P(b.depthOfFieldAperture), dofFocalLength = P(b.depthOfFieldFocalLength),
                    vignetteOuter = P(b.vignettingOuterRing), vignetteInner = P(b.vignettingInnerRing), vignetteFade = P(b.vignettingFade),
                    tonemap = P(b.tonemap), exposure = P(b.tonemapExposurePre), brightnessPost = P(b.tonemapBrightnessPost),
                    saturate = P(b.saturate), brightness = P(b.brightness), contrast = P(b.contrast),
                    sharpen = P(b.sharpenIntensity), lut = P(b.lut), lutIntensity = P(b.lutIntensity), lutTexture = b.lutTexture?.value?.name,
                    anamorphic = P(b.anamorphicFlaresIntensity), disabled = P(b.disabled) });
                continue;
            }
            var bloom = c.TryCast<UnityEngine.Rendering.Universal.Bloom>();
            if (bloom != null)
            {
                list.Add(new { type, active = c.active, threshold = P(bloom.threshold), intensity = P(bloom.intensity),
                    scatter = P(bloom.scatter), clamp = P(bloom.clamp), tint = P(bloom.tint), hq = P(bloom.highQualityFiltering),
                    dirt = P(bloom.dirtIntensity) });
                continue;
            }
            var smh = c.TryCast<ShadowsMidtonesHighlights>();
            if (smh != null)
            {
                list.Add(new { type, active = c.active, shadows = P(smh.shadows), midtones = P(smh.midtones), highlights = P(smh.highlights),
                    shadowsStart = P(smh.shadowsStart), shadowsEnd = P(smh.shadowsEnd), highlightsStart = P(smh.highlightsStart), highlightsEnd = P(smh.highlightsEnd) });
                continue;
            }
            list.Add(new { type, active = c.active, parameters = c.parameters?.Count });
        }
        return list;
    }

    private static string P<T>(VolumeParameter<T> p) => p == null ? null : $"{p.value}{(p.overrideState ? "" : " (no override)")}";

    private static string PathOf(Transform t)
    {
        var names = new List<string>();
        for (var i = 0; t != null && i < 8; i++, t = t.parent) names.Insert(0, t.name);
        return string.Join("/", names);
    }
}
