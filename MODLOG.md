# MODLOG: Shredders Night + Aurora

Idea: an option for always-night plus an aurora borealis in the sky. Toggles with hotkeys (F7 night, F8 aurora),
persisted in MelonPreferences.

## Facts
- Shredders, Steam 1874170, build 22053287. Unity 2021.3.45f2, IL2CPP x64. No anti-cheat.
- Install: `C:\Program Files (x86)\Steam\steamapps\common\Shredders`
- Saves/log: `%USERPROFILE%\AppData\LocalLow\Foampunch\Shredders` (Player.log)
- Save backup: `%USERPROFILE%\.universal-modder\backups\shredders-saves\20261003-173441.zip`
  (restore with `um backup restore`).
- Decomp and analysis scratch live outside the repo, in `~/shredders-decomp`.

## Route
MelonLoader v0.7.3 (IL2CPP, Il2CppInterop) plus Harmony. The Shredders-Modding community already uses
MelonLoader. No Unity Editor needed: the aurora is a procedural mesh with a procedural texture, drawn with the
game's own `Legacy Shaders/Particles/Additive` shader (found in resources.assets with UnityPy).

## Loader install
- `MelonLoader.x64.zip` v0.7.3 from LavaGang/MelonLoader GitHub release. sha256
  5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416 matches GitHub's asset digest.
- Unzipped into the game folder (adds `version.dll` and `MelonLoader/`). To uninstall, delete those plus
  `Mods/`, `Plugins/`, `UserData/`, `UserLibs/`.

## Log
- 17:35 First ML run. ML pulled the .NET 6 runtime installer from aka.ms (Microsoft), Cpp2IL 2022.1.0-pre-release.21
  from SamboyCoding GitHub releases, and UnityDependencies 2021.3.45 from LavaGang GitHub. Interop assemblies:
  `MelonLoader/Il2CppAssemblies`. Lirp types are in `Il2Cpplirp.scripts.dll` (namespace `Il2CppLirp`).
- Decompiled interop with ilspycmd 9.1 into `~/shredders-decomp/interop` (outside the repo).
- Shaders shipped by the game (UnityPy scan): `Legacy Shaders/Particles/Additive` (resources.assets) and
  `Sprites/Default` (globalgamemanagers.assets). `Shader.Find` resolves the Particles one.
- GOTCHA: a Harmony patch on `Lirp.Scene.SetWeather(int,int)` fired about 264k times in 60 s with garbage args and
  null `this`. IL2CPP identical-code-folding shares that native body with hot, unrelated methods. Don't patch it.
- 17:53 mountain01 (the open world) has an EMPTY `Lirp.Scene.TimePresets` list, so the native preset route from
  recon doesn't apply there. Lighting comes from `EnvironmentManager` sun altitude/azimuth (automaticSunPosition is
  false). Each zone calls `EnvironmentManager.Apply(EnvironmentSetting)` with timeType Custom and its own sun, for
  example 'Environment_TheYard_Sun_Top' at alt 24.1. No DayTimeActivator in mountain01.
- 17:57 Night route that works: prefix-rewrite the altitude in SetSun/SetCustomSun/SmoothSetSun(3), force
  SetAutomaticSunPosition(false) and SetTime(play=false), and re-force the sun in an Apply postfix. A 1 Hz guard
  covers anything else. Sun at -12 gives a navy night sky, with the snow lit by the sky's ambient light.
- Aurora: renders with Particles/Additive at queue 3000+. Terrain occludes it, and the Tropos fog softens the lower
  edge. That's fine.
- Capture gotchas: the ML console is the process's MainWindowHandle (set `hide_console = true` in
  UserData/Loader.cfg). Borderless fullscreen minimises when it loses focus, so launch with `-screen-fullscreen 0
  -screen-width 1600 -screen-height 900 -popupwindow`. Registry backup is in
  ~/shredders-decomp/shredders-reg-backup.reg.
