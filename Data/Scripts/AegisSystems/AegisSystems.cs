using System.Collections.Generic;
using System;
using System.Text;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRageMath;
using VRage.Utils;
using System.IO;
using Sandbox.Game.SessionComponents;
using VRage.Game.Definitions.SessionComponents;
using Sandbox.Game.Entities;

namespace RazMods
{
    [System.Serializable]
    public class ShieldData
    {
        public float ShieldMultiplier = 0.8f;
        public int ShieldBuffer = 5;
        public bool UseHighlightShields = true;
        public bool EnableNotifications = true;
        public int NotificationCooldownTicks = 300; // ~5s before the same message type can re-queue
        public int NotificationSendInterval = 45;   // ~0.75s between sending queued notifications
        public int MaxQueuedNotifications = 6;
    }

    struct QueuedNotification
    {
        public string Message;
        public string Color;
        public long OwnerId;
        public string CooldownKey;
    }

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AegisSystems : MySessionComponentBase
    {
        Dictionary<IMyCubeGrid, MyGridShieldInfo> shieldedGrids = new Dictionary<IMyCubeGrid, MyGridShieldInfo>();
        // List used as a queue (Add / RemoveAt(0)) — Queue<> is not available in SE's mod whitelist
        List<QueuedNotification> notificationQueue = new List<QueuedNotification>();
        Dictionary<string, int> notificationCooldowns = new Dictionary<string, int>();
        HashSet<string> queuedCooldownKeys = new HashSet<string>();

        bool bIsServer = false;
        bool binitialized = false;
        int delay = 0;
        int notificationSendTimer = 0;

        public static AegisSystems Instance;

        public static float SHIELDCOEIFFIENT = 0.0002f;
        public static float SHIELDMULTIPLIER = 0.8f;
        public static int SHIELDBUFFER = 5;
        public static bool USEHIGHLIGHTSHIELDS = true;
        public static bool ENABLENOTIFICATIONS = true;
        public static int NOTIFICATIONCOOLDOWNTICKS = 300;
        public static int NOTIFICATIONSENDINTERVAL = 45;
        public static int MAXQUEUEDNOTIFICATIONS = 6;

        public string shieldDataFile = "ShieldConfig.xml";
        public ShieldData shieldData = null;

        public override void LoadData()
        {
            base.LoadData();
            Instance = this;
            MyLog.Default.WriteLineAndConsole("Shield Config Loading....");
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(shieldDataFile, typeof(string)))
            {
                var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(shieldDataFile, typeof(string));
                if (reader != null)
                {
                    string data = reader.ReadToEnd();
                    shieldData = MyAPIGateway.Utilities.SerializeFromXML<ShieldData>(data);
                    if (shieldData != null)
                    {
                        MyLog.Default.WriteLineAndConsole("Shield Config Loaded");
                        ApplyConfig(shieldData);
                    }
                    else
                    {
                        MyLog.Default.WriteLineAndConsole("Shield Config File was not Found. Creating Config File.");
                        CreateDefaultConfig();
                    }
                }
            }
            else
            {
                CreateDefaultConfig();
            }
        }

        void CreateDefaultConfig()
        {
            shieldData = new ShieldData();
            ApplyConfig(shieldData);
            string shielddata = MyAPIGateway.Utilities.SerializeToXML(shieldData);
            TextWriter tw = MyAPIGateway.Utilities.WriteFileInWorldStorage(shieldDataFile, typeof(string));
            tw.Write(shielddata);
            tw.Close();
            MyLog.Default.WriteLineAndConsole("Shield Config Created");
        }

        void ApplyConfig(ShieldData data)
        {
            SHIELDMULTIPLIER = data.ShieldMultiplier;
            SHIELDBUFFER = data.ShieldBuffer;
            USEHIGHLIGHTSHIELDS = data.UseHighlightShields;
            ENABLENOTIFICATIONS = data.EnableNotifications;
            NOTIFICATIONCOOLDOWNTICKS = Math.Max(60, data.NotificationCooldownTicks);
            NOTIFICATIONSENDINTERVAL = Math.Max(15, data.NotificationSendInterval);
            MAXQUEUEDNOTIFICATIONS = Math.Max(1, data.MaxQueuedNotifications);
        }

