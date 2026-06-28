# Si_HeavyTeleporter

> Battlefield vehicle teleport for the **human factions** in [Silica](https://store.steampowered.com/app/504900/Silica/).

Once a team reaches the configured tech tier, any Sol or Centauri soldier can `/st` to call a team vehicle to their current location — useful for getting a Hover Tank, Siege Tank or other heavy unit across the map without driving it the whole way. Commanders and admins have additional forms for tactical positioning of teammates.

## Features

- **Player-callable extraction** — `/st` opens a menu of nearby teleportable vehicles, `/st <N>` confirms the pick
- **Per-team charge pool** — shared between team members, recharges over time, prevents spam
- **Tech-tier gating** — teams have to research up to the configured tier first
- **Anti-rush guard** — refuses teleport too close to enemy HQ / Nest
- **Commander / admin forms** — `/st <playername>` and `/st <x> <z>` for moving someone else
- **Per-player sound** — the teleport cue plays only for the requester and target, not server-wide
- **Configurable countdown** — pre-teleport delay broadcasts a warning so the destination knows incoming

## Installation

1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) on your Silica dedicated server
2. Install the [Silica Admin Mod](https://github.com/data-bomb/Silica) (required dependency)
3. Copy `Si_HeavyTeleporter.dll` to your server's `Mods/` folder
4. (Optional) Copy `cannon_boom.wav` (or your own) to `UserData/sounds/` — referenced by `SoundFile` in the config

## Pickup constraint

> Only vehicles **standing in front of the team's Ultra Heavy Factory** are eligible for teleport (i.e. lined up on the UHF staging pad). The `/st` menu only lists vehicles inside that pickup area. Park the vehicle you want delivered on the UHF pad first.

## Player commands

| Command | Effect |
|--|--|
| `/st` | Request a heavy vehicle teleport to you. Opens a menu of eligible team vehicles parked at the UHF |
| `/st <N>` | Confirm pick N from the menu shown by `/st` |
| `/st status` | Read-only summary: enabled/disabled, your team's required tech tier, charges remaining, recharge timer |

Requirements / restrictions:
- **Faction**: Sol or Centauri only
- **Tech tier**: team must be at or above `Cfg.TechTier`
- **Charges**: team must have at least one charge available
- **Distance from enemy base**: refused if too close to enemy HQ / Nest (`Cfg.MinEnemyBaseDistance`)

## Commander / admin commands

Available to commanders and admins (commanders are still subject to the gameplay restrictions above).

| Command | Effect |
|--|--|
| `/st <playername>` | Teleport that player's controlled unit to you (commander or admin) |
| `/st <x> <z>` | Teleport caller to map coordinates (commander or admin) |

## Admin-only configuration

All save back to JSON automatically.

| Command | Effect |
|--|--|
| `/st on` / `/st off` | Master toggle |
| `/st status` | Live summary (also available to players) |
| `/st charges <N>` | Max charges per team |
| `/st cd <s>` | Recharge time |
| `/st tier <N>` | Minimum tech tier required to use teleporter |
| `/st countdown <s>` | Pre-teleport delay (broadcast warning) |
| `/st radius <m>` | Teleport target radius (the area around the requester where the vehicle lands) |
| `/st mindist <m>` | Minimum distance from enemy HQ/Nest to allow teleport |

## Configuration

`UserData/HeavyTeleporter_cfg/Si_HeavyTeleporter_Config.json` — auto-created on first run with defaults.

Key fields:
- `MaxCharges` — per-team shared pool
- `RechargeTime` — seconds between charge refills
- `TechTier` — minimum tech tier required
- `Countdown` — pre-teleport delay
- `TeleportRadius` — landing area around the requester
- `MinEnemyBaseDistance` — anti-rush guard
- `SoundFile` — path under `UserData/`

## Building from source

```bash
cd Si_HeavyTeleporter
dotnet build -c Release
```

Targets `netstandard2.1`. Reference DLLs in `include/netstandard2.1/`:
- `SilicaCore.dll`, `MelonLoader.dll`, `Si_AdminMod.dll`, `0Harmony.dll`
- `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.PhysicsModule.dll`

Build output: `Si_HeavyTeleporter/bin/Release/netstandard2.1/Si_HeavyTeleporter.dll`.

## License

GPL-3.0 (matches Silica modding ecosystem).
