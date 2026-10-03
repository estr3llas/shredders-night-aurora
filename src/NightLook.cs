using Il2CppTropos;
using UnityEngine;
using Lirp = Il2CppLirp;

namespace ShreddersNightAurora
{
    /// <summary>
    /// The game's stock night (sun below the horizon) is lit almost entirely by flat blue sky ambient, and the
    /// Tropos volumetric fog lays a milky, glowing veil over everything, near objects included (the "foam" on snow,
    /// rails and board). Found by A/B on a frozen frame: turning TroposCamera.renderFog off removes the veil, while
    /// clouds, reflections, post-processing and snow shading were not the cause. While always-night is on this
    /// turns the volumetric fog off (clear arctic night; distance haze/aerial perspective stays), dims the surface
    /// ambient and haze, brightens the moon and gives it soft shadows. Originals are restored on toggle-off.
    /// </summary>
    internal static class NightLook
    {
        static Lirp.EnvironmentManager cachedFor;
        static float troposAmbient, envAmbient, moonIntensity, fogAmbient, hazeColor;
        static LightShadows moonShadows;
        static bool active;
        static TroposCamera fogCamera;    // camera whose renderFog we switched off
        static bool fogCameraOriginal;

        static float AmbientScale => Mathf.Clamp(Mod.NightAmbient.Value, 0f, 1f);
        static float HazeScale => Mathf.Clamp(Mod.NightHaze.Value, 0f, 1f);
        static float MoonScale => Mathf.Clamp(Mod.MoonBrightness.Value, 0f, 5f);

        /// <summary>Apply (or re-assert) the night look. Cheap when nothing changed.</summary>
        public static void Apply()
        {
            var env = Lirp.EnvironmentManager.instance;
            var tropos = TroposEnvironment.instance;
            if (env == null || env.lighting == null || tropos == null || tropos.m_Lighting == null) return;

            if (env != cachedFor)
            {
                cachedFor = env;
                troposAmbient = tropos.m_Lighting.ambientIntensity;
                envAmbient = env.lighting.ambientIntensity;
                var moon = tropos.moon;
                if (moon != null) { moonIntensity = moon.intensity; moonShadows = moon.shadows; }
                if (tropos.fogConfig != null) fogAmbient = tropos.fogConfig.ambientScattering;
                if (tropos.haze != null) hazeColor = tropos.haze.color;
            }

            Set(tropos, env, troposAmbient * AmbientScale, envAmbient * AmbientScale, fogAmbient * HazeScale,
                hazeColor * HazeScale, moonIntensity * MoonScale, LightShadows.Soft);
            SetFog(Mod.NightFog.Value);
            active = true;
        }

        public static void Restore()
        {
            if (!active) return;
            active = false;
            RestoreFog();
            var env = Lirp.EnvironmentManager.instance;
            var tropos = TroposEnvironment.instance;
            if (env == null || env != cachedFor || env.lighting == null || tropos == null || tropos.m_Lighting == null) return;
            Set(tropos, env, troposAmbient, envAmbient, fogAmbient, hazeColor, moonIntensity, moonShadows);
        }

        static void SetFog(bool on)
        {
            var cam = Camera.main;
            var tc = cam != null ? cam.GetComponent<TroposCamera>() : null;
            if (tc == null) return;
            if (tc != fogCamera)
            {
                RestoreFog();
                fogCamera = tc;
                fogCameraOriginal = tc.renderFog;
            }
            bool want = on && fogCameraOriginal;
            if (tc.renderFog != want) tc.renderFog = want;
        }

        static void RestoreFog()
        {
            if (fogCamera != null && fogCamera.renderFog != fogCameraOriginal) fogCamera.renderFog = fogCameraOriginal;
            fogCamera = null;
        }

        static void Set(TroposEnvironment tropos, Lirp.EnvironmentManager env, float tAmbient, float eAmbient,
            float fAmbient, float hColor, float mIntensity, LightShadows shadows)
        {
            if (!Mathf.Approximately(tropos.m_Lighting.ambientIntensity, tAmbient)) tropos.m_Lighting.ambientIntensity = tAmbient;
            if (!Mathf.Approximately(env.lighting.ambientIntensity, eAmbient))
            {
                env.lighting.ambientIntensity = eAmbient;
                env.lighting.UpdateShaderParameters();
            }
            var fog = tropos.fogConfig;
            if (fog != null && !Mathf.Approximately(fog.ambientScattering, fAmbient))
            {
                fog.ambientScattering = fAmbient;
                fog.UpdateShaderParameters();
            }
            var haze = tropos.haze;
            if (haze != null && !Mathf.Approximately(haze.color, hColor))
            {
                haze.color = hColor;
                haze.UpdateShaderParameters();
            }
            var moon = tropos.moon;
            if (moon != null)
            {
                if (!Mathf.Approximately(moon.intensity, mIntensity)) moon.intensity = mIntensity;
                if (moon.shadows != shadows) moon.shadows = shadows;
            }
        }
    }
}