        public override void SaveData()
        {
            base.SaveData();
            if (shieldData == null)
                shieldData = new ShieldData();
            string shielddata = MyAPIGateway.Utilities.SerializeToXML(shieldData);
            TextWriter tw = MyAPIGateway.Utilities.WriteFileInWorldStorage(shieldDataFile, typeof(string));
            tw.Write(shielddata);
            tw.Close();
            MyLog.Default.WriteLineAndConsole("Shield Config Saved");
        }

        /// <summary>
        /// Queue a player notification. Same cooldown key cannot re-queue until the cooldown expires.
        /// Messages are drained slowly so combat cannot flood the server with ShowNotification calls.
        /// </summary>
        public static void EnqueueNotification(string message, string color, long ownerId, string cooldownKey)
        {
            if (!ENABLENOTIFICATIONS || Instance == null || string.IsNullOrEmpty(message))
                return;
            if (ownerId == 0)
                return;

            string key = cooldownKey ?? message;
            if (Instance.notificationCooldowns.ContainsKey(key) || Instance.queuedCooldownKeys.Contains(key))
                return;
            if (Instance.notificationQueue.Count >= MAXQUEUEDNOTIFICATIONS)
                return;

            Instance.notificationQueue.Add(new QueuedNotification
            {
                Message = message,
                Color = color ?? "White",
                OwnerId = ownerId,
                CooldownKey = key
            });
            Instance.queuedCooldownKeys.Add(key);
        }

        void ProcessNotificationQueue()
        {
            // Tick down cooldowns without mutating the dictionary during enumeration
            if (notificationCooldowns.Count > 0)
            {
                List<string> keys = new List<string>(notificationCooldowns.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    string key = keys[i];
                    int remaining;
                    if (!notificationCooldowns.TryGetValue(key, out remaining))
                        continue;
                    remaining--;
                    if (remaining <= 0)
                        notificationCooldowns.Remove(key);
                    else
                        notificationCooldowns[key] = remaining;
                }
            }

            if (notificationQueue.Count == 0)
                return;

            notificationSendTimer++;
            if (notificationSendTimer < NOTIFICATIONSENDINTERVAL)
                return;
            notificationSendTimer = 0;

            QueuedNotification note = notificationQueue[0];
            notificationQueue.RemoveAt(0);
            queuedCooldownKeys.Remove(note.CooldownKey);
            try
            {
                MyVisualScriptLogicProvider.ShowNotification(note.Message, 4000, note.Color, note.OwnerId);
            }
            catch { /* owner may have left */ }
            notificationCooldowns[note.CooldownKey] = NOTIFICATIONCOOLDOWNTICKS;
        }

        public override void UpdateBeforeSimulation()
        {
            if (!binitialized)
                Init();
            if (!bIsServer)
                return;

            ProcessNotificationQueue();

            // Space out shield updates so it doesn't lag out the game
            delay++;
            if (delay < 120)
                return;
            delay = 0;

            List<IMyCubeGrid> toRemove = null;
            foreach (var kvp in shieldedGrids)
            {
                MyGridShieldInfo info = kvp.Value;
                if (info?.grid == null || (info.grid as IMyEntity)?.Closed == true)
                {
                    if (toRemove == null) toRemove = new List<IMyCubeGrid>();
                    toRemove.Add(kvp.Key);
                    continue;
                }

                info.Update();

                if (info.shieldStrength <= 0.0f)
                {
                    if (info.shieldsup)
                    {
                        try
                        {
                            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", info.grid.GetPosition());
                            MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(info.grid.Name, 1.0f);
                        }
                        catch { }
                        info.shieldFX = true;
                        info.ShieldFX(false);
                        info.shieldsup = false;
                    }
                }
                else if (!info.shieldsup && info.MaxPowerJumpDrives() > 0)
                {
                    try
                    {
                        MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldUp", info.grid.GetPosition());
                        MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(info.grid.Name, AegisSystems.SHIELDMULTIPLIER);
                    }
                    catch { }
                    info.shieldFX = false;
                    info.ShieldFX(true, 20);
                    info.shieldsup = true;
                }
            }

            if (toRemove != null)
            {
                foreach (var g in toRemove)
                    shieldedGrids.Remove(g);
            }
        }

