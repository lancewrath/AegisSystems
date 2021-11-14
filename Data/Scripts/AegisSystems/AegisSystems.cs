using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRage.ModAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using System.Collections.Generic;
using System;
using VRageMath;

namespace RazMods
{


    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AegisSystems : MySessionComponentBase
    {
        List<MyGridShieldInfo> shieldedGrids = new List<MyGridShieldInfo>();
        bool bIsServer = false;
        bool binitialized = false;
        int delay = 0;

        public static float SHIELDCOEIFFIENT = 0.00002f;

        /*
        public override void BeforeStart()
        {
            base.BeforeStart();
        }
        */


        public override void UpdateBeforeSimulation()
        {
            if (!binitialized)
                Init();
            if (!bIsServer)
                return;
            //put some space between when we call these functions so it doesn't lag out the game
            delay++;

            if (delay == 60)
            {
                foreach (var grid in shieldedGrids)
                {               
                    grid.Update();
                    if (grid.shieldStrength <= 0.0f)
                    {
                        var cg = grid.grid as MyCubeGrid;
                        if (cg != null)
                        {
                            //cg.DestructibleBlocks = true;
                            if (grid.shieldsup)
                            {

                                //MyVisualScriptLogicProvider.ShowNotification(grid.grid.CustomName + " Aegis Offline!", 5000, "Red", grid.grid.BigOwners.ToArray()[0]);
                                //MyAPIGateway.Utilities.ShowNotification("Aegis System Offline", 5000);
                                //MyAPIGateway.Utilities.ShowMessage("Aegis", "Aegis System Offline");
                                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.grid.GetPosition());
                                grid.ShieldFX(false);
                            }

                        }
                    }
                    else
                    {
                        var cg = grid.grid as MyCubeGrid;
                        if (cg != null)
                        {
                            //If shields were down and we have at least one jump drive that is operational and at max power
                            if (!grid.shieldsup && grid.MaxPowerJumpDrives() > 0)
                            {
                                //MyVisualScriptLogicProvider.ShowNotification(grid.grid.CustomName+" Aegis System Online!", 5000, "Green", grid.grid.BigOwners.ToArray()[0]);
                                //MyAPIGateway.Utilities.ShowNotification("Aegis System Online", 5000);
                                //MyAPIGateway.Utilities.ShowMessage("Aegis", "Aegis System Online");
                                //MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("RAZWarpShield", grid.grid.GetPosition());
                                MyVisualScriptLogicProvider.CreateParticleEffectAtEntity("RAZWarpShield", grid.grid.Name);
                                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldUp", grid.grid.GetPosition());
                                grid.ShieldFX(true);
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

                if(GridHasJumpDrive(grid))
                {
                    shieldedGrids.Add(new MyGridShieldInfo(grid));
                }
            }
            //MyAPIGateway.Utilities.ShowNotification("Initialized", 5000);
            MyAPIGateway.Utilities.ShowMessage("Aegis", "Mod Initialized");
            MyAPIGateway.Entities.OnEntityAdd += CheckNewGrid;
            MyAPIGateway.Entities.OnEntityRemove += RemoveTheGrid;
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, ShieldHandler);
            
        }

        private void ShieldHandler(object target, ref MyDamageInformation info)
        {

            //MyAPIGateway.Utilities.ShowMessage("Aegis", "Damage: " + info.Amount + " target: " + target.GetType().ToString());
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
                                //info.Amount = 0;
                                var fat = block.FatBlock;
                                if(fat!=null)
                                {
                                    Vector3D fatcoord = block.FatBlock.GetPosition();
                                    //Vector3D hitCoords = new Vector3D(block.Position.X, block.Position.Y, block.Position.Z) - cg.GetPosition();
                                    MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("ShieldRazElectric", fatcoord);
                                    MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldHit", fatcoord);
                                }

                                
                            }
                        }
                    }
                }
                

                
            }
        }

        bool GridHasJumpDrive(IMyCubeGrid grid)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                if (block != null)
                {
                    var fat = block.FatBlock;
                    if (fat != null)
                    {
                        if (fat as IMyJumpDrive != null)
                        {
                            IMyJumpDrive jd = (IMyJumpDrive)fat;

                            if (jd != null)
                            {
                                //MyAPIGateway.Utilities.ShowNotification("Grid has jump drive", 5000);
                                //MyAPIGateway.Utilities.ShowMessage("Aegis", "Grid has jump drive");
                                grid.OnBlockAdded -= Grid_OnBlockAdded;
                                grid.OnBlockRemoved += Grid_OnBlockRemoved;
                                grid.OnBlockIntegrityChanged += Grid_OnBlockIntegrityChanged;

                                return true;


                            }
                        }
                    }
                }
            }
            grid.OnBlockAdded += Grid_OnBlockAdded;
            return false;
        }

        private void Grid_OnBlockIntegrityChanged(IMySlimBlock obj)
        {
            var fat = obj.FatBlock;
            if (fat != null)
            {
                if (fat as IMyJumpDrive != null)
                {
                    IMyJumpDrive jd = (IMyJumpDrive)fat;
                    if (jd != null)
                    {
                        if (!jd.IsFunctional)
                        {
                            //MyAPIGateway.Utilities.ShowNotification("Jump drive damaged", 5000);
                            //MyAPIGateway.Utilities.ShowMessage("Aegis",  jd.CustomName +" Jump drive damaged");
                            MyVisualScriptLogicProvider.ShowNotification("Jump Drive: " + jd.CustomName + " Is Damaged!", 5000, "Orange", jd.OwnerId);

                        }
                    }
                }
            }
        }

        public bool GetJumpDrivesWorking(IMyCubeGrid grid)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            
            foreach (var block in blocks)
            {
                var fat = block.FatBlock;
                if (fat != null)
                {
                    if (fat as IMyJumpDrive != null)
                    {
                        IMyJumpDrive jd = (IMyJumpDrive)fat;
                        if (jd != null)
                        {

                            if (jd.IsWorking)
                            {
                                return true;
                            }


                        }
                    }
                }
            }
                return false;
        }

        private void Grid_OnBlockRemoved(IMySlimBlock obj)
        {
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
                            sg.Update(true);
                        }
                    }
                    
                }
            }
        }

        private void Grid_OnBlockAdded(IMySlimBlock obj)
        {
            var fat = obj.FatBlock;
            if (fat != null)
            {
                if (fat as IMyJumpDrive != null)
                {
                    IMyJumpDrive jd = (IMyJumpDrive)fat;
                    if (jd != null)
                    {

                        //MyAPIGateway.Utilities.ShowNotification("Jump drive added", 5000);
                        //MyAPIGateway.Utilities.ShowMessage("Aegis", "Jump drive added");
                        MyVisualScriptLogicProvider.ShowNotification("Aegis Systems Firmware Installed into Jump Drive", 5000, "Green", jd.OwnerId);

                        if (shieldedGrids.Find(x => x.grid == obj.CubeGrid) == null)
                        {
                            shieldedGrids.Add(new MyGridShieldInfo(obj.CubeGrid));
                        }
                        MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == obj.CubeGrid);
                        if (sg != null)
                        {
                            sg.Update(true);
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
        public MyGridShieldInfo(IMyCubeGrid g)
        {
            grid = g;
            jumpDrives = GetJumpDrives(g);
            shieldStrength = GetShieldStrength(jumpDrives);
            grid.OnBlockIntegrityChanged += Grid_OnBlockIntegrityChanged;
        }

        public void Update(bool refreshJD = false)
        {

            jumpDrives = GetJumpDrives(grid);
            shieldStrength = GetShieldStrength(jumpDrives);
            if(shieldStrength<=0.0f && shieldsup)
            {
                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
                ShieldFX(false);
                shieldsup = false;
            }
        }

        public void ShieldFX(bool enable)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                if (block != null)
                {
                    var fat = block.FatBlock;
                    if (fat != null)
                    {
                       if(enable)
                       {
                            Vector3D fatcoord = block.FatBlock.GetPosition();
                            //Vector3D hitCoords = new Vector3D(block.Position.X, block.Position.Y, block.Position.Z) - cg.GetPosition();
                            MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("RazElectric", fatcoord);
                       } else
                       {
                            Vector3D fatcoord = block.FatBlock.GetPosition();
                            MyVisualScriptLogicProvider.CreateParticleEffectAtPosition("RazElectric", fatcoord);
                        }
                    }
                }
            }
        }

        public int MaxPowerJumpDrives()
        {
            int jdcount = 0;
            foreach (var j in jumpDrives)
            {
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled)
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
        private void Grid_OnBlockIntegrityChanged(IMySlimBlock obj)
        {
            //MyVisualScriptLogicProvider.ShowNotification("Block damage is: " + obj.AccumulatedDamage, 5000, "Red", obj.BuiltBy);
        }

        public void HandleDamage(IMySlimBlock block, ref MyDamageInformation info)
        {
            float shieldstrength = GetShieldStrength();
            
            if (shieldstrength > (info.Amount * AegisSystems.SHIELDCOEIFFIENT))
            {
                //block all damage               
                ApplyShieldDamage(info.Amount);
                info.Amount = 0;
                
            } else
            {
                //eat up whats left over to mitigate some of the damage
                OverLoad(ref info);
                
            }

            //MyAPIGateway.Utilities.ShowMessage("Aegis", "Shield Strength: " + shieldstrength + "Damage: " + info.Amount);
            //MyAPIGateway.Utilities.ShowMessage("Aegis", "Damage Deflected: " + info.Amount + " target: " + target.ToString());
        }

        public void OverLoad(ref MyDamageInformation info)
        {
            long Ownerid = 0;
            foreach (var j in jumpDrives)
            {
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled)
                    {
                       
                        info.Amount -= jd.CurrentStoredPower;
                        jd.CurrentStoredPower = 0;
                        
                    }
                    Ownerid = jd.OwnerId;
                }
            }
            MyVisualScriptLogicProvider.ShowNotification("Shields Are Down", 5000, "Red", Ownerid);
            MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("RazShieldDown", grid.GetPosition());
            ShieldFX(false);
            shieldsup = false;
        }

        public float ApplyShieldDamage(float damage)
        {
            foreach (var j in jumpDrives)
            {
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled)
                    {
                        if(jd.CurrentStoredPower <= damage * AegisSystems.SHIELDCOEIFFIENT)
                        {
                            damage -= jd.CurrentStoredPower / AegisSystems.SHIELDCOEIFFIENT;
                            jd.CurrentStoredPower = 0;
                            
                            MyVisualScriptLogicProvider.ShowNotification("Jump Drive: " + jd.CustomName + " Has Been Fully Drained", 5000, "Red", jd.OwnerId);
                        } else
                        {
                            //MyAPIGateway.Utilities.ShowMessage("Aegis", "JD Charge: "+ jd.CurrentStoredPower+" - Damage: "+(damage * 0.001f));
                            jd.CurrentStoredPower -= damage * AegisSystems.SHIELDCOEIFFIENT;
                            if(jd.CurrentStoredPower <= 0.2 && jd.CurrentStoredPower >= 0.19)
                            {
                                MyVisualScriptLogicProvider.ShowNotification("Warning Shield Integrity for " + jd.CustomName +"Critically Low", 5000, "Yellow", jd.OwnerId);
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
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    if (jd.IsFunctional && jd.Enabled)
                    {
                        shields += jd.CurrentStoredPower;
                    }
                }
            }
            //shields = jumpDrives.Count*100.0f;
            return shields;
        }

        public static float GetShieldStrength(List<IMyCubeBlock> jumpdrives)
        {
            float shields = 0.0f;

            foreach (var j in jumpdrives)
            {
                IMyJumpDrive jd = (IMyJumpDrive)j;
                if (jd != null)
                {
                    shields += jd.CurrentStoredPower;
                }
            }
            //shields = jumpdrives.Count*100.0f;
            return shields;
        }

        public static List<IMyCubeBlock> GetJumpDrives(IMyCubeGrid grid)
        {
            List<IMyCubeBlock> jumpDrives = new List<IMyCubeBlock>();
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                if (block != null)
                {
                    var fat = block.FatBlock;
                    if (fat != null)
                    {
                        if (fat as IMyJumpDrive != null)
                        {
                            jumpDrives.Add(fat);
                            
                        }
                    }
                }
            }
            return jumpDrives;
        }

        public List<IMyCubeBlock> jumpDrives;
        public IMyCubeGrid grid;
        public float shieldStrength = 0.0f;
        public bool shieldsup = false;

    }
}