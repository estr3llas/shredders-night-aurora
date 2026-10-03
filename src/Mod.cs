using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ShreddersNightAurora.Mod), "Shredders Night + Aurora", "0.1.0", "estr3llas")]
[assembly: MelonGame("Foampunch", "Shredders")]

namespace ShreddersNightAurora
{
    public sealed class Mod : MelonMod
    {
        internal static Mod Instance;
        internal static MelonLogger.Instance Log => Instance.LoggerInstance;

        internal static MelonPreferences_Entry<bool> NightEnabled;
        internal static MelonPreferences_Entry<float> NightSunAltitude;
        internal static MelonPreferences_Entry<float> NightAmbient;
        internal static MelonPreferences_Entry<float> MoonBrightness;
        internal static MelonPreferences_Entry<float> MoonShadowStrength;
        internal static MelonPreferences_Entry<float> MoonElevation;
        internal static MelonPreferences_Entry<float> NightBrightness;
        internal static MelonPreferences_Entry<float> NightHaze;
        internal static MelonPreferences_Entry<bool> NightFog;
        internal static MelonPreferences_Entry<float> NightReflections;
        internal static MelonPreferences_Entry<bool> NightGradeEnabled;
        internal static MelonPreferences_Entry<bool> AuroraEnabled;
        internal static MelonPreferences_Entry<float> AuroraIntensity;
        internal static MelonPreferences_Entry<float> AuroraAzimuth;
        internal static MelonPreferences_Entry<string> NightKey;
        internal static MelonPreferences_Entry<string> AuroraKey;
        internal static MelonPreferences_Entry<bool> Verbose;

        static MelonPreferences_Category category;
        readonly Hotkey nightKey = new Hotkey();
        readonly Hotkey auroraKey = new Hotkey();
        readonly Aurora aurora = new Aurora();

        public override void OnInitializeMelon()
        {
            Instance = this;
            category = MelonPreferences.CreateCategory("ShreddersNightAurora", "Shredders Night + Aurora");
            NightEnabled = category.CreateEntry("NightEnabled", true, "Always night",
                "Keep the sun below the horizon everywhere (the game's own night lights and moon take over).");
            NightSunAltitude = category.CreateEntry("NightSunAltitude", -12f, "Night sun altitude",
                "Degrees below the horizon the sun is held at (-1 = dusk, -12 = nautical night, -18 = darkest).");
            NightBrightness = category.CreateEntry("NightBrightness", 0.4f, "Night brightness",
                "Overall night exposure in EV stops (0 = darker, 1 = brighter). Safe to raise: it doesn't bring the glow back.");
            NightAmbient = category.CreateEntry("NightAmbient", 0.5f, "Night ambient",
                "Multiplier on the game's sky fill light at night; keeps areas out of the moonlight from going pitch black.");
            MoonBrightness = category.CreateEntry("MoonBrightness", 3f, "Moon brightness",
                "Multiplier on the moonlight at night. The moon also casts soft shadows while always-night is on.");
            MoonShadowStrength = category.CreateEntry("MoonShadowStrength", 0.6f, "Moon shadow strength",
                "0..1. How dark moon shadows are (1 = fully black).");
            MoonElevation = category.CreateEntry("MoonElevation", 45f, "Moon elevation",
                "Degrees above the horizon. The game's moon sits at ~20°, which leaves whole slopes in black shadow; 0 = keep the game's.");
            NightHaze = category.CreateEntry("NightHaze", 0.3f, "Night haze glow",
                "Multiplier on how much sky light the fog and haze scatter at night (1 = stock blue glow).");
            NightFog = category.CreateEntry("NightFog", false, "Volumetric fog at night",
                "The game's volumetric fog renders as a milky glowing veil at night; off = clear night.");
            NightReflections = category.CreateEntry("NightReflections", 0.35f, "Night reflections",
                "Multiplier on sky reflections at night. Stock reflections put a pale 'foam' sheen on snow, rails and boards.");
            NightGradeEnabled = category.CreateEntry("NightGrade", true, "Night grade",
                "At night: no auto exposure (lets it be dark), no bloom (no glow halos), +contrast, -saturation.");
            AuroraEnabled = category.CreateEntry("AuroraEnabled", true, "Aurora borealis",
                "Draw an aurora in the sky. It fades out automatically while the sun is up.");
            AuroraIntensity = category.CreateEntry("AuroraIntensity", 1f, "Aurora intensity", "0 = invisible, 1 = default, 2 = very bright.");
            AuroraAzimuth = category.CreateEntry("AuroraAzimuth", 0f, "Aurora direction",
                "World-space compass heading of the aurora's centre in degrees (0 = +Z).");
            NightKey = category.CreateEntry("NightToggleKey", "F7", "Night toggle key", "UnityEngine.KeyCode name.");
            AuroraKey = category.CreateEntry("AuroraToggleKey", "F8", "Aurora toggle key", "UnityEngine.KeyCode name.");
            Verbose = category.CreateEntry("VerboseLogging", false, "Verbose logging",
                "Log every environment/time call the game makes.");

            nightKey.Bind(NightKey.Value);
            auroraKey.Bind(AuroraKey.Value);
            NightKey.OnEntryValueChanged.Subscribe((_, v) => nightKey.Bind(v));
            AuroraKey.OnEntryValueChanged.Subscribe((_, v) => auroraKey.Bind(v));
            NightEnabled.OnEntryValueChanged.Subscribe((_, v) => Night.SetEnabled(v));

            Log.Msg($"Loaded. Night={NightEnabled.Value} ({NightKey.Value}), Aurora={AuroraEnabled.Value} ({AuroraKey.Value})");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (Verbose.Value) Log.Msg($"Scene initialized: {buildIndex} '{sceneName}'");
            Night.OnSceneInitialized();
        }

        public override void OnUpdate()
        {
            if (nightKey.Pressed())
            {
                NightEnabled.Value = !NightEnabled.Value; // OnEntryValueChanged applies it
                category.SaveToFile(false);
                Log.Msg($"Always night: {(NightEnabled.Value ? "ON" : "OFF")}");
            }
            if (auroraKey.Pressed())
            {
                AuroraEnabled.Value = !AuroraEnabled.Value;
                category.SaveToFile(false);
                Log.Msg($"Aurora: {(AuroraEnabled.Value ? "ON" : "OFF")}");
            }
            Night.Tick();
        }

        public override void OnLateUpdate()
        {
            aurora.LateUpdate(AuroraEnabled.Value);
        }
    }
}
