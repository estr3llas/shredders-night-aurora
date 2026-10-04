using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppLirp;
using UnityEngine;
using LirpScene = Il2CppLirp.Scene;

namespace ShreddersNightAurora
{
    /// <summary>
    /// Always-night. Shredders lights the world from Lirp.EnvironmentManager's sun (altitude/azimuth), and every
    /// zone the rider enters calls EnvironmentManager.Apply(EnvironmentSetting) with its own custom sun. The mod
    /// keeps the sun below the horizon by rewriting the altitude in every sun setter, so the game's own night
    /// behaviour (DayTimeActivator lights, moon) kicks in. Levels that also ship time presets
    /// (Lirp.Scene.TimePresets) get their native night preset loaded once so the matching shadowmap is used.
    /// </summary>
    internal static class Night
    {
        const float RetryInterval = 0.5f;
        const float GuardInterval = 1f;

        static bool pending;
        static bool applied;
        static bool applying;             // our own calls pass through the patches untouched
        static float nextTry;
        static string lastWaitState;

        // State saved before forcing night, restored when the toggle goes off.
        static bool savedAutomatic;
        static float savedAltitude, savedAzimuth;
        static double savedTime;
        static bool savedPlay;
        static int savedPresetIndex = -1;  // last preset index the game asked for (levels with presets only)

        static LirpScene presetScene;
        static int nightPresetIndex = -1;

        static float NightAltitude => Mathf.Clamp(Mod.NightSunAltitude.Value, -90f, -1f);
        static bool Active => Mod.NightEnabled.Value && applied;
        static bool Intercept => Active && !applying;

        public static void OnSceneInitialized()
        {
            // Additive level chunks load constantly; just make sure night is (re)asserted once things settle.
            NightGrade.OnSceneInitialized();
            if (!Mod.NightEnabled.Value) return;
            pending = true;
            nextTry = 0f;
        }

        public static void SetEnabled(bool on)
        {
            if (on) { pending = true; nextTry = 0f; return; }
            pending = false;
            Restore();
        }

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < nextTry) return;

            if (pending)
            {
                nextTry = now + RetryInterval;
                if (Mod.NightEnabled.Value && TryApply()) pending = false;
                return;
            }

