# Shredders Night + Aurora

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for [Shredders](https://store.steampowered.com/app/1874170/Shredders/)
that keeps the mountain at night, everywhere and all the time, and puts an animated aurora borealis in the sky.
Both can be toggled in game.

| Key | Action |
|---|---|
| **F7** | Toggle always-night. Turning it off gives the game its own sun back for the zone you're in. |
| **F8** | Toggle the aurora. It fades in and out smoothly, and fades out by itself whenever the sun is up. |

Both settings persist between sessions.

## How it works
- **Night.** Shredders lights each zone from `Lirp.EnvironmentManager`'s sun. When you ride into a zone, the
  game calls `EnvironmentManager.Apply(...)` with that zone's sun position.
  - Harmony prefixes on `SetSun`, `SetCustomSun`, `SmoothSetSun`, `SetAutomaticSunPosition` and `SetTime`
    rewrite the requested altitude to below the horizon.
  - A postfix on `Apply` re-asserts it.
  - A once-a-second guard catches anything else.
  - **Night look.** The game's stock night looks like a light "foam" over everything: snow, mountains, rails and
    the board. I tracked it down in two rounds of A/B on a frozen frame, switching one effect at a time.
    1. **Tropos volumetric fog** (`TroposCamera.renderFog`). At night it renders as a milky, glowing veil, so the
       mod switches it off for a clear arctic night. The distance haze stays, for depth.
    2. **Sky reflections.** The reflection probes hold a bright night sky (Tropos sky exposure is 12), so every
       glossy surface gets a pale sheen. The mod scales reflections to 35%.
    3. **Auto exposure and bloom.** Auto exposure pushes the dark scene back towards mid-grey, and bloom then halos
       the snow. Both are off at night.
    4. **Flat lighting.** The moon is ×3 with soft shadows, so slopes facing it are lit and others are shaded.
       The grade adds a bit more contrast and a little less saturation, because night vision is less colourful.

    Removing the glow made some places pitch black, so brightness is put back only through sources that don't
    glow:
    - the moon is raised from the game's 20° to 45°, so whole slopes no longer sit in its long shadows;
    - moon shadows are kept at 60% strength;
    - sky fill light is at 50%;
    - post-exposure is +0.4 EV.

    In a measured ride, frame brightness went from about 28 to about 43 (out of 255), with no sheen coming back.

    This follows the usual "Hollywood darkness" practice (see the
    [Level Design Book](https://book.leveldesignbook.com/process/lighting/darkness)): make it feel dark without
    making it unreadable. Night is carried by contrast and directional moonlight.
    Turning night off restores every original value, including the shared post-process profile and the moon's
    direction.
  - The mod never forces anything every frame.
  - Levels that ship native time presets (`Lirp.Scene.TimePresets`) also get their own night preset loaded.
- **Aurora.** Two curtain rings go all the way round the horizon, and a third arc passes high overhead, so there's
  aurora whichever way you ride.
  - Bright and faint patches drift slowly around the sky.
  - It sits on a sky sphere centred on the camera, just inside the far plane, so terrain still occludes it.
  - Each curtain is textured with a generated, tileable "rays" texture: green at the base, teal in the middle,
    violet at the top.
  - It's drawn with a transparent shader the game already ships (`Legacy Shaders/Particles/Additive`), so there's
    no AssetBundle and no Unity Editor dependency.
  - The curtains sway and shimmer through vertex updates written into persistent native arrays, so nothing is
    allocated per frame.
  - All of the art is generated at runtime by this mod. No game assets are copied or shipped.

## Install
1. Install **MelonLoader v0.7.3** into the Shredders folder.
   - Use the official [release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3) `MelonLoader.x64.zip`,
     sha256 `5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416`, unzipped next to `Shredders.exe`.
   - Or use the official installer.
2. Start the game once so MelonLoader generates its IL2CPP assemblies, then quit.
3. Copy `ShreddersNightAurora.dll` into `Shredders\Mods\`.

**Uninstall:** delete `Mods\ShreddersNightAurora.dll`. To remove MelonLoader too, delete `version.dll`,
`MelonLoader\`, `Mods\`, `Plugins\`, `UserData\` and `UserLibs\`.

## Settings
`Shredders\UserData\MelonPreferences.cfg`, section `[ShreddersNightAurora]`:

| Key | Default | Meaning |
|---|---|---|
| `NightEnabled` | `true` | Always night. |
| `NightSunAltitude` | `-12` | How far below the horizon the sun is held: `-1` is dusk, `-18` is the darkest night. |
| `NightBrightness` | `0.4` | Overall night exposure in EV stops. Raise it to brighten; it doesn't bring the glow back. |
| `NightAmbient` | `0.5` | Multiplier on the sky fill light. It keeps areas out of the moonlight from going pitch black. `1` is the stock look, which is flat and glowy. |
| `NightHaze` | `0.3` | Multiplier on how much sky light the haze scatters at night. `1` is the stock blue glow. |
| `MoonBrightness` | `3` | Multiplier on the moonlight. The moon also casts soft shadows while night is forced. |
| `MoonShadowStrength` | `0.6` | How dark moon shadows are. `1` is fully black. |
| `MoonElevation` | `45` | Moon height in degrees, keeping the game's compass heading. `0` keeps the game's own moon at about 20°. |
| `NightFog` | `false` | Keep the game's volumetric fog at night. At night it renders as a milky glowing veil. |
| `NightReflections` | `0.35` | Multiplier on sky reflections at night. `1` brings back the pale sheen on snow, rails and boards. |
| `NightGrade` | `true` | At night: auto exposure off, bloom off, contrast +15, saturation −15. |
| `AuroraEnabled` | `true` | Draw the aurora. |
| `AuroraIntensity` | `1.0` | `0`–`3`. |
| `AuroraAzimuth` | `0` | Rotates the aurora pattern, in degrees. |
| `NightToggleKey` / `AuroraToggleKey` | `F7` / `F8` | Any `UnityEngine.KeyCode` name. |
| `VerboseLogging` | `false` | Log every sun/time call the game makes, to `MelonLoader\Latest.log`. |

## Build
Requirements:
- .NET SDK 6 or newer;
- a Shredders install with MelonLoader already run once, because the build references
  `MelonLoader\net6` and `MelonLoader\Il2CppAssemblies` from the game folder.

```sh
dotnet build -c Release                                   # -> bin/Release/net6.0/ShreddersNightAurora.dll
dotnet build -c Release -p:Deploy=true                    # also copies it into <game>\Mods
dotnet build -c Release -p:GameDir="D:\Steam\steamapps\common\Shredders"   # non-default install path
```

## Multiplayer
This mod is visual-only and client-side, and it was tested in single-player. In an online session your local sun
is still forced to night; other players see their own sky. Shredders has no anti-cheat, but please keep mods out
of competitive or ranked play.

## Gotchas found while building this
- In the open-world `mountain01`, `Lirp.Scene.TimePresets` is empty, so lighting comes entirely from
  `EnvironmentManager` sun calls (`Apply` with per-zone `EnvironmentSetting`s, `timeType` Custom).
- Don't Harmony-patch `Lirp.Scene.SetWeather(int, int)`. In this build IL2CPP folded its native body with hot,
  unrelated methods, and the detour fires thousands of times a second with garbage arguments.
- Post-processing parameters (`ParameterOverride<T>.value`) read as garbage (every value reads `37.0683`) through
  the Il2CppInterop accessors. `src/Il2CppParam.cs` reads and writes the field through the object's real IL2CPP
  class instead.

## Credits
- Built by estr3llas with an AI coding agent, using the
  [universal-modder](https://github.com/rehan-remade/universal-modder) toolkit.
- Loader: MelonLoader (LavaGang). Patching: HarmonyX and Il2CppInterop.
- Prior Shredders modding by the [Shredders-Modding](https://github.com/Shredders-Modding) community.
- Shredders © Foampunch. This project is not affiliated with Foampunch.

## License
MIT. See `LICENSE`.
