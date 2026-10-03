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
