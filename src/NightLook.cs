using Il2CppTropos;
using UnityEngine;
using Lirp = Il2CppLirp;

namespace ShreddersNightAurora
{
    /// <summary>
    /// The game's stock night (sun below the horizon) is lit almost entirely by flat blue sky ambient, both on
    /// surfaces and in the Tropos fog/haze. The moon is very dim and casts no shadows, so everything looks
    /// self-lit. While always-night is on, this dims the surface ambient and the fog's ambient in-scattering,
    /// brightens the moon and gives it soft shadows. Originals are cached per level and put back on toggle-off.
    /// </summary>
    internal static class NightLook
    {
        static Lirp.EnvironmentManager cachedFor;
        static float troposAmbient, envAmbient, moonIntensity, fogAmbient, hazeColor;
        static LightShadows moonShadows;
        static bool active;

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
            active = true;
        }

        public static void Restore()
        {
            if (!active) return;
            active = false;
            var env = Lirp.EnvironmentManager.instance;
            var tropos = TroposEnvironment.instance;
            if (env == null || env != cachedFor || env.lighting == null || tropos == null || tropos.m_Lighting == null) return;
            Set(tropos, env, troposAmbient, envAmbient, fogAmbient, hazeColor, moonIntensity, moonShadows);
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