            nextTry = now + GuardInterval;
            if (!Active) return;
            var env = EnvironmentManager.instance;
            if (env == null) { applied = false; pending = true; NightGrade.Forget(); return; }   // level unloaded
            // Safety net for anything that moves the sun without going through a patched setter.
            if (env.automaticSunPosition || env.GetSunAltitude() > NightAltitude + 0.5f)
            {
                if (Mod.Verbose.Value)
                    Mod.Log.Msg($"Sun drifted (alt {env.GetSunAltitude():0.0}, automatic={env.automaticSunPosition}); forcing night.");
                ForceSun(env);
            }
            NightLook.Apply();
        }

        static bool TryApply()
        {
            var env = EnvironmentManager.instance;
            if (env == null)
            {
                if (Mod.Verbose.Value && lastWaitState != "env") Mod.Log.Msg("Waiting for EnvironmentManager...");
                lastWaitState = "env";
                return false;
            }
            lastWaitState = null;

            if (!applied)
            {
                savedAutomatic = env.automaticSunPosition;
                savedAltitude = env.GetSunAltitude();
                savedAzimuth = env.GetSunAzimuth();
                if (env.time != null) { savedTime = env.time.time; savedPlay = env.time.play; }
                applied = true;
                Mod.Log.Msg($"Saved day state: alt {savedAltitude:0.0}, az {savedAzimuth:0.0}, automatic={savedAutomatic}, time {savedTime:0.00}, play={savedPlay}");
            }

            ApplyNightPreset();
            ForceSun(env);
            if (Mod.Verbose.Value) LogDayTimeActivators();
            Mod.Log.Msg($"Night applied: sun altitude {env.GetSunAltitude():0.0}");
            return true;
        }

        /// <summary>Levels with time presets: load the native night preset (its shadowmap) once.</summary>
        static void ApplyNightPreset()
        {
            var scene = UnityEngine.Object.FindObjectOfType<LirpScene>();
            if (scene == null || scene == presetScene) return;
            presetScene = scene;
            nightPresetIndex = -1;
            var presets = scene.TimePresets;
            if (presets == null || presets.Count == 0) return;

            for (int i = 0; i < presets.Count; i++)
            {
                var p = presets[i];
                if (Mod.Verbose.Value) Mod.Log.Msg($"Time preset {i}: '{p.Name}' {p.Hour:00}:{p.Minute:00} shadowmap='{p.ShadowmapPath}'");
                if (nightPresetIndex < 0 && (p.Name ?? "").IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0)
                    nightPresetIndex = i;
            }
            if (nightPresetIndex < 0) return;
            Mod.Log.Msg($"Loading native night preset #{nightPresetIndex} '{presets[nightPresetIndex].Name}'");
            Call(() => scene.SetTime(nightPresetIndex));
        }

        static void ForceSun(EnvironmentManager env)
        {
            float azimuth = env.GetSunAzimuth();
            Call(() =>
            {
                if (env.automaticSunPosition) env.SetAutomaticSunPosition(false);
                if (env.time != null && env.time.play) env.SetTime((float)env.time.time, false);
                env.SetSun(NightAltitude, azimuth);
            });
            NightLook.Apply();
        }

        static void Restore()
        {
            if (!applied) return;
            applied = false;
            NightLook.Restore();
            var env = EnvironmentManager.instance;
            if (env != null)
            {
                Call(() =>
                {
                    if (presetScene != null && nightPresetIndex >= 0 && savedPresetIndex >= 0)
                        presetScene.SetTime(savedPresetIndex);
                    env.SetTime((float)savedTime, savedPlay);
                    env.SetAutomaticSunPosition(savedAutomatic);
                    if (!savedAutomatic) env.SetSun(savedAltitude, savedAzimuth);
                });
            }
            presetScene = null;
            nightPresetIndex = -1;
            savedPresetIndex = -1;
            Mod.Log.Msg("Night released; the game's own sun is back.");
        }

        static void LogDayTimeActivators()
        {
            var activators = UnityEngine.Object.FindObjectsOfType<DayTimeActivator>();
            Mod.Log.Msg($"DayTimeActivators in level: {activators.Length}");
            foreach (var a in activators)
                Mod.Log.Msg($"  '{a.name}' threshold {a.sunAltitudeThreshold} day={a.dayObjects?.Length} night={a.nightObjects?.Length} lastIsDay={a.lastIsDay}");
        }

        static void Call(Action a)
        {
            applying = true;
            try { a(); }
            catch (Exception e) { Mod.Log.Error(e); }
            finally { applying = false; }
        }

        // ---- Patches: rewrite every sun/time change the game makes while always-night is on. ----
        // Scene.SetWeather(int, int) is deliberately NOT patched: in this build IL2CPP folded its native body with
        // unrelated hot methods, so a detour there fires thousands of times a second with garbage arguments.

        /// <summary>Remember what the game wanted (restored on toggle-off) and return the night altitude.</summary>
        static float Requested(float altitude, float azimuth)
        {
            savedAltitude = altitude;
            savedAzimuth = azimuth;
            return NightAltitude;
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.SetSun))]
        static class EnvSetSun
        {
            static void Prefix(ref float __0, float __1)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"EnvironmentManager.SetSun(alt {__0}, az {__1})");
                if (Intercept) __0 = Requested(__0, __1);
            }
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.SetCustomSun))]
        static class EnvSetCustomSun
        {
            static void Prefix(ref float __0, float __1)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"EnvironmentManager.SetCustomSun(alt {__0}, az {__1})");
                if (Intercept) __0 = Requested(__0, __1);
            }
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.SmoothSetSun), typeof(float), typeof(float), typeof(float))]
        static class EnvSmoothSetSun
        {
            static void Prefix(ref float __0, float __1, float __2)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"EnvironmentManager.SmoothSetSun(alt {__0}, az {__1}, t {__2})");
                if (Intercept) __0 = Requested(__0, __1);
            }
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.SetAutomaticSunPosition))]
        static class EnvSetAutomatic
        {
            static void Prefix(ref bool __0)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"EnvironmentManager.SetAutomaticSunPosition({__0})");
                if (!Intercept) return;
                savedAutomatic = __0;
                __0 = false;
            }
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.SetTime))]
        static class EnvSetTime
        {
            static void Prefix(float __0, ref bool __1)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"EnvironmentManager.SetTime({__0}, play={__1})");
                if (!Intercept) return;
                savedTime = __0;
                savedPlay = __1;
                __1 = false;
            }
        }

        [HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.Apply))]
        static class EnvApply
        {
            static void Postfix(EnvironmentManager __instance, EnvironmentSetting __0)
            {
                if (Mod.Verbose.Value && __0 != null)
                    Mod.Log.Msg($"EnvironmentManager.Apply('{__0.name}', {__0.timeType}, tod={__0.timeOfDay}, custom alt={__0.CustomSunAltitude})");
                if (Intercept) ForceSun(__instance);
            }
        }

        [HarmonyPatch(typeof(LirpScene), nameof(LirpScene.SetTime), typeof(int))]
        static class SceneSetTime
        {
            static void Prefix(ref int __0)
            {
                if (Mod.Verbose.Value && !applying) Mod.Log.Msg($"Scene.SetTime({__0})");
                if (!Intercept || nightPresetIndex < 0) return;
                savedPresetIndex = __0;
                __0 = nightPresetIndex;
            }
        }
    }
}
