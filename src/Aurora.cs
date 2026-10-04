using System;
using System.Diagnostics;
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
    /// every direction. The whole effect fades with the sun altitude.
    ///
    /// Cost: everything is built during level load (<see cref="Prewarm"/>), not when the aurora first appears.
    /// Per frame the root follows the camera and the rays scroll; the curtain geometry is rewritten for one curtain
    /// per frame (round-robin, at most 60 Hz each). Each curtain and its glow share one path evaluation. Every wave
    /// is sin(k·x + ω·t): sin/cos(k·x) are tabulated once and the time part is evaluated once per curtain, so the
    /// per-vertex work is 8 sin/cos for a curtain and its glow (was ~36). Vertex data goes into persistent native
    /// arrays, so nothing is allocated per frame.
    /// </summary>
    internal sealed class Aurora
    {
        /// <summary>tabulated sin(k·2πx) and cos(k·2πx) over the curtain's segments.</summary>
        sealed class Wave
        {
            public readonly float[] Sin = new float[Segments + 1], Cos = new float[Segments + 1];

            public Wave(int k)
            {
                for (int s = 0; s <= Segments; s++)
                {
                    float p = TwoPi * k * s / Segments;
                    Sin[s] = MathF.Sin(p);
                    Cos[s] = MathF.Cos(p);
                }
            }
        }

        /// <summary>One curtain plus the soft glow layer that follows the same path.</summary>
        sealed class Curtain
        {
            // Path: azimuth centre/span (deg), lower-border elevation and curtain height (deg).
            public float Azimuth, Span, Elevation, Height;
            // Folds: sideways displacement amplitude (deg) and how many folds round the path. When
            // Fold * 2π * FoldCount exceeds Span the lower border doubles back on itself, like real curtain folds.
            public float Fold, Lean, Phase;
            public int FoldCount;
            public float URepeat, Scroll, Alpha, GlowAlpha;

            public Wave FoldWave, KinkWave;
            public float[] Edge;           // end fade for arcs; all ones for full rings
            public float SinLean, CosLean;
            public float LastUpdate = float.NegativeInfinity;

            public Material Material, GlowMaterial;
            public Mesh Mesh, GlowMesh;
            public Il2CppStructArray<Vector3> Vertices, GlowVertices;
            public Il2CppStructArray<Color32> Colors, GlowColors;
        }

        const int Segments = 320;
        const float TwoPi = MathF.PI * 2f;
        const float Deg = MathF.PI / 180f;
        const int CurtainWidth = 1024, CurtainHeight = 512;
        const int GlowWidth = 64, GlowHeight = 128;
        const float GlowBelow = 2f, GlowAbove = 10f;     // glow extends this many degrees beyond the curtain
        const float MinUpdateInterval = 1f / 60f;         // per curtain
        const float NightFactorInterval = 0.25f;

        // Harmonics shared by every curtain (see Animate).
        static readonly Wave W1 = new Wave(1), W2 = new Wave(2), W3 = new Wave(3), W37 = new Wave(37), W83 = new Wave(83);

        // Shaders shipped by Shredders (checked in resources.assets / globalgamemanagers.assets).
        static readonly string[] ShaderNames =
        {
            "Legacy Shaders/Particles/Additive",
            "Sprites/Default",
        };

        GameObject root;
        Transform rootTransform;
        Texture2D curtainTexture, glowTexture;
        Curtain[] curtains;
        int nextCurtain;
        bool additive;
        float visibility;          // smoothed 0..1
        float nightFactor;
        float nextNightFactor;
        float nextCameraCheck;
        Camera camera;
        Transform cameraTransform;
        bool failed;

        // Verbose-mode timing of LateUpdate, logged every PerfFrames frames.
        const int PerfFrames = 600;
        long perfTicks, perfMaxTicks;
        int perfCount;

        /// <summary>Build textures, materials and meshes while a level loads instead of mid-gameplay.</summary>
        public void Prewarm()
        {
            if (failed || root != null) return;
            if (Build()) root.SetActive(false);
        }

        public void LateUpdate(bool enabled)
        {
            if (!Mod.Verbose.Value) { Run(enabled); return; }
            long start = Stopwatch.GetTimestamp();
            Run(enabled);
            long ticks = Stopwatch.GetTimestamp() - start;
            perfTicks += ticks;
            if (ticks > perfMaxTicks) perfMaxTicks = ticks;
            if (++perfCount < PerfFrames) return;
            double toMs = 1000.0 / Stopwatch.Frequency;
            Mod.Log.Msg($"Aurora cost over {PerfFrames} frames: avg {perfTicks * toMs / PerfFrames:0.000} ms, max {perfMaxTicks * toMs:0.000} ms");
            perfTicks = perfMaxTicks = 0;
            perfCount = 0;
        }

        void Run(bool enabled)
        {
            if (failed) return;
            float now = Time.unscaledTime;
            if (now >= nextNightFactor)
            {
                nightFactor = NightFactor();
                nextNightFactor = now + NightFactorInterval;
            }
            float target = enabled ? Mathf.Clamp(Mod.AuroraIntensity.Value, 0f, 3f) * nightFactor : 0f;
            visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime * 0.5f);

            if (visibility <= 0.001f)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }

            if (root == null && !Build()) return;
            if (!root.activeSelf) root.SetActive(true);

            if (camera == null || now >= nextCameraCheck)
            {
                camera = Camera.main;
                cameraTransform = camera != null ? camera.transform : null;
                nextCameraCheck = now + 2f;
            }
            if (camera == null) return;

            rootTransform.position = cameraTransform.position;
            rootTransform.rotation = Quaternion.Euler(0f, Mod.AuroraAzimuth.Value, 0f);
            // Sit just inside the far plane so terrain occludes the aurora but nothing else gets clipped.
            rootTransform.localScale = Vector3.one * Mathf.Clamp(camera.farClipPlane * 0.85f, 200f, 20000f);

            float time = Time.time;
            // Rays slide every frame (cheap); geometry is rewritten for one curtain per frame.
            for (int i = 0; i < curtains.Length; i++)
                curtains[i].Material.mainTextureOffset = new Vector2(time * curtains[i].Scroll, 0f);
            var c = curtains[nextCurtain];
            nextCurtain = (nextCurtain + 1) % curtains.Length;
            if (now - c.LastUpdate >= MinUpdateInterval)
            {
                c.LastUpdate = now;
                Animate(c, time, visibility);
            }
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
            rootTransform = root.transform;

            // Main curtain: all round the horizon with deep folds. Second curtain: higher, parallel, fainter.
            // Overhead arc: crosses near the zenith. URepeat is an integer on full rings so the rays wrap seamlessly.
            // Lower borders sit above typical ridgelines (~10°): from the riding camera the sky band is narrow and
            // mountains hide anything lower.
            curtains = new[]
            {
                new Curtain { Azimuth = 0f, Span = 360f, Elevation = 9f, Height = 36f, Fold = 13f, FoldCount = 5, Lean = 3f, Phase = 0.0f, URepeat = 10f, Scroll = 0.0035f, Alpha = 1.00f, GlowAlpha = 0.35f },
                new Curtain { Azimuth = 0f, Span = 360f, Elevation = 17f, Height = 36f, Fold = 9f, FoldCount = 4, Lean = -2f, Phase = 2.1f, URepeat = 7f, Scroll = -0.0025f, Alpha = 0.55f, GlowAlpha = 0.25f },
                new Curtain { Azimuth = 90f, Span = 200f, Elevation = 44f, Height = 24f, Fold = 10f, FoldCount = 2, Lean = 4f, Phase = 4.4f, URepeat = 4f, Scroll = 0.005f, Alpha = 0.6f, GlowAlpha = 0.25f },
            };
            // Glows take the lower render queues so the sharp curtains sit on top of their halo.
            for (int i = 0; i < curtains.Length; i++) BuildCurtain(curtains[i], shader, 3000 + i, 3000 + curtains.Length + i);
            return true;
        }

        void BuildCurtain(Curtain c, Shader shader, int glowQueue, int curtainQueue)
        {
            c.FoldWave = new Wave(c.FoldCount);
            c.KinkWave = new Wave(c.FoldCount * 3 + 1);
            c.SinLean = MathF.Sin(c.Lean * Deg);
            c.CosLean = MathF.Cos(c.Lean * Deg);
            c.Edge = new float[Segments + 1];
            for (int s = 0; s <= Segments; s++)
            {
                float x = (float)s / Segments;
                c.Edge[s] = c.Span >= 360f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Min(x, 1f - x) * 6f);
            }

            c.Material = MakeMaterial(shader, curtainTexture, curtainQueue);
            c.GlowMaterial = MakeMaterial(shader, glowTexture, glowQueue);

            int vertexCount = (Segments + 1) * 2;
            c.Vertices = new Il2CppStructArray<Vector3>(vertexCount);
            c.Colors = new Il2CppStructArray<Color32>(vertexCount);
            c.GlowVertices = new Il2CppStructArray<Vector3>(vertexCount);
            c.GlowColors = new Il2CppStructArray<Color32>(vertexCount);
            var uv = new Il2CppStructArray<Vector2>(vertexCount);
            var glowUv = new Il2CppStructArray<Vector2>(vertexCount);
            var triangles = new Il2CppStructArray<int>(Segments * 6);
            for (int s = 0; s <= Segments; s++)
            {
                float u = (float)s / Segments;
                uv[s * 2] = new Vector2(u * c.URepeat, 0f);
                uv[s * 2 + 1] = new Vector2(u * c.URepeat, 1f);
                glowUv[s * 2] = new Vector2(u, 0f);
                glowUv[s * 2 + 1] = new Vector2(u, 1f);
                if (s == Segments) continue;
                int v = s * 2, k = s * 6;
                triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
            }

            c.Mesh = MakeMesh("AuroraCurtain", c.Material);
            c.GlowMesh = MakeMesh("AuroraGlow", c.GlowMaterial);
            Animate(c, 0f, 0f);   // valid vertices before the index buffer goes in
            c.Mesh.uv = uv;
            c.Mesh.triangles = triangles;
            c.GlowMesh.uv = glowUv;
            c.GlowMesh.triangles = triangles;
        }

        Material MakeMaterial(Shader shader, Texture2D texture, int queue)
        {
            var material = new Material(shader) { name = texture.name, hideFlags = HideFlags.DontSave };
            material.mainTexture = texture;
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.renderQueue = queue;
            return material;
        }

        Mesh MakeMesh(string name, Material material)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();

            var go = new GameObject(name);
            go.transform.SetParent(rootTransform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return mesh;
        }

        /// <summary>
        /// Rewrites one curtain and its glow. Every wave is sin(k·p + θ(t)) with p = 2πx; by angle addition that is
        /// sin(k·p)·cos θ + cos(k·p)·sin θ, using the tabulated sin/cos(k·p) and θ evaluated once per call.
        /// Lean and the glow's lower offset are constant angles, so they're also applied by angle addition.
        /// </summary>
        void Animate(Curtain c, float time, float vis)
        {
            float ph = c.Phase;
            float strength = vis * c.Alpha * (additive ? 1f : 0.8f);
            float glowStrength = vis * c.GlowAlpha * (additive ? 1f : 0.8f);

            // Time-dependent parts, once per curtain.
            float breathe = c.Fold * (0.75f + 0.25f * MathF.Sin(time * 0.05f + ph));
            float foldS = MathF.Sin(time * 0.06f + ph), foldC = MathF.Cos(time * 0.06f + ph);
            float kinkA = 0.35f * c.Fold;
            float kinkS = MathF.Sin(-time * 0.17f + ph * 1.7f), kinkC = MathF.Cos(-time * 0.17f + ph * 1.7f);
            float lowS = MathF.Sin(time * 0.07f + ph), lowC = MathF.Cos(time * 0.07f + ph);
            float hS = MathF.Sin(-time * 0.04f + ph), hC = MathF.Cos(-time * 0.04f + ph);
            float a1S = MathF.Sin(time * 0.03f + ph), a1C = MathF.Cos(time * 0.03f + ph);
            float a2S = MathF.Sin(-time * 0.045f + ph * 0.6f), a2C = MathF.Cos(-time * 0.045f + ph * 0.6f);
            float suS = MathF.Sin(-time * 0.35f + ph), suC = MathF.Cos(-time * 0.35f + ph);
            float s37S = MathF.Sin(time * 1.3f + ph), s37C = MathF.Cos(time * 1.3f + ph);
            float s83S = MathF.Sin(-time * 2.1f), s83C = MathF.Cos(-time * 2.1f);
            float glowDownS = MathF.Sin(-GlowBelow * Deg), glowDownC = MathF.Cos(-GlowBelow * Deg);

            float[] fs = c.FoldWave.Sin, fc = c.FoldWave.Cos, ks = c.KinkWave.Sin, kc = c.KinkWave.Cos;
            var vertices = c.Vertices;
            var colors = c.Colors;
            var glowVertices = c.GlowVertices;
            var glowColors = c.GlowColors;

            for (int s = 0; s <= Segments; s++)
            {
                float x = (float)s / Segments;

                // Large folds that slowly travel and breathe, plus smaller kinks.
                float fold = breathe * (fs[s] * foldC + fc[s] * foldS);
                float kink = kinkA * (ks[s] * kinkC + kc[s] * kinkS);
                float az = (c.Azimuth + (x - 0.5f) * c.Span + fold + kink) * Deg;
                float sinAz = MathF.Sin(az), cosAz = MathF.Cos(az);
                float sinTopAz = sinAz * c.CosLean + cosAz * c.SinLean;
                float cosTopAz = cosAz * c.CosLean - sinAz * c.SinLean;

                float lowEl = c.Elevation + 2f * (W3.Sin[s] * lowC + W3.Cos[s] * lowS);
                float heightWave = 0.75f + 0.25f * (W2.Sin[s] * hC + W2.Cos[s] * hS);
                float lowRad = lowEl * Deg;
                float sinLow = MathF.Sin(lowRad), cosLow = MathF.Cos(lowRad);
                float topRad = (lowEl + c.Height * heightWave) * Deg;
                float sinTop = MathF.Sin(topRad), cosTop = MathF.Cos(topRad);

                vertices[s * 2] = new Vector3(sinAz * cosLow, sinLow, cosAz * cosLow);
                vertices[s * 2 + 1] = new Vector3(sinTopAz * cosTop, sinTop, cosTopAz * cosTop);

                // Glow: same path, starting GlowBelow° lower and reaching GlowAbove° higher.
                float sinGLow = sinLow * glowDownC + cosLow * glowDownS;
                float cosGLow = cosLow * glowDownC - sinLow * glowDownS;
                float gTopRad = (lowEl - GlowBelow + (c.Height + GlowAbove) * heightWave) * Deg;
                float sinGTop = MathF.Sin(gTopRad), cosGTop = MathF.Cos(gTopRad);
                glowVertices[s * 2] = new Vector3(sinAz * cosGLow, sinGLow, cosAz * cosGLow);
                glowVertices[s * 2 + 1] = new Vector3(sinTopAz * cosGTop, sinGTop, cosTopAz * cosGTop);

                // Activity: slow bright/faint patches, plus brightness surges running along the curtain.
                float activity = 0.3f + 0.7f * Mathf.Clamp01(0.55f + 0.45f * (W1.Sin[s] * a1C + W1.Cos[s] * a1S)
                                                              + 0.3f * (W2.Sin[s] * a2C + W2.Cos[s] * a2S));
                float surge = MathF.Max(0f, W3.Sin[s] * suC + W3.Cos[s] * suS);
                surge *= surge; surge *= surge; surge *= surge;   // ^8
                float pulse = c.Edge[s] * (activity + 0.6f * surge);
                float shimmer = 0.75f + 0.15f * (W37.Sin[s] * s37C + W37.Cos[s] * s37S)
                                      + 0.10f * (W83.Sin[s] * s83C + W83.Cos[s] * s83S);

                var cc = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(strength * pulse * shimmer) * 255f));
                colors[s * 2] = cc;
                colors[s * 2 + 1] = cc;
                var gc = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(glowStrength * pulse) * 255f));
                glowColors[s * 2] = gc;
                glowColors[s * 2 + 1] = gc;
            }
            c.Mesh.vertices = vertices;
            c.Mesh.colors32 = colors;
            c.GlowMesh.vertices = glowVertices;
            c.GlowMesh.colors32 = glowColors;
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
            // The curtains face the camera, so high anisotropic filtering buys nothing here.
            return MakeTexture("AuroraCurtain", CurtainWidth, CurtainHeight, pixels, anisoLevel: 2);
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
            return MakeTexture("AuroraGlow", GlowWidth, GlowHeight, pixels, anisoLevel: 1);
        }

        static Texture2D MakeTexture(string name, int width, int height, Color32[] pixels, int anisoLevel)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = anisoLevel,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
