/*
 Si_HeavyTeleporter - v1.2.0

 Super weapon for Sol and Centauri: at configurable tech tier, teleport
 vehicles from the Ultra Heavy Factory to a player's position.
 Teleport zone = area around the factory's production waypoint
 (where built vehicles drive to).

 Player commands:
   /st              — request nearest eligible vehicle teleported to you
   /st <playername> — commander/admin: teleport vehicle to that player
   /st <x> <z>     — commander/admin: teleport to map coordinates
   /st status       — show current settings

 Admin commands:
   /st on|off       — toggle
   /st charges N    — max charges per volley
   /st cd N         — recharge time in seconds
   /st tier N       — tech tier required
   /st countdown N  — seconds before teleport executes
   /st radius N     — teleport pickup zone radius
*/

using MelonLoader;
using Newtonsoft.Json;
using SilicaAdminMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[assembly: MelonInfo(typeof(Si_HeavyTeleporter.HeavyTeleporter), "Heavy Teleporter", "1.2.0", "schwe")]
[assembly: MelonGame("Bohemia Interactive", "Silica")]

namespace Si_HeavyTeleporter
{
    public class HeavyTeleporter : MelonMod
    {
        // --- Config JSON ---
        class TeleporterConfig
        {
            public bool Enabled = true;
            public int TechTier = 8;
            public int MaxCharges = 5;
            public float RechargeTime = 300f;
            public float RechargeAnnounceInterval = 60f;
            public float TeleportRadius = 30f;
            public float Countdown = 3f;
            public string SoundFile = "sounds/cannon_boom.wav";
            public float MinEnemyBaseDistance = 800f;
        }

        static string _configPath = "";

        // --- Config (runtime) ---
        static bool _enabled = true;
        static int TechTier = 8;
        static int MaxCharges = 5;
        static float RechargeTime = 300f;
        static float RechargeAnnounceInterval = 60f;
        static float TeleportRadius = 30f;
        static float Countdown = 3f;
        static string SoundFile = "sounds/cannon_boom.wav";
        static float MinEnemyBaseDistance = 800f;

        // --- Per-team runtime state ---
        class TeamState
        {
            public int Charges;
            public bool Ready;
            public bool Announced;
            public bool Recharging;
            public float RechargeTimer;
            public float LastRechargeAnnounce;
        }
        static readonly Dictionary<int, TeamState> _teamStates = new Dictionary<int, TeamState>();

        static TeamState GetTeamState(Team team)
        {
            int tid = team.GetInstanceID();
            if (!_teamStates.TryGetValue(tid, out var state))
            {
                state = new TeamState();
                _teamStates[tid] = state;
            }
            return state;
        }

        // --- Vehicle selection menu: playerId -> list of candidates ---
        class VehicleCandidate
        {
            public Vehicle Vehicle;
            public string Name;
            public string FactoryLabel;
        }
        static readonly Dictionary<int, List<VehicleCandidate>> _vehicleMenus = new Dictionary<int, List<VehicleCandidate>>();

        // --- Pending teleport requests ---
        class TeleportRequest
        {
            public Player Requester;
            public Player Target;
            public Vector3 TargetPosition;
            public bool UseCoords;
            public float RequestTime;
            public int LastAnnounced;
            public Vehicle ChosenVehicle;  // specific vehicle selected by player
        }
        static readonly Dictionary<int, TeleportRequest> _pendingRequests = new Dictionary<int, TeleportRequest>();

        // --- Config persistence ---
        static void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var cfg = JsonConvert.DeserializeObject<TeleporterConfig>(File.ReadAllText(_configPath));
                    if (cfg != null)
                    {
                        _enabled = cfg.Enabled;
                        TechTier = cfg.TechTier;
                        MaxCharges = cfg.MaxCharges;
                        RechargeTime = cfg.RechargeTime;
                        RechargeAnnounceInterval = cfg.RechargeAnnounceInterval;
                        TeleportRadius = cfg.TeleportRadius;
                        Countdown = cfg.Countdown;
                        SoundFile = cfg.SoundFile;
                        MinEnemyBaseDistance = cfg.MinEnemyBaseDistance;
                        MelonLogger.Msg("HeavyTeleporter: Config loaded from " + _configPath);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("HeavyTeleporter: Failed to load config: " + ex.Message);
            }
            SaveConfig();
            MelonLogger.Msg("HeavyTeleporter: Default config saved to " + _configPath);
        }

