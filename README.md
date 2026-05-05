# DynamicSites

A free, open-source Carbon plugin for Rust that spawns dynamic PVE combat sites across the map — with custom NPCs, loot crates, decorations, map markers, and optional RP/Economics rewards. A self-contained alternative to paid plugins like Dynamic Monuments.

---

## Features

- **5 built-in difficulty presets** — Easy through Nightmare, each with tuned NPC loadouts, health, aim, and loot
- **Fully configurable** — Every preset, NPC, crate, decoration, and reward is defined in the JSON config
- **Auto-spawn on wipe** — Sites appear automatically when a new map is generated
- **Maintenance timer** — Periodically checks site counts and respawns as needed
- **Site expiry** — Sites despawn after a configurable time (default 2 hours)
- **Map markers** — Colored radius ring + named vending pin shown on every player's map
- **Completion detection** — Broadcasts when all NPCs are cleared; rewards the last killer
- **ServerRewards & Economics support** — Optional RP/coin rewards on site completion
- **Player-placed sites** — Players can throw a skinned supply signal to spawn a site at their location (configurable RP cost)
- **Loot ownership** — Player-placed sites can be locked so only the owner can loot crates
- **Admin commands** — Spawn, remove, list sites from chat or RCON console

---

## Requirements

- [Carbon](https://carbonmod.gg) modding framework (any recent build)
- Rust dedicated server
- Optional: [ServerRewards](https://umod.org/plugins/server-rewards) and/or [Economics](https://umod.org/plugins/economics) for reward integration

---

## Installation

1. Copy `DynamicSites.cs` to your Carbon plugins directory:
   ```
   rust-server/carbon/plugins/DynamicSites.cs
   ```

2. Carbon will compile and load the plugin automatically. On first load it creates the default config at:
   ```
   carbon/configs/DynamicSites.json
   ```

3. Edit the config to adjust presets, NPC loadouts, rewards, and spawn behavior, then run `/ds reload` in-game or restart.

> **File ownership:** The plugin file must be owned by the user running the Rust server (typically `recon` or `rust`), not `root`:
> ```bash
> chown recon:recon carbon/plugins/DynamicSites.cs
> ```

---

## Default Presets

| Name | Difficulty | NPCs | Loot | RP Reward |
|------|-----------|------|------|-----------|
| Scavengers' Den | Easy | 3 bow hunters (burlap) | 3 basic + 1 normal crate | 75 RP |
| Bandit Encampment | Easy | 3 sword rushers + 2 crossbow guards | 2 normal + 2 basic crates | 150 RP |
| Military Outpost | Medium | 3 pump shotgun + 2 semi-auto rifle guards | 3 normal + 2 normal2 + 1 elite crate | 300 RP |
| Research Site | Hard | 3 AK-47 + 3 LR-300 hazmat scientists | 2 elite + 3 normal + 1 tools crate | 700 RP |
| Elite Compound | Nightmare | 4 AK + 3 LR-300 hazmat + 2 chainsaw enforcers | 4 elite + 4 normal + 2 tools crates | 1500 RP |

---

## Commands

### Player
| Command | Description |
|---------|-------------|
| `/ds place` | Spend RP to spawn a site at your location (if supply signal method is disabled) |

### Admin (chat)
| Command | Description |
|---------|-------------|
| `/ds spawn [preset]` | Spawn a site at a random valid location. Omit preset for a random enabled one. |
| `/ds spawnhere [preset]` | Spawn a site at your current position |
| `/ds remove` | Remove the nearest site within 200m |
| `/ds removeall` | Remove all active sites |
| `/ds list` | List all active sites with ID, preset, position, and NPC count |
| `/ds reload` | Reload the config without restarting |

### Admin (RCON/console)
| Command | Description |
|---------|-------------|
| `ds spawn [preset]` | Spawn a site |
| `ds removeall` | Remove all sites |
| `ds list` | List active sites |

---

## Configuration Reference

### Top-level settings

| Key | Default | Description |
|-----|---------|-------------|
| `Auto-spawn on wipe` | `true` | Spawn sites automatically when `OnNewSave` fires |
| `Auto-spawn count` | `2` | Number of sites per enabled preset to maintain |
| `Maintain site count via timer (seconds)` | `900` | How often (in seconds) to check and refill site counts. `0` = disabled |
| `Site expiry time (seconds)` | `7200` | How long a site lasts before auto-removal. `0` = never |
| `Broadcast site spawns to chat` | `true` | Announce new sites to all players |
| `Broadcast completions to chat` | `true` | Announce site clears and reward winners |
| `Minimum distance between sites` | `200` | Sites will not spawn within this distance of each other (meters) |
| `Minimum distance from players on spawn` | `80` | Sites will not spawn within this distance of any online player |
| `Player-placed site: supply signal skin ID` | `0` | Skin ID of the supply signal item that triggers a player-placed site. `0` = disabled |
| `Player-placed site: preset name` | `GuardPost` | Which preset to use for player-placed sites |
| `Player-placed site: ServerRewards cost` | `200` | RP cost to place a site. `0` = free |
| `Player-placed site: lock loot to owner` | `true` | Prevent other players from looting crates in a player-placed site |

### Preset fields

| Field | Type | Description |
|-------|------|-------------|
| `Name` | string | Internal identifier (used in commands) |
| `DisplayName` | string | Shown in map markers and chat broadcasts |
| `Difficulty` | string | Label: `easy`, `medium`, `hard`, `nightmare` (display only) |
| `Color` | string | Hex color for map marker (e.g. `FF4500`) |
| `Radius` | float | Site radius in meters. Controls NPC/crate/decoration spread and map marker ring size. |
| `AutoSpawn` | bool | Whether this preset participates in auto-spawn and maintenance |
| `NPCs` | array | List of NPC groups (see below) |
| `Crates` | array | List of crate types to spawn |
| `Decorations` | array | List of decoration prefab paths placed in a ring around the site |
| `Reward.ServerRewards` | int | RP awarded to the player who kills the last NPC |
| `Reward.Economics` | int | Coin amount awarded on completion (requires Economics plugin) |

### NPC group fields

| Field | Type | Description |
|-------|------|-------------|
| `Prefab` | string | Full prefab path — use `scientist.prefab` for light, `scientistnpc_heavy.prefab` for heavy |
| `Count` | int | How many NPCs to spawn from this group |
| `Health` | float | Starting and max health |
| `AggressionRange` | float | Distance at which the NPC detects and engages players |
| `AimConeScale` | float | Aim accuracy multiplier. `1.0` = default, `2.5` = wide/inaccurate, `0.4` = tight/lethal |
| `Loadout` | array | Weapon shortnames for belt slot. Format: `"rifle.ak"` or `"ammo.rifle:90"` for items with quantity |
| `Wear` | array | Clothing shortnames for wear slot |

### Crate fields

| Field | Type | Description |
|-------|------|-------------|
| `Prefab` | string | Full prefab path for the crate type |
| `Count` | int | How many of this crate to spawn |

---

## Useful Prefab References

### NPC prefabs
```
assets/prefabs/npc/scientist/scientist.prefab                               (light scientist)
assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab (heavy scientist)
```

### Crate prefabs
```
assets/bundled/prefabs/radtown/crate_basic.prefab
assets/bundled/prefabs/radtown/crate_normal.prefab
assets/bundled/prefabs/radtown/crate_normal_2.prefab
assets/bundled/prefabs/radtown/crate_elite.prefab
assets/bundled/prefabs/radtown/crate_tools.prefab
```

### Decoration prefabs
```
assets/prefabs/deployable/barricade/barricade.sandbag.prefab
assets/prefabs/deployable/barricade/barricade.stone.prefab
assets/prefabs/deployable/barricade/barricade.metal.prefab
assets/prefabs/deployable/barricade/barricade.concrete.prefab
assets/prefabs/deployable/search light/searchlight.deployed.prefab
assets/prefabs/deployable/campfire/campfire.prefab
```

---

## Troubleshooting

**Plugin compiles but sites never spawn**

Check Carbon's log for `[DynamicSites]` output. Common issues:
- All presets have `"AutoSpawn": false`
- The server map is fully ocean (unlikely but possible on custom seeds)
- `Auto-spawn on wipe` is `false` — trigger manually with `/ds spawn`

**NPCs spawn with default scientist loadout instead of custom gear**

This happens if the item shortnames in `Loadout` or `Wear` are incorrect. Check Carbon's log for `Unknown item: <shortname>` warnings and correct the shortname.

**"An item with the same key has already been added" in Carbon log**

This is a known Carbon file-watcher bug and is harmless — the plugin still loads on server restart. To hot-reload without restarting: delete the `.cs` file and copy it back fresh (don't edit in place).

**Sites spawn in water or on steep slopes**

The spawn logic includes water and slope checks but may occasionally place a site on marginal terrain depending on map seed. Increase `Minimum distance between sites` to give the random sampler more room to find valid positions.

---

## License

MIT License — free to use, modify, and distribute.