- 18:08 User feedback: "everything is glowy". Lighting dump at night: RenderSettings.sun switches to the 'Moon'
  directional light (colour 0.061/0.075/0.111, intensity 2.31, shadows None). Atmosphere.exposure is fixed at 12.
  Tropos.Lighting ambient is 0.3 and EM.lighting ambient 0.395. Lighting is almost all flat sky ambient, plus a
  bright blue fog in-scatter.
- 18:15 Frozen-frame A/B (Time.timeScale=0, values stepped by hotkey). Ambient x0.3, FogConfiguration.ambientScattering
  and Haze.color x0.3, moon x2 with soft shadows read as a natural night. These are the new defaults
  (NightAmbient, NightHaze, MoonBrightness), cached per level and restored on toggle-off.
- 18:17 Final run: night across spawn and TheYard (its zone Apply alt 24.1 gets overridden), F7 off/on and F8
  off/on all work, aurora visible. The earlier zone change at 18:04 (SmoothSetSun to 27.4) was intercepted too.
  Scripted riding with a held W crashes the rider out of bounds, so it's no good for multi-zone tours.
- Cleanup: the game's registry screen settings were restored from backup, Loader.cfg hide_console was set back to
  false, and the test MelonPreferences.cfg was removed.
- 18:25 User: "everything still glows af, like a light foam on top of snow, mountains, rails, board". I built an A/B
  harness (temporary): freeze with timeScale 0, then step one experiment per key.
  - Round 1: reflections off, post-process off, TroposCamera off, ambient 0, snow subsurface/glitter 0, moon 0.
    Only TroposCamera off removed the veil.
  - Round 2: TroposCamera flags. `renderFog=false` removes the milky veil. `renderAerialPerspective` makes little
    difference, and so does `renderClouds`. Scaling `fogVolume.density` and `haze.density` x0.3 gave a hard black
    band at the horizon, so don't do that.
  - Fix: while night is forced, `TroposCamera.renderFog = false` on Camera.main ('VRCamera'), remembered per
    camera and restored on toggle-off. New pref `NightFog` (default false).
  - Verified: crisp moonlit mountains, F7 brings day back with its fog, F7 again gives night, F8 works.
- Research: Level Design Book, "Lighting for darkness" (Hollywood darkness: contrast and directional moonlight
  rather than flat ambient), and Jensen et al., "Night Rendering" (2000).
- 18:36 User: "still glowy as hell", and the aurora only shows on one side.
  - Aurora: rebuilt as two 360-degree rings (every term periodic in x, integer URepeat, no seam) plus an overhead
    arc, with drifting activity patches. Captures from several headings show aurora in all of them.
  - Post FX dump. The global volume 'POSTPROCESS' uses profile 'Post-process Volume Profile_Shredders' with
    AmbientOcclusion, AutoExposure (min -1.52, max -0.67, key 0.63), Bloom (intensity 1, threshold 2) and
    ColorGrading (ACES, postExp -2.19, contrast 26). The interop generic `ParameterOverride<T>.value` returns
    37.0683 for everything, so it reads the field via il2cpp_class_get_field_from_name + offset.
  - A/B round 3 on a frozen frame. Reflections at 0.3 removed the pale sheen, which is the real "foam" left after
    the fog fix. Auto exposure off gives a darker, natural image. Bloom off is a minor win. Moon x3 gives
    directional shading. Snow subsurface and SnowPostFX made no difference.
  - Fix (NightGrade): reflections x0.35 (RenderSettings plus every ReflectionProbe), AE and bloom inactive,
    contrast +15, saturation -15, ambient 0.2, moon x3. All restored on toggle-off, and the profile originals are
    kept across levels because it's a shared asset.
  - Verified: contrasty moonlit night with no sheen; F7 brings back the full day look; F7 again gives night; F8 OK.
