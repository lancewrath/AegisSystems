using System.Collections.Generic;
using System;
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
    }

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AegisSystems : MySessionComponentBase
    {
        List<MyGridShieldInfo> shieldedGrids = new List<MyGridShieldInfo>();
        bool bIsServer = false;
        bool binitialized = false;
        int delay = 0;

        public static float SHIELDCOEIFFIENT = 0.0002f;
        public static float SHIELDMULTIPLIER = 0.8f;
        public static int SHIELDBUFFER = 5;
        public static bool USEHIGHLIGHTSHIELDS = true;

        public string shieldDataFile = "ShieldConfig.xml";
        public ShieldData shieldData = null;

        public override void LoadData()
        {
            base.LoadData();
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
                        AegisSystems.SHIELDMULTIPLIER = shieldData.ShieldMultiplier;
                        AegisSystems.SHIELDBUFFER = shieldData.ShieldBuffer;
                        AegisSystems.USEHIGHLIGHTSHIELDS = shieldData.UseHighlightShields;

                    }
                    else
                    {
                        MyLog.Default.WriteLineAndConsole("Shield Config File was not Found. Creating Config File.");
                        shieldData = new ShieldData();
                        string shielddata = MyAPIGateway.Utilities.SerializeToXML(shieldData);
                        TextWriter tw = MyAPIGateway.Utilities.WriteFileInWorldStorage(shieldDataFile, typeof(string));
                        tw.Write(shielddata);
                        tw.Close();
                        MyLog.Default.WriteLineAndConsole("Shield Config Created");
                        
                    }
                }
            }

        }


        public override void SaveData()
        {
            base.SaveData();
            if(shieldData==null)
            {
                shieldData = new ShieldData();
            }
            if (shieldData != null)
            {
                string shielddata = MyAPIGateway.Utilities.SerializeToXML(shieldData);
                TextWriter tw = MyAPIGateway.Utilities.WriteFileInWorldStorage(shieldDataFile, typeof(string));
                tw.Write(shielddata);
                tw.Close();
                MyLog.Default.WriteLineAndConsole("Shield Config Saved");
            }
        }


        public override void UpdateBeforeSimulation()
        {
            if (!binitialized)
                Init();
            if (!bIsServer)
                return;


            //put some space between when we call these functions so it doesn't lag out the game
            delay++;

            if (delay == 120)
            {
                foreach (var grid in shieldedGrids)
                {               
                    grid.Update();
                    if (grid.shieldStrength <= 0.0f)
                    {                       
                        if (grid.grid != null)
                        {
                            if (grid.shieldsup)
                            {

                                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.grid.GetPosition());
                                //reset damage multiplier
                                MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.grid.Name, 1.0f);
                                grid.shieldFX = true;
                                grid.ShieldFX(false);
                                grid.shieldsup = false;
                            }

                        }
                    }
                    else
                    {
                        if (grid.grid != null)
                        {
                            //If shields were down and we have at least one jump drive that is operational and at max power
                            if (!grid.shieldsup && grid.MaxPowerJumpDrives() > 0)
                            {

                                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldUp", grid.grid.GetPosition());
                                //add Damage multiplier
                                MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.grid.Name, AegisSystems.SHIELDMULTIPLIER);
                                grid.shieldFX = false;
                                grid.ShieldFX(true,20);
                                grid.shieldsup = true;
                            }
                        }
                    }
                }
                delay = 0;
            }
        }

        void Init()
        {
            bIsServer = MyAPIGateway.Multiplayer.IsServer;
            binitialized = true;

            if (!bIsServer)
                return;
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            foreach (IMyEntity entity in entities)
            {
                var grid = entity as IMyCubeGrid;

                if (grid == null)
                {
                    continue;
                }
                grid.OnBlockAdded += Grid_OnBlockAdded;
                grid.OnBlockRemoved += Grid_OnBlockRemoved;
                grid.OnClose += Grid_OnClose;

                if (GridHasJumpDrive(grid))
                {
                    shieldedGrids.Add(new MyGridShieldInfo(grid));
                }
            }

            MyLog.Default.WriteLineAndConsole("Aegis Mod Initialized");
            MyAPIGateway.Utilities.ShowMessage("Aegis", "Mod Initialized");
            MyAPIGateway.Entities.OnEntityAdd += CheckNewGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveTheGrid;
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, ShieldHandler);
            


        }



        private void Grid_OnClose(IMyEntity obj)
        {
            if(obj as IMyCubeGrid != null)
            {
                IMyCubeGrid grid = obj as IMyCubeGrid;

                grid.OnBlockAdded -= Grid_OnBlockAdded;
                grid.OnBlockRemoved -= Grid_OnBlockRemoved;
                grid.OnClose -= Grid_OnClose;
                //just make sure grid is removed from list entirely.
                MyGridShieldInfo sgrid = shieldedGrids.Find(g => g.grid == grid);
                if(sgrid != null)
                {
                    shieldedGrids.Remove(sgrid);
                }
            }
        }

        private void ShieldHandler(object target, ref MyDamageInformation info)
        {
            
            if (target as IMySlimBlock != null)
            {
                var block = target as IMySlimBlock;
                if(block != null)
                {
                    var cg = block.CubeGrid;
                    if (cg != null)
                    {
                        MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == cg);
                        if (sg != null)
                        {
                            if (sg.shieldsup)
                            {
                                sg.HandleDamage(block, ref info);
                                
                                var fat = block.FatBlock;
                                if(fat!=null)
                                {
                                    
                                    if (sg.buffer <= 0)
                                    {
                                        Vector3D fatcoord = block.FatBlock.GetPosition();
                                        MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldHit", fatcoord);
                                        sg.buffer = AegisSystems.SHIELDBUFFER;
                                    } else
                                    {
                                        sg.buffer--;
                                    }
                                }

                                
                            }
                        }
                    }
                }
                

                
            }
        }

        bool GridHasJumpDrive(IMyCubeGrid grid)
        {
            IEnumerable<IMyJumpDrive> jumpdrives = grid.GetFatBlocks<IMyJumpDrive>();

            int count = 0;
            foreach(var jd in jumpdrives)
            {
                if (!jd.CustomData.Contains("[NOSHIELD]"))
                    count++;
            }
            if(count>0)
            {
                return true;
            }
            return false;
            
        }

        public bool GetJumpDrivesWorking(IMyCubeGrid grid)
        {
            IEnumerable<IMyJumpDrive> jumpdrives = grid.GetFatBlocks<IMyJumpDrive>();
            foreach (var jd in jumpdrives)
            {
                if (jd != null)
                {
                    if (jd.IsWorking && !jd.CustomData.Contains("[NOSHIELD]"))
                    {
                        return true;
                    }                   
                }
            }
            return false;
        }

        private void Grid_OnBlockRemoved(IMySlimBlock obj)
        {
            if (obj == null) return;
            var fat = obj.FatBlock;
            if (fat != null)
            {
                if (fat as IMyJumpDrive != null)
                {
                    IMyJumpDrive jd = (IMyJumpDrive)fat;
                    if (jd != null)
                    {
                        MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == obj.CubeGrid);
                        if(sg!=null)
                        {
                            sg.jumpDrives.Remove(jd);
                            sg.getblocksbuffer = 0;
                            sg.Update();
                        }
                    }
                    
                }
            }
        }

        private void Grid_OnBlockAdded(IMySlimBlock obj)
        {
            if (obj == null) return;
            var fat = obj.FatBlock;
            if (fat != null)
            {
                if (fat as IMyJumpDrive != null)
                {
                    IMyJumpDrive jd = fat as IMyJumpDrive;
                    if (jd != null)
                    {

                        MyVisualScriptLogicProvider.ShowNotification("Aegis Systems Firmware Installed into Jump Drive", 5000, "Green", jd.OwnerId);
                        MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == obj.CubeGrid);

                        if (sg == null)
                        {
                            sg = new MyGridShieldInfo(obj.CubeGrid);
                            shieldedGrids.Add(sg);
                        }
                        
                        if (sg != null)
                        {
                            sg.getblocksbuffer = 0;
                            sg.Update();
                        }
                    }
                }
            }
        }

        public void RemoveTheGrid(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;

            if (grid == null)
            {
                return;
            }

            grid.OnBlockAdded -= Grid_OnBlockAdded;
            grid.OnBlockRemoved -= Grid_OnBlockRemoved;
            grid.OnClose -= Grid_OnClose;

            var sg = shieldedGrids.Find(x => x.grid == grid);
            if(sg!=null)
            {
                shieldedGrids.Remove(sg);

            }
        }

        public void CheckNewGrid(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;

            if (grid == null)
            {
                return;
            }

            grid.OnBlockAdded += Grid_OnBlockAdded;
            grid.OnBlockRemoved += Grid_OnBlockRemoved;
            grid.OnClose += Grid_OnClose;

            if (GridHasJumpDrive(grid))
            {
                MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == grid);
                if (sg == null)
                {
                    shieldedGrids.Add(new MyGridShieldInfo(grid));
                }
            }
            delay = 0;
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
        public int panelbuffer = 0;
        public int messagebuffer = 0;
        public int messagebufferB = 0;
        public bool hasFactionColor = false;
        public Color shieldsColor = Color.White;

        public MyGridShieldInfo(IMyCubeGrid g)
        {
            grid = g;
            jumpDrives = GetJumpDrives(g);
            shieldStrength = GetShieldStrength(jumpDrives);
        }

        public void Update()
        {
            buffer = 0;
            shieldbuffer = 0;
            //help reduce lag by less frequent calls to update block listing.
            if (getblocksbuffer <= 0)
            {
                jumpDrives = GetJumpDrives(grid);
                panels = GetPanels(grid);
                getblocksbuffer = 10;
            }
            getblocksbuffer--;
            messagebuffer = 0;
            messagebufferB = 0;
            
            shieldStrength = GetShieldStrength(jumpDrives);

           

            //reduce lag by checking faction every update -- should add a callback when a player changes faction
            if (!hasFactionColor)
            {
                if (grid.BigOwners != null)
                {
                    if (grid.BigOwners.Count > 0)
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
            }

            //turn off shields FX at update
            if (AegisSystems.USEHIGHLIGHTSHIELDS)
            {
                
                if (shieldFX)
                {
                    ShieldFX(false);

                }
            } else {
            //turn off any existing shields FX at update
                if (!fxdisabled)
                {
                    ShieldFX(false);
                    fxdisabled = true;
                }
            }


            if (shieldStrength<=0.0f && shieldsup)
            {
                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
                if(shieldFX)
                {
                    ShieldFX(false);
                }
                //Added damage modifier to system
                MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.Name, 1.0f);
                shieldsup = false;
            }
            UpdatePanels();
            shieldFX = false;
        }

        public void UpdatePanels()
        {
            float ss = 0.0f;
            string bar = "";
            while (ss < (Math.Ceiling((shieldStrength / jumpDrives.Count) * 0.333)))
            {
                bar = bar + Convert.ToChar(Convert.ToUInt32("e2d2", 16)).ToString();
                ss += 0.1f;
            }
            foreach (var tp in panels)
            {
                if (shieldsup)
                {
                    tp.WriteText("Shields Up \n");
                } else
                {
                    if (jumpDrives.Count > 0)
                    {
                        tp.WriteText("Recharging \n");
                    } else
                    {
                        tp.WriteText("No Shields \n");
                    }
                }
                if (jumpDrives.Count > 0)
                {
                    tp.WriteText(Math.Ceiling(((shieldStrength/ jumpDrives.Count) * 100) * 0.333f) + "% \n", true);
                    tp.WriteText(bar, true);
                } else
                {
                    tp.WriteText("--", true);
                }

            }
        }

        public void ShieldFX(bool enable,int thicc = 10)
        {
           
            if (enable)
            {                               
                if (AegisSystems.USEHIGHLIGHTSHIELDS && !shieldFX)
                {
                    shieldFX = true;
                    MyVisualScriptLogicProvider.SetAlphaHighlightForAll(grid.Name, true, thicc, 20, shieldsColor, null, 0.0165f * (float)thicc);
                }

            } else
            {
                
                if (AegisSystems.USEHIGHLIGHTSHIELDS && shieldFX)
                {
                    MyVisualScriptLogicProvider.SetAlphaHighlightForAll(grid.Name, false, 10, 10, shieldsColor, null, 0.025f);
                }
            }
           

        }

        public int MaxPowerJumpDrives()
        {
            int jdcount = 0;
            foreach (var j in jumpDrives)
            {
                if (j == null) continue;
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled && !jd.CustomData.Contains("[NOSHIELD]"))
                    {
                        if(jd.CurrentStoredPower==jd.MaxStoredPower)
                        {
                            jdcount++;
                        }
                    }
                }
            }
            return jdcount;
        }

        public void HandleDamage(IMySlimBlock block, ref MyDamageInformation info)
        {
            float shieldstrength = GetShieldStrength();

            if(shieldbuffer<=0)
            {
                if (AegisSystems.USEHIGHLIGHTSHIELDS)
                {
                    //show shields on damage
                    double thick = Math.Ceiling(((shieldStrength / jumpDrives.Count) * 100) * 0.333f)*0.1;
                    ShieldFX(true, (int)thick);
                }
                shieldbuffer = AegisSystems.SHIELDBUFFER;
            }
            shieldbuffer--;

            if (panelbuffer<=0)
            {
                UpdatePanels();
                panelbuffer = 5;
            }
            
            panelbuffer--;

            if (shieldstrength > (info.Amount * AegisSystems.SHIELDCOEIFFIENT * AegisSystems.SHIELDMULTIPLIER))
            {
                //block all damage               
                ApplyShieldDamage(info.Amount);
                info.Amount = 0;
                info.IsDeformation = false;
                
                
            } else
            {
                //eat up whats left over to mitigate some of the damage
                OverLoad(ref info);
                
            }

        }

        public void OverLoad(ref MyDamageInformation info)
        {
            long Ownerid = 0;
            foreach (var j in jumpDrives)
            {
                if (j == null) continue;

                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled)
                    {
                       
                        info.Amount -= jd.CurrentStoredPower;
                        info.IsDeformation = false;
                        jd.CurrentStoredPower = 0;
                        
                    }
                    Ownerid = jd.OwnerId;
                }
            }
            MyVisualScriptLogicProvider.ShowNotification("Shields Are Down", 5000, "Red", Ownerid);
            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
            MyVisualScriptLogicProvider.SetGridGeneralDamageModifier(grid.Name, 1.0f);
            ShieldFX(false);
            shieldsup = false;
        }

        public float ApplyShieldDamage(float damage)
        {
            foreach (var j in jumpDrives)
            {

                if (j != null)
                {
                    if (j.IsFunctional && j.Enabled)
                    {
                        if(j.CurrentStoredPower <= damage * (AegisSystems.SHIELDCOEIFFIENT * AegisSystems.SHIELDMULTIPLIER))
                        {
                            damage -= j.CurrentStoredPower / (AegisSystems.SHIELDCOEIFFIENT* AegisSystems.SHIELDMULTIPLIER);
                            j.CurrentStoredPower = 0;
                            if (messagebuffer <= 0)
                            {
                                MyVisualScriptLogicProvider.ShowNotification("Jump Drive: " + j.CustomName + " Has Been Fully Drained", 5000, "Red", j.OwnerId);
                                messagebuffer = 10;
                            }
                            messagebuffer--;
                        } else
                        {
                            //MyAPIGateway.Utilities.ShowMessage("Aegis", "JD Charge: "+ jd.CurrentStoredPower+" - Damage: "+(damage * 0.001f));
                            j.CurrentStoredPower -= damage * (AegisSystems.SHIELDCOEIFFIENT * AegisSystems.SHIELDMULTIPLIER);
                            if(j.CurrentStoredPower <= 0.2)
                            {
                                if (messagebufferB <= 0)
                                {
                                    MyVisualScriptLogicProvider.ShowNotification("Warning Shield Integrity for " + j.CustomName + "Critically Low", 5000, "Yellow", j.OwnerId);
                                    messagebufferB = 20;
                                }
                                messagebufferB--;
                            }
                            return 0.0f;
                        }
                        
                    }
                }
            }
            return damage;
        }

        public float GetShieldStrength()
        {
            float shields = 0.0f;

            foreach (var j in jumpDrives)
            {
                if (j == null) continue;

                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled && !jd.CustomData.Contains("[NOSHIELD]"))
                    {
                        shields += jd.CurrentStoredPower;
                    }
                }
            }
            //shields = jumpDrives.Count*100.0f;
            return shields;
        }

        public static float GetShieldStrength(List<IMyJumpDrive> jumpdrives)
        {
            float shields = 0.0f;

            foreach (var j in jumpdrives)
            {
                
                if (j != null)
                {
                    if (j.IsFunctional && j.Enabled & !j.CustomData.Contains("[NOSHIELD]"))
                    {
                        shields += j.CurrentStoredPower;
                    }
                }
            }
            //shields = jumpdrives.Count*100.0f;
            return shields;
        }

        public static List<IMyTextPanel> GetPanels(IMyCubeGrid grid)
        {
            List<IMyTextPanel> textPanels = new List<IMyTextPanel>();

            if (grid == null)
                return textPanels;
            IEnumerable<IMyTextPanel> tp = grid.GetFatBlocks<IMyTextPanel>();
            foreach (var block in tp)
            {
                if(block.CustomData.Contains("[SHIELD]"))
                    textPanels.Add(block);

            }
            return textPanels;
        }
        public static List<IMyJumpDrive> GetJumpDrives(IMyCubeGrid grid)
        {
            
            List<IMyJumpDrive> jumpDrives = new List<IMyJumpDrive>();
            
            if (grid == null)
                return jumpDrives;
            IEnumerable<IMyJumpDrive> jd = grid.GetFatBlocks<IMyJumpDrive>();
            foreach (var block in jd)
            {
                if (!block.CustomData.Contains("[NOSHIELD]"))
                    jumpDrives.Add(block);

            }
            return jumpDrives;
        }



    }
}