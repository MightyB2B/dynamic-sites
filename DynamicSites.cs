using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oxide.Plugins
{
    [Info("DynamicSites", "Community", "1.0.0")]
    [Description("Spawns dynamic PVE sites with NPCs, loot, and map markers. Free alternative to Dynamic Monuments.")]
    public class DynamicSites : RustPlugin
    {
        // ─── Plugin references (optional integrations) ─────────────────────────
        [PluginReference] Plugin ServerRewards, Economics;

        // ─── Runtime state ──────────────────────────────────────────────────────
        private Configuration _config;
        private readonly Dictionary<string, ActiveSite> _sites = new();
        private Timer _maintainTimer;
        private int _botIdCounter = 1000000;

        private static readonly int HeightMask = LayerMask.GetMask("Terrain", "World", "Default", "Construction", "Deployed");

        // ═══════════════════════════════════════════════════════════════════════
        // Config
        // ═══════════════════════════════════════════════════════════════════════

        private class Configuration
        {
            [JsonProperty("Auto-spawn on wipe (true/false)")]
            public bool AutoSpawnOnWipe = true;

            [JsonProperty("Auto-spawn count (sites per enabled preset)")]
            public int AutoSpawnCount = 2;

            [JsonProperty("Maintain site count via timer (seconds, 0 = disabled)")]
            public int MaintainInterval = 900;

            [JsonProperty("Site expiry time (seconds, 0 = never)")]
            public int SiteExpiry = 7200;

            [JsonProperty("Broadcast site spawns to chat")]
            public bool BroadcastSpawns = true;

            [JsonProperty("Broadcast completions to chat")]
            public bool BroadcastCompletions = true;

            [JsonProperty("Minimum distance between sites")]
            public float MinSiteSpacing = 200f;

            [JsonProperty("Minimum distance from players on spawn")]
            public float MinPlayerDistance = 80f;

            [JsonProperty("Player-placed site: supply signal skin ID (0 = disabled)")]
            public ulong SupplySignalSkinId = 0;

            [JsonProperty("Player-placed site: preset name")]
            public string PlayerPreset = "GuardPost";

            [JsonProperty("Player-placed site: ServerRewards cost (0 = free)")]
            public int PlayerCost = 200;

            [JsonProperty("Player-placed site: lock loot to owner")]
            public bool LockPlayerSites = true;

            [JsonProperty("Site presets")]
            public List<SitePreset> Presets = BuildDefaultPresets();

            static List<SitePreset> BuildDefaultPresets() => new()
            {
                // ── 1. EASY — Scavengers' Den ─────────────────────────────────────
                // Bow hunters in burlap. Poor aim — telegraphs damage, forgiving for new players.
                new SitePreset
                {
                    Name        = "ScavengersDen",
                    DisplayName = "Scavengers' Den",
                    Difficulty  = "easy",
                    Color       = "00AA44",
                    Radius      = 18f,
                    AutoSpawn   = true,
                    NPCs = new()
                    {
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 3,
                            Health          = 80f,
                            AggressionRange = 35f,
                            AimConeScale    = 2.5f,
                            Loadout         = new() { "bow.hunting", "arrow.wooden:50" },
                            Wear            = new() { "burlap.headwrap", "burlap.shirt", "burlap.trousers", "burlap.shoes" }
                        }
                    },
                    Crates = new()
                    {
                        new CrateDef { Prefab = Prefabs.CrateBasic,  Count = 3 },
                        new CrateDef { Prefab = Prefabs.CrateNormal, Count = 1 }
                    },
                    Decorations = new() { Prefabs.BarricadeSandbag, Prefabs.Campfire },
                    Reward = new RewardDef { ServerRewards = 75 }
                },

                // ── 2. EASY — Bandit Encampment ───────────────────────────────────
                // Sword rushers + crossbow guards. Mix of melee and ranged threat.
                new SitePreset
                {
                    Name        = "BanditEncampment",
                    DisplayName = "Bandit Encampment",
                    Difficulty  = "easy",
                    Color       = "00FF00",
                    Radius      = 22f,
                    AutoSpawn   = true,
                    NPCs = new()
                    {
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 3,
                            Health          = 110f,
                            AggressionRange = 40f,
                            AimConeScale    = 1.0f,
                            Loadout         = new() { "salvaged.sword" },
                            Wear            = new() { "bone.armor.suit", "burlap.headwrap", "burlap.shoes" }
                        },
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 2,
                            Health          = 110f,
                            AggressionRange = 42f,
                            AimConeScale    = 2.0f,
                            Loadout         = new() { "crossbow", "arrow.wooden:40" },
                            Wear            = new() { "burlap.shirt", "burlap.trousers", "burlap.shoes" }
                        }
                    },
                    Crates = new()
                    {
                        new CrateDef { Prefab = Prefabs.CrateNormal, Count = 2 },
                        new CrateDef { Prefab = Prefabs.CrateBasic,  Count = 2 }
                    },
                    Decorations = new() { Prefabs.BarricadeSandbag, Prefabs.BarricadeStone, Prefabs.Campfire },
                    Reward = new RewardDef { ServerRewards = 150 }
                },

                // ── 3. MEDIUM — Military Outpost ──────────────────────────────────
                // Pump shotguns + semi-auto rifles. Road sign armor. Real guns now.
                new SitePreset
                {
                    Name        = "MilitaryOutpost",
                    DisplayName = "Military Outpost",
                    Difficulty  = "medium",
                    Color       = "0080FF",
                    Radius      = 28f,
                    AutoSpawn   = true,
                    NPCs = new()
                    {
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 3,
                            Health          = 160f,
                            AggressionRange = 50f,
                            AimConeScale    = 1.2f,
                            Loadout         = new() { "shotgun.pump", "ammo.shotgun:32" },
                            Wear            = new() { "roadsign.jacket", "roadsign.kilt", "metal.facemask" }
                        },
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistHeavy,
                            Count           = 2,
                            Health          = 240f,
                            AggressionRange = 55f,
                            AimConeScale    = 1.0f,
                            Loadout         = new() { "rifle.semiauto", "ammo.rifle:60" },
                            Wear            = new() { "metal.plate.torso", "roadsign.kilt", "metal.facemask" }
                        }
                    },
                    Crates = new()
                    {
                        new CrateDef { Prefab = Prefabs.CrateNormal,  Count = 3 },
                        new CrateDef { Prefab = Prefabs.CrateNormal2, Count = 2 },
                        new CrateDef { Prefab = Prefabs.CrateElite,   Count = 1 }
                    },
                    Decorations = new() { Prefabs.BarricadeMetal, Prefabs.BarricadeConcrete, Prefabs.Searchlight, Prefabs.Campfire },
                    Reward = new RewardDef { ServerRewards = 300 }
                },

                // ── 4. HARD — Research Site ───────────────────────────────────────
                // AK-47 / LR-300. Full metal + hazmat. Good aim — punishing to fight.
                new SitePreset
                {
                    Name        = "ScientistLab",
                    DisplayName = "Research Site",
                    Difficulty  = "hard",
                    Color       = "FF4500",
                    Radius      = 25f,
                    AutoSpawn   = true,
                    NPCs = new()
                    {
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 3,
                            Health          = 220f,
                            AggressionRange = 55f,
                            AimConeScale    = 0.7f,
                            Loadout         = new() { "rifle.ak", "ammo.rifle:90" },
                            Wear            = new() { "metal.plate.torso", "metal.facemask", "roadsign.kilt" }
                        },
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistHeavy,
                            Count           = 3,
                            Health          = 320f,
                            AggressionRange = 55f,
                            AimConeScale    = 0.7f,
                            Loadout         = new() { "rifle.lr300", "ammo.rifle:90" },
                            Wear            = new() { "hazmatsuit" }
                        }
                    },
                    Crates = new()
                    {
                        new CrateDef { Prefab = Prefabs.CrateElite,  Count = 2 },
                        new CrateDef { Prefab = Prefabs.CrateNormal, Count = 3 },
                        new CrateDef { Prefab = Prefabs.CrateTools,  Count = 1 }
                    },
                    Decorations = new() { Prefabs.BarricadeMetal, Prefabs.BarricadeStone, Prefabs.Searchlight },
                    Reward = new RewardDef { ServerRewards = 700 }
                },

                // ── 5. NIGHTMARE — Elite Compound ─────────────────────────────────
                // AK/LR-300 soldiers with near-perfect aim + chainsaw enforcers.
                // Expect to die if you rush in.
                new SitePreset
                {
                    Name        = "EliteCompound",
                    DisplayName = "Elite Compound",
                    Difficulty  = "nightmare",
                    Color       = "FF00FF",
                    Radius      = 35f,
                    AutoSpawn   = true,
                    NPCs = new()
                    {
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistHeavy,
                            Count           = 4,
                            Health          = 450f,
                            AggressionRange = 70f,
                            AimConeScale    = 0.4f,
                            Loadout         = new() { "rifle.ak", "ammo.rifle:120" },
                            Wear            = new() { "hazmatsuit" }
                        },
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistHeavy,
                            Count           = 3,
                            Health          = 480f,
                            AggressionRange = 70f,
                            AimConeScale    = 0.4f,
                            Loadout         = new() { "rifle.lr300", "ammo.rifle:120" },
                            Wear            = new() { "hazmatsuit" }
                        },
                        new NpcDef
                        {
                            Prefab          = Prefabs.ScientistLight,
                            Count           = 2,
                            Health          = 350f,
                            AggressionRange = 60f,
                            AimConeScale    = 1.0f,
                            Loadout         = new() { "chainsaw" },
                            Wear            = new() { "metal.plate.torso", "metal.facemask", "roadsign.kilt" }
                        }
                    },
                    Crates = new()
                    {
                        new CrateDef { Prefab = Prefabs.CrateElite,  Count = 4 },
                        new CrateDef { Prefab = Prefabs.CrateNormal, Count = 4 },
                        new CrateDef { Prefab = Prefabs.CrateTools,  Count = 2 }
                    },
                    Decorations = new() { Prefabs.BarricadeMetal, Prefabs.BarricadeConcrete, Prefabs.Searchlight, Prefabs.Campfire },
                    Reward = new RewardDef { ServerRewards = 1500 }
                }
            };
        }

        private class SitePreset
        {
            public string Name;
            public string DisplayName;
            public string Difficulty;
            public string Color;
            public float  Radius     = 20f;
            public bool   AutoSpawn  = true;
            public List<NpcDef>   NPCs        = new();
            public List<CrateDef> Crates      = new();
            public List<string>   Decorations = new();
            public RewardDef      Reward      = new();
        }

        private class NpcDef
        {
            public string Prefab;
            public int    Count           = 3;
            public float  Health          = 150f;
            public float  AggressionRange = 45f;
            public float  AimConeScale    = 1.0f;
            public List<string> Loadout   = new();
            public List<string> Wear      = new();
        }

        private class CrateDef
        {
            public string Prefab;
            public int    Count = 1;
        }

        private class RewardDef
        {
            public int ServerRewards = 0;
            public int Economics     = 0;
        }

        private static class Prefabs
        {
            public const string ScientistLight  = "assets/prefabs/npc/scientist/scientist.prefab";
            public const string ScientistHeavy  = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
            public const string CrateNormal     = "assets/bundled/prefabs/radtown/crate_normal.prefab";
            public const string CrateNormal2    = "assets/bundled/prefabs/radtown/crate_normal_2.prefab";
            public const string CrateElite      = "assets/bundled/prefabs/radtown/crate_elite.prefab";
            public const string CrateBasic      = "assets/bundled/prefabs/radtown/crate_basic.prefab";
            public const string CrateTools      = "assets/bundled/prefabs/radtown/crate_tools.prefab";
            public const string BarricadeSandbag = "assets/prefabs/deployable/barricade/barricade.sandbag.prefab";
            public const string BarricadeStone   = "assets/prefabs/deployable/barricade/barricade.stone.prefab";
            public const string BarricadeMetal   = "assets/prefabs/deployable/barricade/barricade.metal.prefab";
            public const string BarricadeConcrete = "assets/prefabs/deployable/barricade/barricade.concrete.prefab";
            public const string Searchlight      = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
            public const string Campfire         = "assets/prefabs/deployable/campfire/campfire.prefab";
            public const string VendingMarker    = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
            public const string RadiusMarker     = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Runtime site data
        // ═══════════════════════════════════════════════════════════════════════

        private class ActiveSite
        {
            public string     Id;
            public string     PresetName;
            public Vector3    Center;
            public ulong      OwnerId;          // 0 = server-spawned
            public float      SpawnTime;
            public HashSet<ulong> NpcIds   = new();
            public HashSet<ulong> CrateIds = new();
            public List<BaseEntity> Entities = new();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Custom NPC components  (mirrors DangerousTreasures / RaidableBases)
        // ═══════════════════════════════════════════════════════════════════════

        public class SiteNPC : ScientistNPC
        {
            public string SiteId;
            public override string Categorize()           => "SiteNPC";
            public override bool   ShouldDropActiveItem() => false;
        }

        public class SiteBrain : ScientistBrain
        {
            public string        SiteId;
            public Vector3       HomePosition;
            public List<Vector3> RoamPositions = new();

            // Stored separately so InitializeAI can re-apply after base resets them
            private float _aggroRange;
            private float _targetLostRange;

            private static void CopySerializable<T>(T src, T dst)
            {
                foreach (var f in typeof(T).GetFields())
                {
                    if (!f.IsStatic) f.SetValue(dst, f.GetValue(src));
                }
            }

            public static bool TryCreate(string prefabPath, Vector3 position, string siteId,
                                          float health, float aggroRange, List<Vector3> roamPos,
                                          out SiteBrain brain, out SiteNPC npc)
            {
                brain = null; npc = null;

                var prefab = GameManager.server.FindPrefab(prefabPath);
                if (prefab == null) return false;

                var go = Facepunch.Instantiate.GameObject(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                go.SetActive(false);
                go.name = prefabPath;

                var origNpc   = go.GetComponent<ScientistNPC>();
                var origBrain = go.GetComponent<ScientistBrain>();

                npc   = go.AddComponent<SiteNPC>();
                brain = go.AddComponent<SiteBrain>();

                if (origNpc   != null) { CopySerializable(origNpc,   npc);   DestroyImmediate(origNpc,   true); }
                if (origBrain != null) { CopySerializable(origBrain, brain); DestroyImmediate(origBrain, true); }

                npc.Brain         = brain;
                npc.SiteId        = siteId;
                brain.SiteId      = siteId;
                brain.HomePosition = position;
                brain.RoamPositions = roamPos;
                brain._baseEntity  = npc;
                brain._aggroRange       = aggroRange;
                brain._targetLostRange  = aggroRange * 1.25f;

                // Set initial values; InitializeAI will re-apply after base may reset them
                brain.SenseRange      = aggroRange;
                brain.TargetLostRange = aggroRange * 1.25f;

                npc.startHealth = health;

                SceneManager.MoveGameObjectToScene(go, Rust.Server.EntityScene);
                go.SetActive(true);

                return npc != null;
            }

            // base.InitializeAI() resets SenseRange to the prefab default, so re-apply ours after.
            public override void InitializeAI()
            {
                base.InitializeAI();
                SenseRange      = _aggroRange;
                TargetLostRange = _targetLostRange;
                if (Senses != null) Senses.targetLostRange = _targetLostRange;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Oxide hooks
        // ═══════════════════════════════════════════════════════════════════════

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try   { _config = Config.ReadObject<Configuration>(); }
            catch { _config = new Configuration(); }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        protected override void LoadDefaultConfig() => _config = new Configuration();

        private void OnServerInitialized()
        {
            if (_config.AutoSpawnOnWipe) AutoSpawnAll();
            StartMaintainTimer();
        }

        private void OnNewSave(string _)
        {
            RemoveAllSites();
            if (_config.AutoSpawnOnWipe)
                NextTick(AutoSpawnAll);
        }

        private void Unload() => RemoveAllSites();

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            var id = entity.net?.ID.Value ?? 0UL;
            if (id == 0UL) return;

            foreach (var site in _sites.Values.ToList())
            {
                if (!site.NpcIds.Contains(id)) continue;
                site.NpcIds.Remove(id);
                if (site.NpcIds.Count == 0)
                    OnSiteCleared(site, info?.InitiatorPlayer);
                return;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (!_config.LockPlayerSites || player == null || container?.net == null) return null;
            var id = container.net.ID.Value;
            foreach (var site in _sites.Values)
            {
                if (site.OwnerId == 0UL || !site.CrateIds.Contains(id)) continue;
                if (player.userID == site.OwnerId) return null;
                SendMsg(player, "<color=#FF4500>This site belongs to another player.</color>");
                return false;
            }
            return null;
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            if (_config.SupplySignalSkinId == 0) return;
            if (entity is not SupplySignal signal) return;
            if (signal.skinID != _config.SupplySignalSkinId) return;

            // Intercept this supply signal and use it for site placement
            NextTick(() => { if (!signal.IsDestroyed) signal.Kill(); });
            TrySpawnPlayerSite(player);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Commands
        // ═══════════════════════════════════════════════════════════════════════

        [ChatCommand("ds")]
        private void CmdDS(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin)
            {
                // Non-admin: only /ds place
                if (args.Length > 0 && args[0] == "place") TrySpawnPlayerSite(player);
                else SendMsg(player, "Usage: <color=#FFD700>/ds place</color>");
                return;
            }
            AdminCommand(player, args);
        }

        [ConsoleCommand("ds")]
        private void ConsoleDS(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) { arg.ReplyWith("Admin only."); return; }
            var args = arg.Args ?? Array.Empty<string>();
            string sub = args.Length > 0 ? args[0].ToLower() : "help";
            switch (sub)
            {
                case "spawn":
                    string preset = args.Length > 1 ? args[1] : null;
                    SpawnSite(preset, null, 0UL);
                    break;
                case "removeall":
                    RemoveAllSites();
                    arg.ReplyWith("All sites removed.");
                    break;
                case "list":
                    ListSites(arg);
                    break;
                default:
                    arg.ReplyWith("ds spawn [preset] | ds removeall | ds list");
                    break;
            }
        }

        private void AdminCommand(BasePlayer player, string[] args)
        {
            string sub = args.Length > 0 ? args[0].ToLower() : "help";
            switch (sub)
            {
                case "spawn":
                    string preset = args.Length > 1 ? args[1] : null;
                    SpawnSite(preset, null, 0UL);
                    break;
                case "spawnhere":
                    string presetHere = args.Length > 1 ? args[1] : null;
                    SpawnSite(presetHere, player.transform.position, 0UL);
                    break;
                case "remove":
                    RemoveSiteNearPlayer(player);
                    break;
                case "removeall":
                    int count = _sites.Count;
                    RemoveAllSites();
                    SendMsg(player, $"Removed <color=#FFD700>{count}</color> sites.");
                    break;
                case "list":
                    ListSites(player);
                    break;
                case "reload":
                    LoadConfig();
                    SendMsg(player, "Config reloaded.");
                    break;
                default:
                    SendMsg(player,
                        "<color=#FFD700>/ds spawn [preset]</color> — random spawn\n" +
                        "<color=#FFD700>/ds spawnhere [preset]</color> — spawn at your position\n" +
                        "<color=#FFD700>/ds remove</color> — remove nearest site\n" +
                        "<color=#FFD700>/ds removeall</color> — remove all sites\n" +
                        "<color=#FFD700>/ds list</color> — list active sites");
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Spawn logic
        // ═══════════════════════════════════════════════════════════════════════

        private void AutoSpawnAll()
        {
            foreach (var preset in _config.Presets.Where(p => p.AutoSpawn))
            {
                int existing = _sites.Values.Count(s => s.PresetName == preset.Name);
                int needed   = _config.AutoSpawnCount - existing;
                for (int i = 0; i < needed; i++)
                    SpawnSite(preset.Name, null, 0UL);
            }
        }

        private void StartMaintainTimer()
        {
            _maintainTimer?.Destroy();
            if (_config.MaintainInterval <= 0) return;
            _maintainTimer = timer.Every(_config.MaintainInterval, () =>
            {
                PruneExpiredSites();
                AutoSpawnAll();
            });
        }

        private void PruneExpiredSites()
        {
            if (_config.SiteExpiry <= 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (var siteId in _sites.Keys.ToList())
            {
                var site = _sites[siteId];
                if (now - site.SpawnTime > _config.SiteExpiry)
                    RemoveSite(siteId);
            }
        }

        private void SpawnSite(string presetName, Vector3? forcedPos, ulong ownerId)
        {
            SitePreset preset;
            if (string.IsNullOrEmpty(presetName))
            {
                var candidates = _config.Presets.Where(p => p.AutoSpawn).ToList();
                if (candidates.Count == 0) return;
                preset = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
            else
            {
                preset = _config.Presets.FirstOrDefault(p =>
                    string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                if (preset == null) { Puts($"Preset '{presetName}' not found."); return; }
            }

            Vector3 pos = forcedPos ?? FindSpawnPosition(preset);
            if (pos == Vector3.zero) { Puts($"Could not find spawn position for {preset.Name}."); return; }
            pos.y = GetGroundHeight(pos);

            var site = new ActiveSite
            {
                Id         = Guid.NewGuid().ToString("N").Substring(0, 8),
                PresetName = preset.Name,
                Center     = pos,
                OwnerId    = ownerId,
                SpawnTime  = Time.realtimeSinceStartup
            };

            SpawnDecorations(preset, site);
            SpawnCrates(preset, site);
            SpawnNPCs(preset, site);
            SpawnMarkers(preset, site);

            _sites[site.Id] = site;

            if (_config.BroadcastSpawns && ownerId == 0UL)
            {
                string diff  = preset.Difficulty?.ToUpper() ?? "SITE";
                string color = $"#{preset.Color}";
                BroadcastAll($"<color={color}>[{diff}]</color> <color=#FFD700>{preset.DisplayName}</color> has appeared! Check your map.");
            }

            if (_config.SiteExpiry > 0)
                timer.Once(_config.SiteExpiry, () => { if (_sites.ContainsKey(site.Id)) RemoveSite(site.Id); });
        }

        private void SpawnNPCs(SitePreset preset, ActiveSite site)
        {
            var roamPositions = BuildRoamPositions(site.Center, preset.Radius);

            foreach (var def in preset.NPCs)
            {
                for (int i = 0; i < def.Count; i++)
                {
                    var spawnPos = RandomNavPosition(site.Center, preset.Radius * 0.85f);
                    if (spawnPos == Vector3.zero) spawnPos = RingPosition(site.Center, preset.Radius * 0.5f, i, def.Count);
                    spawnPos.y = GetGroundHeight(spawnPos) + 0.1f;

                    if (!SiteBrain.TryCreate(def.Prefab, spawnPos, site.Id,
                                              def.Health, def.AggressionRange,
                                              roamPositions, out var brain, out var npc))
                        continue;

                    ulong userId = (ulong)System.Threading.Interlocked.Increment(ref _botIdCounter);
                    npc.userID       = userId;
                    npc.UserIDString = userId.ToString();
                    npc.loadouts     = new PlayerInventoryProperties[0];

                    npc.Spawn();
                    npc.InitializeHealth(def.Health, def.Health);
                    npc.aimConeScale = def.AimConeScale;

                    bool hasCustomGear = def.Loadout.Count > 0 || def.Wear.Count > 0;
                    if (hasCustomGear)
                    {
                        var capturedNpc = npc;
                        var capturedDef = def;
                        NextTick(() =>
                        {
                            if (capturedNpc == null || capturedNpc.IsDestroyed) return;
                            capturedNpc.inventory.Strip();
                            foreach (var entry in capturedDef.Loadout)
                            {
                                var (shortname, amount) = ParseLoadoutEntry(entry);
                                var item = ItemManager.CreateByName(shortname, amount);
                                if (item == null) { Puts($"[DynamicSites] Unknown item: {shortname}"); continue; }
                                var dest = item.info.category == ItemCategory.Weapon || item.info.category == ItemCategory.Tool
                                    ? capturedNpc.inventory.containerBelt
                                    : capturedNpc.inventory.containerMain;
                                item.MoveToContainer(dest);
                            }
                            foreach (var entry in capturedDef.Wear)
                            {
                                var (shortname, amount) = ParseLoadoutEntry(entry);
                                var item = ItemManager.CreateByName(shortname, amount);
                                if (item == null) continue;
                                item.MoveToContainer(capturedNpc.inventory.containerWear);
                            }
                        });
                    }
                    else
                    {
                        npc.CancelInvoke(npc.EquipTest);
                    }

                    BasePlayer.bots.Remove(npc);

                    site.NpcIds.Add(npc.net.ID.Value);
                    site.Entities.Add(npc);
                }
            }
        }

        private void SpawnCrates(SitePreset preset, ActiveSite site)
        {
            int totalCrates = preset.Crates.Sum(c => c.Count);
            int idx = 0;
            foreach (var def in preset.Crates)
            {
                for (int i = 0; i < def.Count; i++)
                {
                    var pos = RingPosition(site.Center, preset.Radius * 0.45f, idx++, totalCrates);
                    pos.y = GetGroundHeight(pos) + 0.05f;

                    var entity = GameManager.server.CreateEntity(def.Prefab, pos,
                        Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                    if (entity == null) continue;
                    entity.Spawn();

                    if (entity is StorageContainer sc)
                    {
                        site.CrateIds.Add(sc.net.ID.Value);
                        site.Entities.Add(sc);
                    }
                }
            }
        }

        private void SpawnDecorations(SitePreset preset, ActiveSite site)
        {
            for (int i = 0; i < preset.Decorations.Count; i++)
            {
                var pos = RingPosition(site.Center, preset.Radius * 0.7f, i, preset.Decorations.Count);
                pos.y = GetGroundHeight(pos) + 0.05f;

                var entity = GameManager.server.CreateEntity(preset.Decorations[i], pos,
                    Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                if (entity == null) continue;
                entity.Spawn();
                site.Entities.Add(entity);
            }
        }

        private void SpawnMarkers(SitePreset preset, ActiveSite site)
        {
            var markerPos = site.Center + Vector3.up * 0.1f;
            var color     = HexColor(preset.Color);

            // Named vending pin
            var vending = GameManager.server.CreateEntity(Prefabs.VendingMarker, markerPos) as VendingMachineMapMarker;
            if (vending != null)
            {
                vending.enabled        = false;
                vending.markerShopName = preset.DisplayName;
                vending.Spawn();
                site.Entities.Add(vending);
            }

            // Colored radius ring
            var radius = GameManager.server.CreateEntity(Prefabs.RadiusMarker, markerPos) as MapMarkerGenericRadius;
            if (radius != null)
            {
                radius.alpha  = 0.75f;
                radius.color1 = color;
                radius.color2 = new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.4f);
                radius.radius = Mathf.Clamp(preset.Radius / 145f, 0.05f, 0.5f);
                radius.Spawn();
                radius.SendUpdate();
                site.Entities.Add(radius);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Site management
        // ═══════════════════════════════════════════════════════════════════════

        private void OnSiteCleared(ActiveSite site, BasePlayer killer)
        {
            if (!_config.BroadcastCompletions) return;
            var preset = _config.Presets.FirstOrDefault(p => p.Name == site.PresetName);
            if (preset == null) return;

            string color = $"#{preset.Color}";
            string who   = killer?.displayName ?? "someone";
            BroadcastAll($"<color={color}>[CLEARED] {preset.DisplayName}</color> was cleared by <color=#FFD700>{who}</color>!");

            if (preset.Reward.ServerRewards > 0 && killer != null)
            {
                ServerRewards?.Call("AddPoints", killer.userID, preset.Reward.ServerRewards);
                SendMsg(killer, $"<color=#FFD700>+{preset.Reward.ServerRewards} RP</color> for clearing {preset.DisplayName}!");
            }
            if (preset.Reward.Economics > 0 && killer != null)
                Economics?.Call("Deposit", killer.UserIDString, (double)preset.Reward.Economics);

            // Remove markers immediately on completion, leave crates for looting
            foreach (var e in site.Entities.ToList())
            {
                if (e == null || e.IsDestroyed) continue;
                if (e is VendingMachineMapMarker || e is MapMarkerGenericRadius)
                {
                    e.Kill();
                    site.Entities.Remove(e);
                }
            }
        }

        private void RemoveSite(string siteId)
        {
            if (!_sites.TryGetValue(siteId, out var site)) return;
            foreach (var e in site.Entities)
            {
                if (e == null || e.IsDestroyed) continue;
                e.Kill();
            }
            _sites.Remove(siteId);
        }

        private void RemoveAllSites()
        {
            foreach (var id in _sites.Keys.ToList()) RemoveSite(id);
        }

        private void RemoveSiteNearPlayer(BasePlayer player)
        {
            ActiveSite nearest = null;
            float      minDist = float.MaxValue;
            foreach (var site in _sites.Values)
            {
                float d = Vector3.Distance(player.transform.position, site.Center);
                if (d < minDist) { minDist = d; nearest = site; }
            }
            if (nearest == null || minDist > 200f)
            {
                SendMsg(player, "No site within 200m.");
                return;
            }
            RemoveSite(nearest.Id);
            SendMsg(player, $"Removed site <color=#FFD700>{nearest.Id}</color>.");
        }

        private void TrySpawnPlayerSite(BasePlayer player)
        {
            if (ServerRewards != null && _config.PlayerCost > 0)
            {
                object balance = ServerRewards.Call("CheckPoints", player.userID);
                int rp = balance is int i ? i : 0;
                if (rp < _config.PlayerCost)
                {
                    SendMsg(player, $"<color=#FF4500>You need {_config.PlayerCost} RP (you have {rp}).</color>");
                    return;
                }
                ServerRewards.Call("TakePoints", player.userID, _config.PlayerCost);
            }
            SpawnSite(_config.PlayerPreset, player.transform.position, player.userID);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Position helpers
        // ═══════════════════════════════════════════════════════════════════════

        private Vector3 FindSpawnPosition(SitePreset preset)
        {
            int mapHalf = ConVar.Server.worldsize / 2 - 150;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float x = UnityEngine.Random.Range(-mapHalf, mapHalf);
                float z = UnityEngine.Random.Range(-mapHalf, mapHalf);
                var   pos = new Vector3(x, 0f, z);
                pos.y = GetGroundHeight(pos);

                if (IsInWater(pos))          continue;
                if (IsSteepSlope(pos))        continue;
                if (IsNearPlayer(pos))        continue;
                if (IsNearSite(pos))          continue;
                if (IsInsideMonument(pos))    continue;

                return pos;
            }
            return Vector3.zero;
        }

        private float GetGroundHeight(Vector3 pos)
        {
            float terrainH = TerrainMeta.HeightMap.GetHeight(pos);
            if (Physics.Raycast(pos.WithY(terrainH + 200f), Vector3.down, out var hit, 300f, HeightMask))
                return Mathf.Max(hit.point.y, terrainH);
            return terrainH;
        }

        private bool IsInWater(Vector3 pos)
            => WaterLevel.Test(pos, true, true, null)
            || TerrainMeta.WaterMap.GetHeight(pos) - TerrainMeta.HeightMap.GetHeight(pos) > 0.1f;

        private bool IsSteepSlope(Vector3 pos, float maxAngle = 28f)
        {
            if (Physics.Raycast(new Ray(pos + Vector3.up * 10f, Vector3.down), out var hit, 20f, HeightMask))
                return Vector3.Angle(hit.normal, Vector3.up) > maxAngle;
            return false;
        }

        private bool IsNearPlayer(Vector3 pos)
        {
            foreach (var p in BasePlayer.activePlayerList)
                if (Vector3.Distance(p.transform.position, pos) < _config.MinPlayerDistance) return true;
            return false;
        }

        private bool IsNearSite(Vector3 pos)
        {
            foreach (var s in _sites.Values)
                if (Vector3.Distance(s.Center, pos) < _config.MinSiteSpacing) return true;
            return false;
        }

        private bool IsInsideMonument(Vector3 pos)
        {
            foreach (var mon in TerrainMeta.Path.Monuments)
                if (mon.IsInBounds(pos)) return true;
            return false;
        }

        private Vector3 RingPosition(Vector3 center, float radius, int index, int total)
        {
            float angle  = (360f / Mathf.Max(total, 1)) * index + UnityEngine.Random.Range(-15f, 15f);
            float jitter = UnityEngine.Random.Range(0.7f, 1.0f);
            float x = center.x + Mathf.Sin(angle * Mathf.Deg2Rad) * radius * jitter;
            float z = center.z + Mathf.Cos(angle * Mathf.Deg2Rad) * radius * jitter;
            return new Vector3(x, center.y, z);
        }

        private Vector3 RandomNavPosition(Vector3 center, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                var candidate = center + UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(3f, radius);
                candidate.y = GetGroundHeight(candidate);
                if (NavMesh.SamplePosition(candidate, out var hit, 25f, NavMesh.AllAreas))
                    return hit.position;
            }
            return Vector3.zero;
        }

        private List<Vector3> BuildRoamPositions(Vector3 center, float radius)
        {
            var positions = new List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                var p = RandomNavPosition(center, radius * 0.8f);
                if (p != Vector3.zero) positions.Add(p);
            }
            if (positions.Count == 0) positions.Add(center);
            return positions;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Utilities
        // ═══════════════════════════════════════════════════════════════════════

        private Color HexColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var c)) return c;
            return Color.white;
        }

        private void ListSites(BasePlayer player)
        {
            if (_sites.Count == 0) { SendMsg(player, "No active sites."); return; }
            SendMsg(player, $"<color=#FFD700>Active sites ({_sites.Count}):</color>");
            foreach (var s in _sites.Values)
                SendMsg(player, $"  <color=#00FF7F>{s.Id}</color> {s.PresetName} @ {s.Center} | NPCs: {s.NpcIds.Count}");
        }

        private void ListSites(ConsoleSystem.Arg arg)
        {
            if (_sites.Count == 0) { arg.ReplyWith("No active sites."); return; }
            arg.ReplyWith($"Active sites ({_sites.Count}):");
            foreach (var s in _sites.Values)
                arg.ReplyWith($"  {s.Id} | {s.PresetName} | {s.Center} | NPCs alive: {s.NpcIds.Count}");
        }

        private static (string name, int amount) ParseLoadoutEntry(string entry)
        {
            int sep = entry.IndexOf(':');
            if (sep < 0) return (entry, 1);
            return int.TryParse(entry.Substring(sep + 1), out int n)
                ? (entry.Substring(0, sep), n)
                : (entry, 1);
        }

        private void BroadcastAll(string msg)
        {
            Server.Broadcast(msg);
            Puts(msg.Replace("<color=#", "").Replace(">", " ").Replace("</color>", ""));
        }

        private void SendMsg(BasePlayer player, string msg)
            => player.ChatMessage($"<color=#FFD700>[DynamicSites]</color> {msg}");
    }
}