- 18:55 User: the glow is gone, but it's way too dark and pitch black in places.
  - A/B at two spots. In the moon-shadow spot, ambient .5 + shadowStrength .7 + postExposure +.4 made it readable
    without glow. Turning AE back on brightened the lit areas but left the shadows black.
  - The worst dark spots are whole slopes in shadow from the game's low moon (rotation x=20.39°, y=95.2°).
  - Ride luminance (6 frames each, 8-bit mean): stock moon about 27-32; MoonElevation 45 about 37-48, with no
    sheen.
  - Defaults now: NightBrightness +0.4 EV (ColorGrading.postExposure offset), NightAmbient 0.5,
    MoonShadowStrength 0.6, MoonElevation 45 (keeps the heading). All cached and restored on toggle-off.
    Toggling F7 off and on three times gives identical frame luminance (29.4 / 29.3 / 29.4).
- 19:30 User: make the aurora more beautiful (research it).
  - Research: NPS "The Colors of the Aurora" (Lummerzheim) for the structure:
    - green 557.7 nm O emission with a sharp lower border at about 100 km;
    - a purple N2 lower fringe in intense displays;
    - diffuse ray-less red 630 nm above 200 km, plus a blue top on sunlit ions;
    - rays, folds and parallel curtains.
  - Also: Lawlor & Genetti 2011 (volume aurora, vertical deposition profile); Gaia Sky's aurora write-up (a flat
    mesh only works with good textures and multiple layers); the shaders.com Aurora parameter set (base, core and
    tip colours, curtain count, waviness, ray density).
  - Rebuilt Aurora.cs:
    - 1024x512 curtain texture: rays with per-ray tops, sharp border, purple fringe, diffuse red band, blue tip;
      rgb normalised with intensity in alpha for the additive shader;
    - a separate soft glow texture/layer under each curtain (bloom is off at night);
    - folding paths (fold amplitude x 2π x count > span), kinks, surges, finer shimmer;
    - 320 segments, 6 meshes.
  - The first capture showed the curtains hidden behind ridges: the riding camera looks down, so the visible sky
    band is about 0-20° and the mountains reach about 10°. Raised the lower borders to 9° and 17°.
  - Verified at spawn: tall green rays, purple fringe and red tops are visible across headings, and the animation
    runs.
  - A recorded clip was unusable because the rider drifted into a building; it was discarded.
- 20:10 Optimization pass from a static review. The game wasn't run; the user will check later.
  - Aurora: sin/cos tables per harmonic plus angle addition; one path evaluation shared by each curtain and its
    glow (lean and the glow's -2° offset applied by angle addition); x^8 by squaring; one curtain re-meshed per
    frame round-robin (at most 60 Hz each); per-frame texture scroll kept; NightFactor at 4 Hz; root and camera
    transforms cached; textures, meshes and materials built in OnSceneWasInitialized (Prewarm) rather than on
    first fade-in; curtain anisoLevel 8 -> 2, glow 1. Verbose mode logs avg/max LateUpdate cost every 600 frames.
  - Off-game .NET benchmark of the same math: all 3 curtains+glows 0.374 -> 0.105 ms; with round-robin about
    0.035 ms/frame. Equivalence over t in {0, 1.7, 37, 600, 3600, 86400}: max vertex diff 6.4e-5 (unit sphere),
    max alpha diff 1/255.
  - NightGrade: FindObjectsOfType<PostProcessVolume> only after a scene load or a restore (10 attempts max).
    Bloom/AutoExposure/ColorGrading objects cached with the profile.
  - Il2CppParam: field offsets cached per (class, name). Removed the unused TryGetInt.
  - NightLook: GetComponent<TroposCamera> only when Camera.main changes.
  - Not done: merging the 6 draw calls into fewer. The curtains scroll at different speeds (one material each),
    and order-independence only holds for the additive shader, not the Sprites/Default fallback.
- User confirmed the optimized build works in game (commit eeec784 deployed). Released as v0.6.0.
