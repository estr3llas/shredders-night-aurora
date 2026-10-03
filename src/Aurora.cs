using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLirp;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ShreddersNightAurora
{
    /// <summary>
    /// Procedural aurora drawn with a transparent shader the game already ships (no AssetBundle, no Unity Editor).
    ///
    /// The look follows how real aurora appears (NPS "The Colors of the Aurora", Lummerzheim; Lawlor &amp; Genetti 2011):
    ///  - curtains of vertical rays that follow the magnetic field, each ray reaching its own height;
    ///  - a sharp lower border (green 557.7 nm oxygen emission starts abruptly at ~100 km) with a thin purple
    ///    nitrogen fringe under it when the display is intense;
    ///  - green fading upward into a faint, diffuse red (630 nm oxygen at 200+ km is long-lived, so it has no ray
    ///    structure) with a hint of blue at the very top;
    ///  - large-scale folds and several parallel curtains, rays sliding sideways, brightness surges running along the
    ///    curtain, and a soft glow around the bright parts (drawn as a separate wide, ray-less layer, because the
    ///    night grade turns bloom off).
    ///
    /// Geometry: ribbons on a unit sphere around the camera (scaled just inside the far plane so terrain still
    /// occludes them). Two curtains circle the whole horizon and one crosses high overhead, so there is aurora in
    /// every direction. Vertices are rewritten each frame into persistent native arrays (no per-frame allocation)
    /// and the whole effect fades with the sun altitude.
    /// </summary>
    internal sealed class Aurora
    {
        sealed class Layer
        {
            // Path: azimuth centre/span (deg), lower-border elevation and curtain height (deg).
            public float Azimuth, Span, Elevation, Height;
            // Folds: sideways displacement amplitude (deg) and how many folds round the path. When
            // Fold * 2π * FoldCount exceeds Span the lower border doubles back on itself, like real curtain folds.
            public float Fold, FoldCount, Lean, Phase;
            public float URepeat, Scroll, Alpha;
            public bool Glow;            // soft halo layer: wider, ray-less texture
            public Material Material;
            public Mesh Mesh;
            public Il2CppStructArray<Vector3> Vertices;
            public Il2CppStructArray<Color32> Colors;

            public Layer GlowOf(float alpha) => new Layer
            {
                Azimuth = Azimuth, Span = Span, Elevation = Elevation - 2f, Height = Height + 10f,
                Fold = Fold, FoldCount = FoldCount, Lean = Lean, Phase = Phase,
                URepeat = 1f, Scroll = 0f, Alpha = alpha, Glow = true,
            };
        }

        const int Segments = 320;
        const float TwoPi = Mathf.PI * 2f;
        const float Deg = Mathf.PI / 180f;
        const int CurtainWidth = 1024, CurtainHeight = 512;
        const int GlowWidth = 64, GlowHeight = 128;

        // Shaders shipped by Shredders (checked in resources.assets / globalgamemanagers.assets).
        static readonly string[] ShaderNames =
        {
            "Legacy Shaders/Particles/Additive",
            "Sprites/Default",
        };

        GameObject root;
        Texture2D curtainTexture, glowTexture;
        Layer[] layers;
        bool additive;
        float visibility;          // smoothed 0..1
        float nextCameraCheck;
        Camera camera;
        bool failed;

        public void LateUpdate(bool enabled)
        {
            if (failed) return;
            float target = enabled ? Mathf.Clamp(Mod.AuroraIntensity.Value, 0f, 3f) * NightFactor() : 0f;
            visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime * 0.5f);

            if (visibility <= 0.001f)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }

            if (root == null && !Build()) return;
            if (!root.activeSelf) root.SetActive(true);

            if (camera == null || Time.unscaledTime >= nextCameraCheck)
            {
                camera = Camera.main;
                nextCameraCheck = Time.unscaledTime + 2f;
            }
            if (camera == null) return;

            var t = root.transform;
            t.position = camera.transform.position;
            t.rotation = Quaternion.Euler(0f, Mod.AuroraAzimuth.Value, 0f);
            // Sit just inside the far plane so terrain occludes the aurora but nothing else gets clipped.
            t.localScale = Vector3.one * Mathf.Clamp(camera.farClipPlane * 0.85f, 200f, 20000f);

            float time = Time.time;
            for (int i = 0; i < layers.Length; i++) Animate(layers[i], time);
        }

        static float NightFactor()
        {
            var env = EnvironmentManager.instance;
            if (env == null) return 0f; // menus / loader: no sky to draw on
            float altitude = env.GetSunAltitude();
            // Fully visible once the sun is 6 degrees below the horizon, gone at +2 degrees.
            return Mathf.Clamp01((2f - altitude) / 8f);
        }

        bool Build()
        {
            Shader shader = null;
            foreach (var name in ShaderNames)
            {
                shader = Shader.Find(name);
                if (shader != null) { additive = name.Contains("Additive"); Mod.Log.Msg($"Aurora shader: {name}"); break; }
            }
            if (shader == null)
            {
                Mod.Log.Error("No usable transparent shader found; aurora disabled.");
                failed = true;
                return false;
            }

            if (curtainTexture == null) curtainTexture = BuildCurtainTexture();
            if (glowTexture == null) glowTexture = BuildGlowTexture();

            root = new GameObject("ShreddersNightAurora");
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.DontSave;

            // Main curtain: all round the horizon with deep folds. Second curtain: higher, parallel, fainter.
            // Overhead arc: crosses near the zenith. URepeat is an integer on full rings so the rays wrap seamlessly.
            // Lower borders sit above typical ridgelines (~10°): from the riding camera the sky band is narrow and
            // mountains hide anything lower.
            var main = new Layer { Azimuth = 0f, Span = 360f, Elevation = 9f, Height = 36f, Fold = 13f, FoldCount = 5f, Lean = 3f, Phase = 0.0f, URepeat = 10f, Scroll = 0.0035f, Alpha = 1.00f };
            var second = new Layer { Azimuth = 0f, Span = 360f, Elevation = 17f, Height = 36f, Fold = 9f, FoldCount = 4f, Lean = -2f, Phase = 2.1f, URepeat = 7f, Scroll = -0.0025f, Alpha = 0.55f };
            var overhead = new Layer { Azimuth = 90f, Span = 200f, Elevation = 44f, Height = 24f, Fold = 10f, FoldCount = 2f, Lean = 4f, Phase = 4.4f, URepeat = 4f, Scroll = 0.005f, Alpha = 0.6f };
            // Glow layers render first (lower queue) so the sharp curtains sit on top of their halo.
            layers = new[] { main.GlowOf(0.35f), second.GlowOf(0.25f), overhead.GlowOf(0.25f), second, overhead, main };
            for (int i = 0; i < layers.Length; i++) BuildLayer(layers[i], shader, i);
            return true;
        }

        void BuildLayer(Layer layer, Shader shader, int index)
        {
            var material = new Material(shader) { name = "AuroraLayer" + index, hideFlags = HideFlags.DontSave };
            material.mainTexture = layer.Glow ? glowTexture : curtainTexture;
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.renderQueue = 3000 + index;
            layer.Material = material;

            int vertexCount = (Segments + 1) * 2;
            layer.Vertices = new Il2CppStructArray<Vector3>(vertexCount);
            layer.Colors = new Il2CppStructArray<Color32>(vertexCount);
            var uv = new Il2CppStructArray<Vector2>(vertexCount);
            var triangles = new Il2CppStructArray<int>(Segments * 6);
            for (int s = 0; s <= Segments; s++)
            {
                float u = (float)s / Segments * layer.URepeat;
                uv[s * 2] = new Vector2(u, 0f);
                uv[s * 2 + 1] = new Vector2(u, 1f);
                if (s == Segments) continue;
                int v = s * 2, k = s * 6;
                triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
            }

            var mesh = new Mesh { name = "AuroraLayer" + index, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            layer.Mesh = mesh;
            Animate(layer, 0f, uploadOnly: true);
            mesh.uv = uv;
            mesh.triangles = triangles;

            var go = new GameObject("AuroraLayer" + index);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        void Animate(Layer layer, float time, bool uploadOnly = false)
        {
            float strength = uploadOnly ? 0f : visibility * layer.Alpha;
            var vertices = layer.Vertices;
            var colors = layer.Colors;
            float ph = layer.Phase;
            for (int s = 0; s <= Segments; s++)
            {
                float x = (float)s / Segments;
                float p = TwoPi * x;   // every term below is periodic in x, so rings close without a seam
                float edge = layer.Span >= 360f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Min(x, 1f - x) * 6f);

                // Large folds that slowly travel and breathe, plus smaller kinks.
                float fold = layer.Fold * (0.75f + 0.25f * Mathf.Sin(time * 0.05f + ph))
                             * Mathf.Sin(p * layer.FoldCount + time * 0.06f + ph);
                float kink = 0.35f * layer.Fold * Mathf.Sin(p * (layer.FoldCount * 3f + 1f) - time * 0.17f + ph * 1.7f);
                float az = (layer.Azimuth + (x - 0.5f) * layer.Span + fold + kink) * Deg;

                float lowEl = layer.Elevation + 2f * Mathf.Sin(p * 3f + time * 0.07f + ph);
                float height = layer.Height * (0.75f + 0.25f * Mathf.Sin(p * 2f - time * 0.04f + ph));
                vertices[s * 2] = Direction(az, lowEl * Deg);
                vertices[s * 2 + 1] = Direction(az + layer.Lean * Deg, (lowEl + height) * Deg);

                // Activity: slow bright/faint patches, plus brightness surges running along the curtain.
                float activity = 0.3f + 0.7f * Mathf.Clamp01(0.55f + 0.45f * Mathf.Sin(p + time * 0.03f + ph)
                                                              + 0.3f * Mathf.Sin(p * 2f - time * 0.045f + ph * 0.6f));
                float surge = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(p * 3f - time * 0.35f + ph)), 8f);
                float shimmer = layer.Glow ? 1f
                    : 0.75f + 0.15f * Mathf.Sin(p * 37f + time * 1.3f + ph) + 0.10f * Mathf.Sin(p * 83f - time * 2.1f);
                float a = strength * edge * shimmer * (activity + 0.6f * surge) * (additive ? 1f : 0.8f);
                var c = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                colors[s * 2] = c;
                colors[s * 2 + 1] = c;
            }
            layer.Mesh.vertices = vertices;
            layer.Mesh.colors32 = colors;
            if (!uploadOnly && layer.Scroll != 0f) layer.Material.mainTextureOffset = new Vector2(time * layer.Scroll, 0f);
        }

        static Vector3 Direction(float azimuth, float elevation)
        {
            float c = Mathf.Cos(elevation);
            return new Vector3(Mathf.Sin(azimuth) * c, Mathf.Sin(elevation), Mathf.Cos(azimuth) * c);
        }

        // Emission colours (linear-ish, before the shader's 2x): oxygen green, nitrogen purple fringe,
        // high-altitude oxygen red, and the faint blue of sunlit ions at the very top.
        static readonly Color Green = new Color(0.20f, 1.00f, 0.42f);   // a touch more saturated: the night grade desaturates
        static readonly Color Fringe = new Color(0.85f, 0.25f, 0.95f);
        static readonly Color Red = new Color(1.00f, 0.18f, 0.40f);
        static readonly Color TopBlue = new Color(0.35f, 0.45f, 1.00f);

        /// <summary>Tileable (in u) curtain: thin rays of varying height above a sharp lower border.</summary>
        static Texture2D BuildCurtainTexture()
        {
            var rng = new System.Random(1874170);
            var rays = new float[CurtainWidth];
            var rayTop = new float[CurtainWidth];
            var redGlow = new float[CurtainWidth];

            // Ray brightness: sharpened sines at integer frequencies (tileable), from broad bundles to fine rays.
            int[] freqs = { 4, 7, 11, 18, 29, 47, 76, 123 };
            float[] amps = { 0.9f, 0.8f, 0.7f, 0.6f, 0.55f, 0.45f, 0.35f, 0.25f };
            float[] phase = new float[freqs.Length];
            for (int i = 0; i < phase.Length; i++) phase[i] = (float)(rng.NextDouble() * Math.PI * 2);
            float hp1 = (float)(rng.NextDouble() * 6.28), hp2 = (float)(rng.NextDouble() * 6.28), hp3 = (float)(rng.NextDouble() * 6.28);
            float max = 0f;
            for (int x = 0; x < CurtainWidth; x++)
            {
                float u = (float)x / CurtainWidth, r = 0f;
                for (int i = 0; i < freqs.Length; i++)
                    r += amps[i] * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(TwoPi * freqs[i] * u + phase[i]), 4f);
                rays[x] = r;
                max = Mathf.Max(max, r);
                // Each ray bundle reaches its own height; red glow drifts in broad, structureless patches.
                rayTop[x] = Mathf.Clamp(0.62f + 0.18f * Mathf.Sin(TwoPi * 3f * u + hp1)
                                        + 0.12f * Mathf.Sin(TwoPi * 13f * u + hp2)
                                        + 0.08f * Mathf.Sin(TwoPi * 41f * u + hp3), 0.35f, 0.98f);
                redGlow[x] = 0.5f + 0.5f * Mathf.Sin(TwoPi * 2f * u + hp2 * 0.5f);
            }

            var pixels = new Color32[CurtainWidth * CurtainHeight];
            for (int y = 0; y < CurtainHeight; y++)
            {
                float v = (float)y / (CurtainHeight - 1);
                // Sharp lower border, then exponential fall-off with height.
                float border = Mathf.SmoothStep(0f, 1f, (v - 0.03f) / 0.035f);
                float decay = Mathf.Exp(-v * 2.2f);
                float fringe = Mathf.Exp(-Mathf.Pow((v - 0.03f) / 0.018f, 2f));      // thin purple band at the border
                float redBand = Mathf.Exp(-Mathf.Pow((v - 0.78f) / 0.18f, 2f));       // diffuse red, high up
                float blueTop = Mathf.SmoothStep(0f, 1f, (v - 0.85f) / 0.15f) * (1f - v) * 4f;
                for (int x = 0; x < CurtainWidth; x++)
                {
                    float r = rays[x] / max;
                    float rayShape = 0.18f + 0.82f * r * r;                          // faint sheet + bright rays
                    float top = 1f - Mathf.SmoothStep(0f, 1f, (v - rayTop[x] + 0.2f) / 0.2f);
                    float green = border * decay * rayShape * top * 1.4f;
                    float purple = fringe * (0.25f + 0.75f * r) * 0.55f;
                    float red = redBand * (0.35f + 0.65f * redGlow[x]) * 0.22f;
                    float blue = blueTop * rayShape * 0.08f;

                    Color e = Green * green + Fringe * purple + Red * red + TopBlue * blue;
                    float m = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                    // Store colour normalised in rgb and intensity in alpha: the additive shader adds rgb * alpha.
                    pixels[y * CurtainWidth + x] = m <= 1e-4f
                        ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)(e.r / m * 255f), (byte)(e.g / m * 255f), (byte)(e.b / m * 255f),
                                      (byte)(Mathf.Clamp01(m) * 255f));
                }
            }
            return MakeTexture("AuroraCurtain", CurtainWidth, CurtainHeight, pixels);
        }

        /// <summary>Soft halo behind the curtains: no rays, wide vertical gaussian, green fading to a red-violet top.</summary>
        static Texture2D BuildGlowTexture()
        {
            var pixels = new Color32[GlowWidth * GlowHeight];
            for (int y = 0; y < GlowHeight; y++)
            {
                float v = (float)y / (GlowHeight - 1);
                float glow = Mathf.Exp(-Mathf.Pow((v - 0.3f) / 0.2f, 2f)) * 0.55f
                             + Mathf.Exp(-Mathf.Pow((v - 0.75f) / 0.2f, 2f)) * 0.12f;
                Color col = Color.Lerp(Green, Red, Mathf.SmoothStep(0f, 1f, (v - 0.4f) / 0.5f) * 0.6f);
                var c = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(Mathf.Clamp01(glow) * 255f));
                for (int x = 0; x < GlowWidth; x++) pixels[y * GlowWidth + x] = c;
            }
            return MakeTexture("AuroraGlow", GlowWidth, GlowHeight, pixels);
        }

        static Texture2D MakeTexture(string name, int width, int height, Color32[] pixels)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