        void Init()
        {
            bIsServer = MyAPIGateway.Multiplayer.IsServer;
            binitialized = true;
            Instance = this;

            if (!bIsServer)
                return;

            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            foreach (IMyEntity entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || entity.Closed) continue;
                try
                {
                    grid.OnBlockAdded += Grid_OnBlockAdded;
                    grid.OnBlockRemoved += Grid_OnBlockRemoved;
                    grid.OnClose += Grid_OnClose;
                }
                catch { continue; }
                if (GridHasJumpDrive(grid) && !shieldedGrids.ContainsKey(grid))
                    shieldedGrids[grid] = new MyGridShieldInfo(grid);
            }

            MyLog.Default.WriteLineAndConsole("Aegis Mod Initialized");
            MyAPIGateway.Entities.OnEntityAdd += CheckNewGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveTheGrid;
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, ShieldHandler);
        }

        bool TryGetShieldInfo(IMyCubeGrid grid, out MyGridShieldInfo info)
        {
            info = null;
            if (grid == null) return false;
            return shieldedGrids.TryGetValue(grid, out info) && info != null;
        }

        private void Grid_OnClose(IMyEntity obj)
        {
            IMyCubeGrid grid = obj as IMyCubeGrid;
            if (grid == null)
                return;
            try
            {
                grid.OnBlockAdded -= Grid_OnBlockAdded;
                grid.OnBlockRemoved -= Grid_OnBlockRemoved;
                grid.OnClose -= Grid_OnClose;
            }
            finally
            {
                shieldedGrids.Remove(grid);
            }
        }

        private void ShieldHandler(object target, ref MyDamageInformation info)
        {
            var block = target as IMySlimBlock;
            if (block == null)
                return;
            var cg = block.CubeGrid;
            if (cg == null || (cg as IMyEntity)?.Closed == true)
                return;

            MyGridShieldInfo sg;
            if (!TryGetShieldInfo(cg, out sg) || !sg.shieldsup)
                return;

            sg.HandleDamage(block, ref info);

            var fat = block.FatBlock;
            if (fat == null) return;

            if (sg.buffer <= 0)
            {
                try
                {
                    MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldHit", fat.GetPosition());
                }
                catch { }
                sg.buffer = AegisSystems.SHIELDBUFFER;
            }
            else
            {
                sg.buffer--;
            }
        }

        bool GridHasJumpDrive(IMyCubeGrid grid)
        {
            if (grid == null || (grid as IMyEntity)?.Closed == true)
                return false;
            IEnumerable<IMyJumpDrive> jumpdrives = grid.GetFatBlocks<IMyJumpDrive>();
            if (jumpdrives == null)
                return false;
            foreach (var jd in jumpdrives)
            {
                if (jd == null) continue;
                try
                {
                    if (jd.CustomData == null || !jd.CustomData.Contains("[NOSHIELD]"))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private void Grid_OnBlockRemoved(IMySlimBlock obj)
        {
            if (obj == null) return;
            IMyCubeGrid cubeGrid = obj.CubeGrid;
            if (cubeGrid == null) return;
            var fat = obj.FatBlock;
            if (fat == null) return;
            IMyJumpDrive jd = fat as IMyJumpDrive;
            if (jd == null) return;

            MyGridShieldInfo sg;
            if (!TryGetShieldInfo(cubeGrid, out sg)) return;
            if (sg.jumpDrives != null)
                sg.jumpDrives.Remove(jd);
            sg.getblocksbuffer = 0;
        }

        private void Grid_OnBlockAdded(IMySlimBlock obj)
        {
            if (obj == null) return;
            IMyCubeGrid cubeGrid = obj.CubeGrid;
            if (cubeGrid == null) return;
            var fat = obj.FatBlock;
            if (fat == null) return;
            IMyJumpDrive jd = fat as IMyJumpDrive;
            if (jd == null) return;

            MyGridShieldInfo sg;
            if (!TryGetShieldInfo(cubeGrid, out sg))
            {
                sg = new MyGridShieldInfo(cubeGrid);
                shieldedGrids[cubeGrid] = sg;
                EnqueueNotification("Aegis Systems Firmware Installed into Jump Drive", "Green", jd.OwnerId, "firmware:" + cubeGrid.EntityId);
            }
            else
            {
                // Existing shield grid gained another JD — quieter, cooldown-gated message
                EnqueueNotification("Aegis Systems Firmware Installed into Jump Drive", "Green", jd.OwnerId, "firmware:" + cubeGrid.EntityId);
            }
            sg.getblocksbuffer = 0;
        }

        public void RemoveTheGrid(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null) return;
            try
            {
                grid.OnBlockAdded -= Grid_OnBlockAdded;
                grid.OnBlockRemoved -= Grid_OnBlockRemoved;
                grid.OnClose -= Grid_OnClose;
            }
            catch { }
            shieldedGrids.Remove(grid);
        }

        public void CheckNewGrid(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null || (grid as IMyEntity)?.Closed == true) return;
            try
            {
                grid.OnBlockAdded += Grid_OnBlockAdded;
                grid.OnBlockRemoved += Grid_OnBlockRemoved;
                grid.OnClose += Grid_OnClose;
            }
            catch { return; }

            if (GridHasJumpDrive(grid) && !shieldedGrids.ContainsKey(grid))
                shieldedGrids[grid] = new MyGridShieldInfo(grid);
        }
    }

    public class MyGridShieldInfo
    {
        public List<IMyJumpDrive> jumpDrives;
        public List<IMyTextPanel> panels;
        public IMyCubeGrid grid;
        public float shieldStrength = 0.0f;
        public bool shieldsup = false;
        public bool shieldFX = false;
        public bool fxdisabled = false;
        public int buffer = 0;
        public int shieldbuffer = 0;
        public int getblocksbuffer = 0;
        public bool hasFactionColor = false;
        public Color shieldsColor = Color.White;

        // Once-per-event warning tracking (cleared when drive recharges)
        HashSet<long> warnedCritical = new HashSet<long>();
        HashSet<long> warnedDrained = new HashSet<long>();
        bool warnedShieldsDown = false;

        static readonly char BarChar = Convert.ToChar(Convert.ToUInt32("e2d2", 16));

        public MyGridShieldInfo(IMyCubeGrid g)
        {
            grid = g;
            jumpDrives = g != null ? GetJumpDrives(g) : new List<IMyJumpDrive>();
            if (jumpDrives == null)
                jumpDrives = new List<IMyJumpDrive>();
            panels = new List<IMyTextPanel>();
            shieldStrength = GetShieldStrength(jumpDrives);
        }

        public void Update()
        {
            if (grid == null || (grid as IMyEntity)?.Closed == true)
                return;
            if (jumpDrives == null)
                jumpDrives = new List<IMyJumpDrive>();
            if (panels == null)
                panels = new List<IMyTextPanel>();

            // Refresh block lists periodically (do not reset sound/FX buffers here —
            // resetting them every update was defeating hit throttling during combat)
            if (getblocksbuffer <= 0)
            {
                jumpDrives = GetJumpDrives(grid) ?? new List<IMyJumpDrive>();
                panels = GetPanels(grid) ?? new List<IMyTextPanel>();
                getblocksbuffer = 10;
            }
            getblocksbuffer--;

            shieldStrength = GetShieldStrength(jumpDrives);
            ClearWarningFlagsForRechargedDrives();

            if (!hasFactionColor)
            {
                try
                {
                    if (grid.BigOwners != null && grid.BigOwners.Count > 0)
                    {
                        string fname = MyVisualScriptLogicProvider.GetPlayersFactionName(grid.BigOwners[0]);
                        IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionByName(fname);
                        if (faction != null)
                        {
                            hasFactionColor = true;
                            shieldsColor = faction.IconColor;
                        }
                    }
                }
                catch { }
            }

            if (AegisSystems.USEHIGHLIGHTSHIELDS)
            {
                if (shieldFX)
                    ShieldFX(false);
            }
            else if (!fxdisabled)
            {
                ShieldFX(false);
                fxdisabled = true;
            }

            if (shieldStrength <= 0.0f && shieldsup)
            {
                try
                {
                    if (grid != null && (grid as IMyEntity)?.Closed != true)
                    {
                        MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
                        MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.Name, 1.0f);
                    }
                }
                catch { }
                if (shieldFX)
                    ShieldFX(false);
                shieldsup = false;
            }
            else if (shieldStrength > 0.0f)
            {
                // Allow a new "shields down" notification after they recover
                warnedShieldsDown = false;
            }

            UpdatePanels();
            shieldFX = false;
        }

        void ClearWarningFlagsForRechargedDrives()
        {
            if (jumpDrives == null || (warnedCritical.Count == 0 && warnedDrained.Count == 0))
                return;

            foreach (var j in jumpDrives)
            {
                if (j == null || (j as IMyEntity)?.Closed == true) continue;
                try
                {
                    long id = j.EntityId;
                    if (j.CurrentStoredPower > 0.5f)
                        warnedCritical.Remove(id);
                    if (j.CurrentStoredPower > 0.01f)
                        warnedDrained.Remove(id);
                }
                catch { }
            }
        }

        public void UpdatePanels()
        {
            if (panels == null || panels.Count == 0 || jumpDrives == null) return;
            int jdCount = jumpDrives.Count;
            if (jdCount <= 0) jdCount = 1;

            float pct = (float)Math.Ceiling(((shieldStrength / jdCount) * 100) * 0.333f);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;

            int barLen = (int)Math.Ceiling(pct * 0.1f);
            if (barLen < 0) barLen = 0;
            if (barLen > 10) barLen = 10;

            StringBuilder bar = new StringBuilder(barLen);
            for (int i = 0; i < barLen; i++)
                bar.Append(BarChar);

            string status;
            if (shieldsup)
                status = "Shields Up \n";
            else if (jumpDrives.Count > 0)
                status = "Recharging \n";
            else
                status = "No Shields \n";

            string body = jumpDrives.Count > 0
                ? status + pct + "% \n" + bar
                : status + "--";

            foreach (var tp in panels)
            {
                if (tp == null) continue;
                try { tp.WriteText(body); }
                catch { }
            }
        }

        public void ShieldFX(bool enable, int thicc = 10)
        {
            if (grid == null || (grid as IMyEntity)?.Closed == true)
                return;
            if (!AegisSystems.USEHIGHLIGHTSHIELDS)
                return;

            try
            {
                if (enable && !shieldFX)
                {
                    shieldFX = true;
                    MyVisualScriptLogicProvider.SetAlphaHighlightForAll(grid.Name, true, thicc, 20, shieldsColor, null, 0.0165f * (float)thicc);
                }
                else if (!enable && shieldFX)
                {
                    MyVisualScriptLogicProvider.SetAlphaHighlightForAll(grid.Name, false, 10, 10, shieldsColor, null, 0.025f);
                }
            }
            catch { }
        }

        public int MaxPowerJumpDrives()
        {
            if (jumpDrives == null) return 0;
            int jdcount = 0;
            foreach (var j in jumpDrives)
            {
                if (j == null || (j as IMyEntity)?.Closed == true) continue;
                try
                {
                    if (j.IsFunctional && j.Enabled && (j.CustomData == null || !j.CustomData.Contains("[NOSHIELD]"))
                        && j.CurrentStoredPower == j.MaxStoredPower)
                        jdcount++;
                }
                catch { }
            }
            return jdcount;
        }

        public void HandleDamage(IMySlimBlock block, ref MyDamageInformation info)
        {
            if (grid == null || (grid as IMyEntity)?.Closed == true) return;
            if (jumpDrives == null || jumpDrives.Count == 0) return;

            // Use cached strength; ApplyShieldDamage updates jump drive power live
            float shieldstrength = shieldStrength;
            int jdCount = jumpDrives.Count;
            if (jdCount <= 0) jdCount = 1;

            if (shieldbuffer <= 0)
            {
                if (AegisSystems.USEHIGHLIGHTSHIELDS)
                {
                    double thick = Math.Ceiling(((shieldStrength / jdCount) * 100) * 0.333f) * 0.1;
                    if (thick < 1) thick = 1;
                    ShieldFX(true, (int)thick);
                }
                // Longer gap between hit FX during heavy fire
                shieldbuffer = Math.Max(AegisSystems.SHIELDBUFFER, 8);
            }
            shieldbuffer--;

            // LCD updates only happen on the periodic Update() — writing text every few
            // damage events was a major network/lag source during combat.

            float damageCost = info.Amount * AegisSystems.SHIELDCOEIFFIENT * AegisSystems.SHIELDMULTIPLIER;
            if (shieldstrength > damageCost)
            {
                ApplyShieldDamage(info.Amount);
                info.Amount = 0;
                info.IsDeformation = false;
                shieldStrength = GetShieldStrength();
            }
            else
            {
                OverLoad(ref info);
            }
        }

        public void OverLoad(ref MyDamageInformation info)
        {
            if (grid == null || (grid as IMyEntity)?.Closed == true) return;
            if (jumpDrives == null) return;

            long ownerId = 0;
            foreach (var j in jumpDrives)
            {
                if (j == null || (j as IMyEntity)?.Closed == true) continue;
                try
                {
                    if (j.IsFunctional && j.Enabled)
                    {
                        info.Amount -= j.CurrentStoredPower;
                        info.IsDeformation = false;
                        j.CurrentStoredPower = 0;
                    }
                    if (ownerId == 0)
                        ownerId = j.OwnerId;
                }
                catch { }
            }

            if (!warnedShieldsDown)
            {
                warnedShieldsDown = true;
                long gridId = 0;
                try { gridId = grid.EntityId; } catch { }
                AegisSystems.EnqueueNotification("Shields Are Down", "Red", ownerId, "down:" + gridId);
            }

            try
            {
                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
                MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.Name, 1.0f);
            }
            catch { }

            ShieldFX(false);
            shieldsup = false;
            shieldStrength = 0;
        }

        public float ApplyShieldDamage(float damage)
        {
            if (jumpDrives == null) return damage;
            float coeff = AegisSystems.SHIELDCOEIFFIENT * AegisSystems.SHIELDMULTIPLIER;
            if (coeff <= 0) return damage;

            foreach (var j in jumpDrives)
            {
                if (j == null || (j as IMyEntity)?.Closed == true) continue;
                try
                {
                    if (!j.IsFunctional || !j.Enabled) continue;

                    long id = j.EntityId;
                    if (j.CurrentStoredPower <= damage * coeff)
                    {
                        damage -= j.CurrentStoredPower / coeff;
                        j.CurrentStoredPower = 0;
                        if (!warnedDrained.Contains(id))
                        {
                            warnedDrained.Add(id);
                            AegisSystems.EnqueueNotification(
                                "Jump Drive: " + j.CustomName + " Has Been Fully Drained",
                                "Red", j.OwnerId, "drained:" + id);
                        }
                    }
                    else
                    {
                        j.CurrentStoredPower -= damage * coeff;
                        if (j.CurrentStoredPower <= 0.2f && !warnedCritical.Contains(id))
                        {
                            warnedCritical.Add(id);
                            AegisSystems.EnqueueNotification(
                                "Warning Shield Integrity for " + j.CustomName + " Critically Low",
                                "Yellow", j.OwnerId, "crit:" + id);
                        }
                        return 0.0f;
                    }
                }
                catch { }
            }
            return damage;
        }

        public float GetShieldStrength()
        {
            return GetShieldStrength(jumpDrives);
        }

        public static float GetShieldStrength(List<IMyJumpDrive> jumpdrives)
        {
            if (jumpdrives == null) return 0.0f;
            float shields = 0.0f;
            foreach (var j in jumpdrives)
            {
                if (j == null || (j as IMyEntity)?.Closed == true) continue;
                try
                {
                    if (j.IsFunctional && j.Enabled && (j.CustomData == null || !j.CustomData.Contains("[NOSHIELD]")))
                        shields += j.CurrentStoredPower;
                }
                catch { }
            }
            return shields;
        }

        public static List<IMyTextPanel> GetPanels(IMyCubeGrid grid)
        {
            List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
            if (grid == null || (grid as IMyEntity)?.Closed == true)
                return textPanels;
            IEnumerable<IMyTextPanel> tp = grid.GetFatBlocks<IMyTextPanel>();
            if (tp == null) return textPanels;
            foreach (var block in tp)
            {
                if (block == null) continue;
                try
                {
                    if (block.CustomData != null && block.CustomData.Contains("[SHIELD]"))
                        textPanels.Add(block);
                }
                catch { }
            }
            return textPanels;
        }

        public static List<IMyJumpDrive> GetJumpDrives(IMyCubeGrid grid)
        {
            List<IMyJumpDrive> jumpDrives = new List<IMyJumpDrive>();
            if (grid == null || (grid as IMyEntity)?.Closed == true)
                return jumpDrives;
            IEnumerable<IMyJumpDrive> jd = grid.GetFatBlocks<IMyJumpDrive>();
            if (jd == null) return jumpDrives;
            foreach (var block in jd)
            {
                if (block == null || (block as IMyEntity)?.Closed == true) continue;
                try
                {
                    if (block.CustomData == null || !block.CustomData.Contains("[NOSHIELD]"))
                        jumpDrives.Add(block);
                }
                catch { }
            }
            return jumpDrives;
        }
    }
}

