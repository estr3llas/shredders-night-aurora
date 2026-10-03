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
  - **Night look.** The game's stock night is lit almost entirely by flat blue sky ambient, so snow, clothes and
    boards look self-lit and "glowy". While always-night is on, the mod dims the Tropos surface ambient and the
    fog/haze ambient scattering, doubles the moonlight and turns on soft moon shadows. Turning night off restores
    the originals.
  - The mod never forces anything every frame.
  - Levels that ship native time presets (`Lirp.Scene.TimePresets`) also get their own night preset loaded.
- **Aurora.** Three procedurally generated curtain ribbons sit on a sky sphere centred on the camera, just inside
  the far plane, so terrain still occludes them.
  - Each ribbon is textured with a generated, tileable "rays" texture: green at the base, teal in the middle,
    violet at the top.
  - It's drawn with a transparent shader the game already ships (`Legacy Shaders/Particles/Additive`), so there's
    no AssetBundle and no Unity Editor dependency.
  - The ribbons sway and shimmer through vertex updates written into persistent native arrays, so nothing is
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
| `NightAmbient` | `0.3` | Multiplier on sky ambient on surfaces at night. `1` is the game's stock look, which is flat and glowy. |
| `NightHaze` | `0.3` | Multiplier on how much sky light the fog and haze scatter at night. `1` is the stock blue glow. |
| `MoonBrightness` | `2` | Multiplier on the moonlight. The moon also casts soft shadows while night is forced. |
| `AuroraEnabled` | `true` | Draw the aurora. |
| `AuroraIntensity` | `1.0` | `0`–`3`. |
| `AuroraAzimuth` | `0` | World heading of the aurora's centre, in degrees. |
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

## Credits
- Built by estr3llas with an AI coding agent, using the
  [universal-modder](https://github.com/rehan-remade/universal-modder) toolkit.
- Loader: MelonLoader (LavaGang). Patching: HarmonyX and Il2CppInterop.
- Prior Shredders modding by the [Shredders-Modding](https://github.com/Shredders-Modding) community.
- Shredders © Foampunch. This project is not affiliated with Foampunch.

## License
MIT. See `LICENSE`.