        static void SaveConfig()
        {
            try
            {
                var cfg = new TeleporterConfig
                {
                    Enabled = _enabled, TechTier = TechTier, MaxCharges = MaxCharges,
                    RechargeTime = RechargeTime, RechargeAnnounceInterval = RechargeAnnounceInterval,
                    TeleportRadius = TeleportRadius, Countdown = Countdown, SoundFile = SoundFile,
                    MinEnemyBaseDistance = MinEnemyBaseDistance
                };
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("HeavyTeleporter: Failed to save config: " + ex.Message);
            }
        }

        public override void OnInitializeMelon()
        {
            _configPath = Path.Combine("UserData", "HeavyTeleporter_Config.json");
            LoadConfig();
            MelonLogger.Msg("Heavy Teleporter v1.2.0 loaded! (Sol + Centauri)");
            GameEvents.OnGameEnded += OnGameEnded;
        }

        public override void OnLateInitializeMelon()
        {
            PlayerMethods.RegisterPlayerCommand("st", OnStCommand, true);
            MelonLogger.Msg("HeavyTeleporter: Registered /st command.");
        }

        static void OnGameEnded(GameMode mode, Team winner)
        {
            _teamStates.Clear();
            _pendingRequests.Clear();
            _vehicleMenus.Clear();
            MelonLogger.Msg("HeavyTeleporter: Round ended, cleared state.");
        }

