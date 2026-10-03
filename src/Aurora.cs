using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLirp;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ShreddersNightAurora
{
    /// <summary>
    /// Procedural aurora: a few curtain ribbons on a camera-centred sky sphere, textured with a generated
    /// ray/curtain texture and drawn with a transparent shader the game already ships, so no AssetBundle or Unity
    /// Editor is needed. Ribbons sway and shimmer from per-frame vertex updates into persistent native arrays
    /// (no per-frame allocations), and the whole effect fades with the sun altitude so it never shows in daylight.
    /// </summary>
    internal sealed class Aurora
    {
        sealed class Layer
        {
            public float Azimuth, Span, Elevation, Height, Sway, Lean, URepeat, Scroll, Alpha, Phase;
            public Material Material;
            public Mesh Mesh;
            public Il2CppStructArray<Vector3> Vertices;
            public Il2CppStructArray<Color32> Colors;
        }

        const int Segments = 256;
        const float TwoPi = Mathf.PI * 2f;
        const int TexWidth = 512, TexHeight = 256;
        const float Deg = Mathf.PI / 180f;

        // Shaders shipped by Shredders (checked in resources.assets / globalgamemanagers.assets).
        static readonly string[] ShaderNames =
        {
            "Legacy Shaders/Particles/Additive",
            "Sprites/Default",
        };

        GameObject root;
        Texture2D texture;
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

            if (texture == null) texture = BuildTexture();

            root = new GameObject("ShreddersNightAurora");
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.DontSave;

            // Two rings that go all the way round the horizon plus an arc crossing high overhead, so there is
            // aurora whichever way the rider faces. Slowly drifting "activity" patches keep the rings from looking
            // uniform. URepeat must be an integer on rings so the texture wraps seamlessly.
            layers = new[]
            {
                new Layer { Azimuth = 0f,  Span = 360f, Elevation = 7f,  Height = 28f, Sway = 6f,  Lean = 4f,  URepeat = 8f, Scroll = 0.004f,  Alpha = 1.00f, Phase = 0.0f },
                new Layer { Azimuth = 0f,  Span = 360f, Elevation = 15f, Height = 30f, Sway = 8f,  Lean = -3f, URepeat = 6f, Scroll = -0.003f, Alpha = 0.55f, Phase = 2.1f },
                new Layer { Azimuth = 90f, Span = 180f, Elevation = 42f, Height = 26f, Sway = 10f, Lean = 5f,  URepeat = 3f, Scroll = 0.006f,  Alpha = 0.60f, Phase = 4.4f },
            };
            for (int i = 0; i < layers.Length; i++) BuildLayer(layers[i], shader, i);
            return true;
        }

        void BuildLayer(Layer layer, Shader shader, int index)
        {
            var material = new Material(shader) { name = "AuroraLayer" + index, hideFlags = HideFlags.DontSave };
            material.mainTexture = texture;
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
            for (int s = 0; s <= Segments; s++)
            {
                float x = (float)s / Segments;
                float p = TwoPi * x;   // every term below is periodic in x, so rings close without a seam
                float edge = layer.Span >= 360f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Min(x, 1f - x) * 6f);
                float sway = layer.Sway * (Mathf.Sin(p * 3f + time * 0.11f + layer.Phase)
                                           + 0.45f * Mathf.Sin(p * 7f - time * 0.23f + layer.Phase * 1.7f));
                float az = (layer.Azimuth + (x - 0.5f) * layer.Span + sway) * Deg;
                float lowEl = (layer.Elevation + 2.5f * Mathf.Sin(p * 5f + time * 0.07f + layer.Phase)) * Deg;
                float highEl = lowEl + layer.Height * (0.8f + 0.2f * Mathf.Sin(p * 2f - time * 0.05f)) * Deg;
                float highAz = az + layer.Lean * Deg;
                vertices[s * 2] = Direction(az, lowEl);
                vertices[s * 2 + 1] = Direction(highAz, highEl);

                // Slow-moving bright and faint patches (aurora "activity") plus a faster shimmer.
                float activity = 0.3f + 0.7f * Mathf.Clamp01(0.55f + 0.45f * Mathf.Sin(p + time * 0.03f + layer.Phase)
                                                              + 0.3f * Mathf.Sin(p * 2f - time * 0.045f + layer.Phase * 0.6f));
                float shimmer = 0.55f + 0.25f * Mathf.Sin(p * 9f + time * 0.9f + layer.Phase)
                                      + 0.2f * Mathf.Sin(p * 20f - time * 1.7f);
                byte a = (byte)(Mathf.Clamp01(strength * edge * activity * shimmer * (additive ? 1f : 0.8f)) * 255f);
                var c = new Color32(255, 255, 255, a);
                colors[s * 2] = c;
                colors[s * 2 + 1] = c;
            }
            layer.Mesh.vertices = vertices;
            layer.Mesh.colors32 = colors;
            if (!uploadOnly) layer.Material.mainTextureOffset = new Vector2(time * layer.Scroll, 0f);
        }

        static Vector3 Direction(float azimuth, float elevation)
        {
            float c = Mathf.Cos(elevation);
            return new Vector3(Mathf.Sin(azimuth) * c, Mathf.Sin(elevation), Mathf.Cos(azimuth) * c);
        }

        /// <summary>Tileable (in u) curtain texture: sharp vertical rays, bright lower edge, colour shifting
        /// green → teal → violet with height.</summary>
        static Texture2D BuildTexture()
        {
            var rng = new System.Random(1874170);
            int[] freqs = { 5, 9, 14, 23, 37, 61 };
            float[] amps = { 1f, 0.8f, 0.7f, 0.55f, 0.4f, 0.3f };
            float[] phases = new float[freqs.Length];
            for (int i = 0; i < phases.Length; i++) phases[i] = (float)(rng.NextDouble() * Math.PI * 2);

            var rays = new float[TexWidth];
            float max = 0f;
            for (int x = 0; x < TexWidth; x++)
            {
                float u = (float)x / TexWidth, r = 0f;
                for (int i = 0; i < freqs.Length; i++)
                    r += amps[i] * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * freqs[i] * u + phases[i]), 3f);
                rays[x] = r;
                max = Mathf.Max(max, r);
            }

            var low = new Color(0.25f, 1.00f, 0.45f);
            var mid = new Color(0.20f, 0.85f, 0.75f);
            var high = new Color(0.62f, 0.30f, 0.95f);
            var pixels = new Color32[TexWidth * TexHeight];
            for (int y = 0; y < TexHeight; y++)
            {
                float v = (float)y / (TexHeight - 1);
                Color col = v < 0.45f ? Color.Lerp(low, mid, v / 0.45f) : Color.Lerp(mid, high, (v - 0.45f) / 0.55f);
                float profile = Mathf.SmoothStep(0f, 1f, v / 0.06f) * Mathf.Pow(1f - v, 1.7f);
                for (int x = 0; x < TexWidth; x++)
                {
                    float r = rays[x] / max;
                    float a = profile * (0.3f + 0.7f * r * r);
                    pixels[y * TexWidth + x] = new Color32(
                        (byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
            }

            var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, true)
            {
                name = "AuroraCurtain",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
