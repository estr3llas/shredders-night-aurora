using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace ShreddersNightAurora
{
    /// <summary>
    /// Second half of the night look, found by frozen-frame A/B after the fog fix:
    ///  - Specular reflections are the "foam": the reflection probes hold a bright night sky (Tropos sky exposure
    ///    is 12), so every glossy surface (snow, rails, board) gets a pale sheen. Scaled by NightReflections.
    ///  - Auto exposure lifts the dark scene back towards mid-grey and bloom then halos the snow; both are
    ///    switched off at night so it can actually be dark.
    ///  - A small grade: more contrast, less saturation (night vision is less colourful).
    /// The global post-process profile is a shared asset, so every original value is cached and put back.
    /// </summary>
    internal static class NightGrade
    {
        const float ContrastAdd = 15f, SaturationAdd = -15f;

        static PostProcessProfile profile;
        static bool bloomWas, exposureWas;
        static float contrastWas, saturationWas, exposureOffsetWas;
        static bool contrastOverrideWas, saturationOverrideWas, exposureOffsetOverrideWas;

        static float reflectionWas = -1f;
        static readonly List<(ReflectionProbe probe, float intensity)> probes = new List<(ReflectionProbe, float)>();

        static float ReflectionScale => Mathf.Clamp(Mod.NightReflections.Value, 0f, 1f);

        public static void Apply()
        {
            ApplyReflections();
            if (Mod.NightGradeEnabled.Value) ApplyPost(); else RestorePost();
        }

        public static void Restore()
        {
            RestoreReflections();
            RestorePost();
        }

        static void ApplyReflections()
        {
            if (reflectionWas < 0f)
            {
                reflectionWas = RenderSettings.reflectionIntensity;
                probes.Clear();
                foreach (var p in Object.FindObjectsOfType<ReflectionProbe>()) probes.Add((p, p.intensity));
            }
            float k = ReflectionScale;
            if (!Mathf.Approximately(RenderSettings.reflectionIntensity, reflectionWas * k))
                RenderSettings.reflectionIntensity = reflectionWas * k;
            foreach (var (probe, intensity) in probes)
                if (probe != null && !Mathf.Approximately(probe.intensity, intensity * k)) probe.intensity = intensity * k;
        }

        static void RestoreReflections()
        {
            if (reflectionWas < 0f) return;
            RenderSettings.reflectionIntensity = reflectionWas;
            foreach (var (probe, intensity) in probes)
                if (probe != null) probe.intensity = intensity;
            probes.Clear();
            reflectionWas = -1f;
        }

        /// <summary>Called when a level unloads: its reflection probes go with it. The post-process profile is a
        /// project asset that outlives the level, so its cached originals are kept for the restore.</summary>
        public static void Forget()
        {
            probes.Clear();
            reflectionWas = -1f;
        }

        static void ApplyPost()
        {
            var current = profile != null ? profile : GlobalProfile();
            if (current == null) return;
            if (current != profile)
            {
                RestorePost();
                profile = current;
                var b = Get<Bloom>(profile); var a = Get<AutoExposure>(profile); var c = Get<ColorGrading>(profile);
                bloomWas = b != null && b.active;
                exposureWas = a != null && a.active;
                if (c != null)
                {
                    Il2CppParam.TryGetFloat(c.contrast, "value", out contrastWas);
                    Il2CppParam.TryGetBool(c.contrast, "overrideState", out contrastOverrideWas);
                    Il2CppParam.TryGetFloat(c.saturation, "value", out saturationWas);
                    Il2CppParam.TryGetBool(c.saturation, "overrideState", out saturationOverrideWas);
                    Il2CppParam.TryGetFloat(c.postExposure, "value", out exposureOffsetWas);
                    Il2CppParam.TryGetBool(c.postExposure, "overrideState", out exposureOffsetOverrideWas);
                }
            }
            var bloom = Get<Bloom>(profile); if (bloom != null && bloom.active) bloom.active = false;
            var ae = Get<AutoExposure>(profile); if (ae != null && ae.active) ae.active = false;
            var cg = Get<ColorGrading>(profile);
            if (cg != null)
            {
                SetParam(cg.contrast, Mathf.Clamp(contrastWas + ContrastAdd, -100f, 100f));
                SetParam(cg.saturation, Mathf.Clamp(saturationWas + SaturationAdd, -100f, 100f));
                // With auto exposure off, this is the night's overall brightness (EV). Raising it does not bring the
                // glow back: that came from reflections, fog and bloom, which stay down.
                SetParam(cg.postExposure, exposureOffsetWas + Mathf.Clamp(Mod.NightBrightness.Value, -3f, 3f));
            }
        }

        static void RestorePost()
        {
            if (profile == null) return;
            var b = Get<Bloom>(profile); if (b != null) b.active = bloomWas;
            var a = Get<AutoExposure>(profile); if (a != null) a.active = exposureWas;
            var c = Get<ColorGrading>(profile);
            if (c != null)
            {
                Il2CppParam.TrySetFloat(c.contrast, "value", contrastWas);
                Il2CppParam.TrySetBool(c.contrast, "overrideState", contrastOverrideWas);
                Il2CppParam.TrySetFloat(c.saturation, "value", saturationWas);
                Il2CppParam.TrySetBool(c.saturation, "overrideState", saturationOverrideWas);
                Il2CppParam.TrySetFloat(c.postExposure, "value", exposureOffsetWas);
                Il2CppParam.TrySetBool(c.postExposure, "overrideState", exposureOffsetOverrideWas);
            }
            profile = null;
        }

        static void SetParam(ParameterOverride p, float value)
        {
            if (Il2CppParam.TryGetFloat(p, "value", out float v) && Mathf.Approximately(v, value)) return;
            Il2CppParam.TrySetFloat(p, "value", value);
            Il2CppParam.TrySetBool(p, "overrideState", true);
        }

        static PostProcessProfile GlobalProfile()
        {
            foreach (var v in Object.FindObjectsOfType<PostProcessVolume>())
                if (v.isGlobal && v.isActiveAndEnabled && v.sharedProfile != null) return v.sharedProfile;
            return null;
        }

        static T Get<T>(PostProcessProfile p) where T : PostProcessEffectSettings
        {
            foreach (var s in p.settings)
            {
                var t = s.TryCast<T>();
                if (t != null) return t;
            }
            return null;
        }
    }
}