        // === /st command ===
        static void OnStCommand(Player? caller, string args)
        {
            if (caller == null) return;

            args = (args ?? "").Trim();
            string[] allParts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = allParts.Length > 1 ? allParts[1].ToLower() : "";

            if (!string.IsNullOrEmpty(sub))
            {
                string valStr = allParts.Length > 2 ? allParts[2].Trim() : "";
                bool isAdmin = caller.CanAdminExecute(Power.Generic);

                switch (sub)
                {
                    case "on":
                        if (!isAdmin) { Deny(caller); return; }
                        _enabled = true; SaveConfig(); Reply(caller, "Heavy Teleporter: ON"); return;
                    case "off":
                        if (!isAdmin) { Deny(caller); return; }
                        _enabled = false; SaveConfig(); Reply(caller, "Heavy Teleporter: OFF"); return;
                    case "charges":
                        if (!isAdmin) { Deny(caller); return; }
                        if (int.TryParse(valStr, out int ch) && ch > 0)
                        { MaxCharges = ch; SaveConfig(); Reply(caller, "Charges = " + MaxCharges); }
                        else Reply(caller, "Charges = " + MaxCharges);
                        return;
                    case "cd":
                        if (!isAdmin) { Deny(caller); return; }
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float cd) && cd > 0)
                        { RechargeTime = cd; SaveConfig(); Reply(caller, "Recharge = " + RechargeTime + "s"); }
                        else Reply(caller, "Recharge = " + RechargeTime + "s");
                        return;
                    case "tier":
                        if (!isAdmin) { Deny(caller); return; }
                        if (int.TryParse(valStr, out int t) && t >= 0)
                        { TechTier = t; SaveConfig(); Reply(caller, "Tech tier = " + TechTier); }
                        else Reply(caller, "Tech tier = " + TechTier);
                        return;
                    case "countdown":
                        if (!isAdmin) { Deny(caller); return; }
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float cdown) && cdown >= 0)
                        { Countdown = cdown; SaveConfig(); Reply(caller, "Countdown = " + Countdown + "s"); }
                        else Reply(caller, "Countdown = " + Countdown + "s");
                        return;
                    case "radius":
                        if (!isAdmin) { Deny(caller); return; }
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float rad) && rad > 0)
                        { TeleportRadius = rad; SaveConfig(); Reply(caller, "Radius = " + TeleportRadius + "m"); }
                        else Reply(caller, "Radius = " + TeleportRadius + "m");
                        return;
                    case "mindist":
                        if (!isAdmin) { Deny(caller); return; }
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float md) && md >= 0)
                        { MinEnemyBaseDistance = md; SaveConfig(); Reply(caller, "Min enemy base distance = " + MinEnemyBaseDistance + "m"); }
                        else Reply(caller, "Min enemy base distance = " + MinEnemyBaseDistance + "m (usage: /st mindist <meters>)");
                        return;
                    case "status":
                        string teamInfo = "";
                        foreach (var kvp in _teamStates)
                        {
                            var st = kvp.Value;
                            if (st.Announced)
                                teamInfo += string.Format(" [{0}/{1}{2}]", st.Charges, MaxCharges,
                                    st.Recharging ? string.Format(" cd:{0:F0}s", st.RechargeTimer) : "");
                        }
                        Reply(caller, string.Format("Heavy Teleporter: {0} | tier>={1} charges={2} cd={3}s countdown={4}s radius={5}m{6}",
                            _enabled ? "ON" : "OFF", TechTier, MaxCharges, RechargeTime, Countdown, TeleportRadius, teamInfo));
                        return;
                }

                // /st <number> — select vehicle from menu
                if (int.TryParse(sub, out int menuChoice) && menuChoice >= 1)
                {
                    int pid = caller.GetInstanceID();
                    if (_vehicleMenus.TryGetValue(pid, out var menu) && menuChoice <= menu.Count)
                    {
                        var chosen = menu[menuChoice - 1];
                        if (chosen.Vehicle != null && !chosen.Vehicle.IsDestroyed)
                        {
                            _vehicleMenus.Remove(pid);
                            RequestTeleportSpecific(caller, caller, chosen.Vehicle);
                            return;
                        }
                        HelperMethods.SendChatMessageToPlayer(caller, "[TELEPORTER] Vehicle no longer available.");
                        _vehicleMenus.Remove(pid);
                        return;
                    }
                    // Fall through to coordinate check if not a valid menu choice
                }

                // /st <x> <z> — commander/admin teleport to coordinates
                if ((caller.IsCommander || isAdmin) && allParts.Length >= 3)
                {
                    if (float.TryParse(sub, NumberStyles.Float, CultureInfo.InvariantCulture, out float cx) &&
                        float.TryParse(allParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float cz))
                    {
                        Vector3 coordPos = new Vector3(cx, 2000f, cz);
                        if (Physics.Raycast(coordPos, Vector3.down, out RaycastHit coordHit, 3000f, GamePhysics.TRACELAYERMASK_TERRAIN))
                            coordPos = coordHit.point + Vector3.up * 5f;
                        else
                            coordPos = new Vector3(cx, 200f, cz);
                        RequestTeleportToPosition(caller, coordPos);
                        return;
                    }
                }

                // /st <playername>
                if (caller.IsCommander || isAdmin)
                {
                    Player? target = FindPlayerByName(sub);
                    if (target == null)
                    { HelperMethods.SendChatMessageToPlayer(caller, "Player not found: " + sub); return; }
                    if (target.ControlledUnit == null || target.ControlledUnit.IsDestroyed)
                    { HelperMethods.SendChatMessageToPlayer(caller, target.PlayerName + " has no active unit."); return; }
                    RequestTeleport(caller, target);
                    return;
                }

                HelperMethods.SendChatMessageToPlayer(caller, "Usage: /st [status] | /st <playername> | /st <x> <z>");
                return;
            }

            // /st with no args — request vehicle to self
            if (!_enabled)
            { HelperMethods.SendChatMessageToPlayer(caller, "Heavy Teleporter is disabled."); return; }

            // Must be Sol or Centauri
            if (caller.Team == null || !(caller.Team.TeamName.Contains("Sol") || caller.Team.TeamName.Contains("Centauri")))
            { HelperMethods.SendChatMessageToPlayer(caller, "[TELEPORTER] Sol or Centauri faction only."); return; }

            if (caller.Team.TechnologyTier < TechTier)
            {
                HelperMethods.SendChatMessageToPlayer(caller,
                    string.Format("[TELEPORTER] Requires tech {0} (current: {1}).", TechTier, caller.Team.TechnologyTier));
                return;
            }

            var ts = GetTeamState(caller.Team);
            if (!ts.Ready || ts.Charges <= 0)
            {
                if (ts.Recharging)
                    HelperMethods.SendChatMessageToPlayer(caller,
                        string.Format("[TELEPORTER] Recharging... {0}s remaining.", Mathf.CeilToInt(ts.RechargeTimer)));
                else
                    HelperMethods.SendChatMessageToPlayer(caller, "[TELEPORTER] Not available.");
                return;
            }

            if (caller.ControlledUnit == null || caller.ControlledUnit.IsDestroyed)
            { HelperMethods.SendChatMessageToPlayer(caller, "[TELEPORTER] You need to be controlling a unit."); return; }

            // Check available vehicles
            var candidates = FindAllEligibleVehicles(caller.Team);
            if (candidates.Count == 0)
            {
                HelperMethods.SendChatMessageToPlayer(caller,
                    "[TELEPORTER] No vehicle in teleport zone. Move a vehicle near the Ultra Heavy Factory waypoint.");
                return;
            }

            if (candidates.Count == 1)
            {
                // Only one vehicle — teleport directly
                RequestTeleportSpecific(caller, caller, candidates[0].Vehicle);
            }
            else
            {
                // Multiple vehicles — show selection menu
                int pid = caller.GetInstanceID();
                _vehicleMenus[pid] = candidates;
                HelperMethods.SendChatMessageToPlayer(caller, "[TELEPORTER] Select vehicle:");
                for (int v = 0; v < candidates.Count; v++)
                {
                    HelperMethods.SendChatMessageToPlayer(caller,
                        string.Format("  {0}: {1} ({2})", v + 1, candidates[v].Name, candidates[v].FactoryLabel));
                }
                HelperMethods.SendChatMessageToPlayer(caller, "Type /st <number> to select.");
            }
        }

        // --- Find Ultra Heavy Factory for a team ---
        static Structure? FindUltraHeavyFactory(Team team)
        {
            var structures = Structure.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                var s = structures[i];
                if (s == null || s.IsDestroyed) continue;
                if (s.Team != team) continue;
                if (s.ObjectInfo == null) continue;
                if (s.ObjectInfo.name.Contains("UltraHeavyFactory"))
                    return s;
            }
            return null;
        }

        // --- Get teleport zone center: factory's production waypoint ---
        static Vector3 GetTeleportZoneCenter(Structure factory)
        {
            if (factory.ProductionUnitMoveToTransform != null)
                return factory.ProductionUnitMoveToTransform.position;
            // Fallback: front of factory
            return factory.transform.position + factory.transform.forward * 15f;
        }

        // --- Find ALL eligible vehicles near Ultra Heavy Factory waypoints ---
        static List<VehicleCandidate> FindAllEligibleVehicles(Team team)
        {
            var candidates = new List<VehicleCandidate>();
            var seen = new HashSet<int>(); // avoid duplicates from overlapping zones
            int factoryNum = 0;

            var structures = Structure.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                var s = structures[i];
                if (s == null || s.IsDestroyed) continue;
                if (s.Team != team) continue;
                if (s.ObjectInfo == null || !s.ObjectInfo.name.Contains("UltraHeavyFactory")) continue;

                factoryNum++;
                Vector3 zoneCenter = GetTeleportZoneCenter(s);
                string factoryLabel = (factoryNum > 1) ? "Factory #" + factoryNum : "Factory";

                var colliders = Physics.OverlapSphere(zoneCenter, TeleportRadius);
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    var vehicle = col.GetComponentInParent<Vehicle>();
                    if (vehicle == null || vehicle.IsDestroyed) continue;
                    if (vehicle.Team != team) continue;

                    // Skip player-occupied vehicles — can't teleport with player inside
                    bool occupied = vehicle.DriverCompartment != null && vehicle.DriverCompartment.NumBoarded > 0;
                    if (occupied) continue;

                    int vid = vehicle.GetInstanceID();
                    if (seen.Contains(vid)) continue;
                    seen.Add(vid);

                    string vehName = vehicle.ObjectInfo != null ? vehicle.ObjectInfo.DisplayName : "Vehicle";

                    candidates.Add(new VehicleCandidate
                    {
                        Vehicle = vehicle,
                        Name = vehName,
                        FactoryLabel = factoryLabel
                    });
                }
            }
            return candidates;
        }

        static Vehicle? FindEligibleVehicle(Team team)
        {
            var candidates = FindAllEligibleVehicles(team);
            return candidates.Count > 0 ? candidates[0].Vehicle : null;
        }

        static void RequestTeleport(Player requester, Player target)
        {
            var vehicle = FindEligibleVehicle(requester.Team);
            if (vehicle == null)
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] No vehicle in teleport zone."); return; }
            RequestTeleportSpecific(requester, target, vehicle);
        }

        static void RequestTeleportSpecific(Player requester, Player target, Vehicle vehicle)
        {
            int reqId = requester.GetInstanceID();
            if (_pendingRequests.ContainsKey(reqId))
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] Request already pending."); return; }

            // Check enemy base proximity
            Vector3 destPos = target.ControlledUnit != null ? target.ControlledUnit.transform.position : Vector3.zero;
            string? proximityError = CheckEnemyBaseProximity(destPos, requester.Team);
            if (proximityError != null)
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] " + proximityError); return; }

            string vehName = vehicle.ObjectInfo != null ? vehicle.ObjectInfo.DisplayName : "Vehicle";
            string targetName = (target == requester) ? "your position" : target.PlayerName;

            _pendingRequests[reqId] = new TeleportRequest
            { Requester = requester, Target = target, RequestTime = Time.time, LastAnnounced = -1, ChosenVehicle = vehicle };

            HelperMethods.SendChatMessageToPlayer(requester,
                string.Format("[TELEPORTER] {0} teleporting to {1} in {2}s...", vehName, targetName, Countdown));
        }

        static void RequestTeleportToPosition(Player requester, Vector3 position)
        {
            int reqId = requester.GetInstanceID();
            if (_pendingRequests.ContainsKey(reqId))
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] Request already pending."); return; }

            // Check enemy base proximity
            string? proximityError = CheckEnemyBaseProximity(position, requester.Team);
            if (proximityError != null)
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] " + proximityError); return; }

            var vehicle = FindEligibleVehicle(requester.Team);
            if (vehicle == null)
            { HelperMethods.SendChatMessageToPlayer(requester, "[TELEPORTER] No vehicle in teleport zone."); return; }

            string vehName = vehicle.ObjectInfo != null ? vehicle.ObjectInfo.DisplayName : "Vehicle";

            _pendingRequests[reqId] = new TeleportRequest
            { Requester = requester, Target = null, TargetPosition = position, UseCoords = true, RequestTime = Time.time, LastAnnounced = -1, ChosenVehicle = vehicle };

            HelperMethods.SendChatMessageToPlayer(requester,
                string.Format("[TELEPORTER] {0} teleporting to ({1:F0}, {2:F0}) in {3}s...", vehName, position.x, position.z, Countdown));
        }

        // --- Check if position is too close to enemy HQ or Nest ---
        static string? CheckEnemyBaseProximity(Vector3 position, Team requesterTeam)
        {
            if (MinEnemyBaseDistance <= 0f) return null;

            var structures = Structure.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                var s = structures[i];
                if (s == null || s.IsDestroyed) continue;
                if (s.Team == null || s.Team == requesterTeam) continue;
                if (s.ObjectInfo == null) continue;

                string sName = s.ObjectInfo.name;
                if (!sName.Contains("Nest") && !sName.Contains("Headquarters")) continue;

                float dist = Vector3.Distance(position, s.transform.position);
                if (dist < MinEnemyBaseDistance)
                {
                    string baseName = sName.Contains("Nest") ? "Nest" : "HQ";
                    return string.Format("Too close to enemy {0} ({1:F0}m, min {2}m).", baseName, dist, MinEnemyBaseDistance);
                }
            }
            return null;
        }

        static void Deny(Player caller) { HelperMethods.SendChatMessageToPlayer(caller, "Heavy Teleporter: admin only."); }

        static void Reply(Player? player, string msg)
        {
            if (player != null) HelperMethods.SendChatMessageToPlayer(player, msg);
            MelonLogger.Msg("HeavyTeleporter: " + msg);
        }

        static void SendTeamChat(Team team, string msg)
        {
            if (team == null) return;
            for (int i = 0; i < Player.Players.Count; i++)
            {
                var p = Player.Players[i];
                if (p != null && p.Team == team && p != NetworkGameServer.GetServerPlayer())
                    HelperMethods.SendChatMessageToPlayer(p, msg);
            }
            MelonLogger.Msg("HeavyTeleporter [TeamChat]: " + msg);
        }

        static Player? FindPlayerByName(string name)
        {
            name = name.ToLower();
            for (int i = 0; i < Player.Players.Count; i++)
            {
                var p = Player.Players[i];
                if (p != null && p.PlayerName != null && p.PlayerName.ToLower().Contains(name))
                    return p;
            }
            return null;
        }

        // === Main update loop ===
        public override void OnUpdate()
        {
            if (!NetworkGameServer.GetServerStarted()) return;
            if (!_enabled) return;

            UpdateTeams();
            ProcessPendingRequests();
        }

        void UpdateTeams()
        {
            // Check all human teams for tech tier and recharge
            var processedTeams = new HashSet<int>();
            for (int i = 0; i < Player.Players.Count; i++)
            {
                var p = Player.Players[i];
                if (p == null || p.Team == null || p.Team.TeamName == null) continue;
                if (!p.Team.TeamName.Contains("Sol") && !p.Team.TeamName.Contains("Centauri")) continue;

                int tid = p.Team.GetInstanceID();
                if (processedTeams.Contains(tid)) continue;
                processedTeams.Add(tid);

                var ts = GetTeamState(p.Team);

                // Tech announcement
                if (!ts.Announced && p.Team.TechnologyTier >= TechTier)
                {
                    ts.Announced = true;
                    ts.Charges = MaxCharges;
                    ts.Ready = true;
                    ts.Recharging = false;
                    SendTeamChat(p.Team,
                        string.Format("[TELEPORTER] ONLINE! {0} vehicle teleports available. Use /st to request a vehicle to your position.", MaxCharges));
                }

                // Recharge
                if (ts.Recharging)
                {
                    ts.RechargeTimer -= Time.deltaTime;
                    ts.LastRechargeAnnounce -= Time.deltaTime;

                    if (ts.LastRechargeAnnounce <= 0f)
                    {
                        ts.LastRechargeAnnounce = RechargeAnnounceInterval;
                        int remaining = Mathf.CeilToInt(ts.RechargeTimer);
                        SendTeamChat(p.Team,
                            string.Format("[TELEPORTER] Recharging... {0}:{1:D2} remaining.", remaining / 60, remaining % 60));
                    }

                    if (ts.RechargeTimer <= 0f)
                    {
                        ts.Recharging = false;
                        ts.Charges = MaxCharges;
                        ts.Ready = true;
                        SendTeamChat(p.Team,
                            string.Format("[TELEPORTER] RECHARGED! {0} vehicle teleports available. Use /st to request.", MaxCharges));
                    }
                }
            }
        }

        void ProcessPendingRequests()
        {
            if (_pendingRequests.Count == 0) return;

            var finished = new List<int>();

            foreach (var kvp in _pendingRequests)
            {
                var req = kvp.Value;

                if (!req.UseCoords)
                {
                    if (req.Target == null || req.Target.ControlledUnit == null || req.Target.ControlledUnit.IsDestroyed)
                    {
                        HelperMethods.SendChatMessageToPlayer(req.Requester, "[TELEPORTER] Target lost. Cancelled.");
                        finished.Add(kvp.Key);
                        continue;
                    }
                }

                var ts = GetTeamState(req.Requester.Team);
                if (ts.Charges <= 0)
                {
                    HelperMethods.SendChatMessageToPlayer(req.Requester, "[TELEPORTER] No charges left. Cancelled.");
                    finished.Add(kvp.Key);
                    continue;
                }

                float elapsed = Time.time - req.RequestTime;
                float remaining = Countdown - elapsed;

                if (remaining <= 0f)
                {
                    bool success = ExecuteTeleport(req);
                    if (success)
                    {
                        ts.Charges--;
                        string targetName = req.UseCoords
                            ? string.Format("({0:F0}, {1:F0})", req.TargetPosition.x, req.TargetPosition.z)
                            : (req.Target == req.Requester) ? req.Requester.PlayerName : req.Target.PlayerName;

                        SendTeamChat(req.Requester.Team,
                            string.Format("[TELEPORTER] Vehicle teleported to {0}! {1}/{2} charges remaining.",
                                targetName, ts.Charges, MaxCharges));

                        if (ts.Charges <= 0)
                        {
                            ts.Ready = false;
                            ts.Recharging = true;
                            ts.RechargeTimer = RechargeTime;
                            ts.LastRechargeAnnounce = RechargeAnnounceInterval;
                            SendTeamChat(req.Requester.Team,
                                string.Format("[TELEPORTER] All charges spent! Recharging in {0} minutes...", (int)RechargeTime / 60));
                        }
                    }
                    else
                    {
                        HelperMethods.SendChatMessageToPlayer(req.Requester,
                            "[TELEPORTER] No eligible vehicle found near Ultra Heavy Factory.");
                    }
                    finished.Add(kvp.Key);
                }
                else
                {
                    int secRemaining = Mathf.CeilToInt(remaining);
                    if (secRemaining != req.LastAnnounced)
                    {
                        req.LastAnnounced = secRemaining;
                        HelperMethods.SendChatMessageToPlayer(req.Requester,
                            string.Format("[TELEPORTER] Teleporting in {0}s...", secRemaining));
                    }
                }
            }

            foreach (int id in finished)
                _pendingRequests.Remove(id);
        }

        bool ExecuteTeleport(TeleportRequest req)
        {
            Vector3 targetPos = req.UseCoords
                ? req.TargetPosition
                : req.Target.ControlledUnit.transform.position;

            // Use the specific vehicle chosen at request time
            var bestVehicle = req.ChosenVehicle;
            if (bestVehicle == null || bestVehicle.IsDestroyed)
            {
                // Fallback: try to find any eligible vehicle
                bestVehicle = FindEligibleVehicle(req.Requester.Team);
                if (bestVehicle == null) return false;
            }

            // Offset based on vehicle size
            Vector3 teleportDest;
            if (req.UseCoords)
            {
                teleportDest = targetPos;
            }
            else
            {
                float vehRadius = bestVehicle.PhysicalRadius;
                float offset = Mathf.Max(vehRadius * 2f + 5f, 15f);
                Vector3 tFwd = req.Target.ControlledUnit.transform.forward;
                tFwd.y = 0f;
                teleportDest = targetPos + tFwd.normalized * offset;
            }

            // Terrain height
            if (Physics.Raycast(new Vector3(teleportDest.x, 2000f, teleportDest.z),
                Vector3.down, out RaycastHit hit, 3000f, GamePhysics.TRACELAYERMASK_TERRAIN))
            {
                teleportDest.y = hit.point.y + 5f;
            }

            // Upright rotation
            Vector3 fwd = req.UseCoords ? bestVehicle.transform.forward : req.Target.ControlledUnit.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Quaternion destRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            // Strip network ownership from driver so client doesn't override teleport
            Player? driver = null;
            var netComp = bestVehicle.NetworkComponent;
            if (netComp != null)
            {
                driver = netComp.OwnerPlayer;
                if (driver != null)
                    netComp.SetPlayerOwner(null);
            }

            bestVehicle.QueueTeleport(teleportDest, destRot, false);
            BaseGameObject.PerformDelayedTeleportAll();

            // Zero velocity to prevent sliding after teleport
            var vRbs = bestVehicle.RigidBodies;
            if (vRbs != null)
            {
                foreach (var rb in vRbs)
                {
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }

            // Restore ownership after a short delay (next frame)
            if (driver != null && netComp != null)
                netComp.SetPlayerOwner(driver);

            try { _ = AudioHelper.PlaySoundFile(SoundFile); }
            catch (Exception ex) { MelonLogger.Warning("HeavyTeleporter sound failed: " + ex.Message); }

            string vehName = bestVehicle.ObjectInfo != null ? bestVehicle.ObjectInfo.DisplayName : "Vehicle";
            MelonLogger.Msg(string.Format("TELEPORT! {0} teleported (requested by {1})", vehName, req.Requester.PlayerName));

            return true;
        }
    }
}
