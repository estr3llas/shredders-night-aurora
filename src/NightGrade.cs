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
    ///  - A small grade: more contrast, less saturation (night vision is less colourful), plus the
    ///    NightBrightness exposure offset.
    /// The global post-process profile is a shared asset, so every original value is cached and put back.
    ///
    /// Runs once a second (Night's guard). Scene searches (FindObjectsOfType) happen only after a scene load; the
    /// profile's effect objects and parameter field offsets are cached with the profile.
    /// </summary>
    internal static class NightGrade
    {
        const float ContrastAdd = 15f, SaturationAdd = -15f;

        static PostProcessProfile profile;
        static Bloom bloom;
        static AutoExposure autoExposure;
        static ColorGrading grading;
        static bool bloomWas, exposureWas;
        static float contrastWas, saturationWas, exposureOffsetWas;
        static bool contrastOverrideWas, saturationOverrideWas, exposureOffsetOverrideWas;
        // After a scene load the global volume may activate a little later, so allow a few once-a-second searches,
        // then stop until the next load (FindObjectsOfType walks every object in the scene).
        const int SearchAttempts = 10;
        static int searchesLeft = SearchAttempts;

        static float reflectionWas = -1f;
        static readonly List<(ReflectionProbe probe, float intensity)> probes = new List<(ReflectionProbe, float)>();

        static float ReflectionScale => Mathf.Clamp(Mod.NightReflections.Value, 0f, 1f);

        public static void OnSceneInitialized() => searchesLeft = SearchAttempts;

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
            if (profile == null)
            {
                if (searchesLeft <= 0) return;
                searchesLeft--;
                var found = GlobalProfile();
                if (found == null) return;
                Capture(found);
            }
            if (bloom != null && bloom.active) bloom.active = false;
            if (autoExposure != null && autoExposure.active) autoExposure.active = false;
            if (grading != null)
            {
                SetParam(grading.contrast, Mathf.Clamp(contrastWas + ContrastAdd, -100f, 100f));
                SetParam(grading.saturation, Mathf.Clamp(saturationWas + SaturationAdd, -100f, 100f));
                // With auto exposure off, this is the night's overall brightness (EV). Raising it does not bring the
                // glow back: that came from reflections, fog and bloom, which stay down.
                SetParam(grading.postExposure, exposureOffsetWas + Mathf.Clamp(Mod.NightBrightness.Value, -3f, 3f));
            }
        }

        static void Capture(PostProcessProfile p)
        {
            profile = p;
            bloom = Get<Bloom>(p);
            autoExposure = Get<AutoExposure>(p);
            grading = Get<ColorGrading>(p);
            bloomWas = bloom != null && bloom.active;
            exposureWas = autoExposure != null && autoExposure.active;
            if (grading == null) return;
            Il2CppParam.TryGetFloat(grading.contrast, "value", out contrastWas);
            Il2CppParam.TryGetBool(grading.contrast, "overrideState", out contrastOverrideWas);
            Il2CppParam.TryGetFloat(grading.saturation, "value", out saturationWas);
            Il2CppParam.TryGetBool(grading.saturation, "overrideState", out saturationOverrideWas);
            Il2CppParam.TryGetFloat(grading.postExposure, "value", out exposureOffsetWas);
            Il2CppParam.TryGetBool(grading.postExposure, "overrideState", out exposureOffsetOverrideWas);
        }

        static void RestorePost()
        {
            if (profile == null) return;
            if (bloom != null) bloom.active = bloomWas;
            if (autoExposure != null) autoExposure.active = exposureWas;
            if (grading != null)
            {
                Il2CppParam.TrySetFloat(grading.contrast, "value", contrastWas);
                Il2CppParam.TrySetBool(grading.contrast, "overrideState", contrastOverrideWas);
                Il2CppParam.TrySetFloat(grading.saturation, "value", saturationWas);
                Il2CppParam.TrySetBool(grading.saturation, "overrideState", saturationOverrideWas);
                Il2CppParam.TrySetFloat(grading.postExposure, "value", exposureOffsetWas);
                Il2CppParam.TrySetBool(grading.postExposure, "overrideState", exposureOffsetOverrideWas);
            }
            profile = null;
            bloom = null;
            autoExposure = null;
            grading = null;
            searchesLeft = SearchAttempts;   // next Apply re-captures from the (now restored) profile
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
