using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRage.ModAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using System.Collections.Generic;
using System;

namespace RazMods
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AegisSystems : MySessionComponentBase
    {
        List<MyGridShieldInfo> shieldedGrids = new List<MyGridShieldInfo>();
        bool bIsServer = false;
        bool binitialized = false;
        int delay = 0;
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
                                MyVisualScriptLogicProvider.ShowNotification("Jump Drive Aegis Offline!", 5000, "Red", grid.grid.BigOwners.ToArray()[0]);
                                MyAPIGateway.Utilities.ShowNotification("Jump drive offline", 5000);
                                MyAPIGateway.Utilities.ShowMessage("Aegis", "Jump drive offline");
                                grid.shieldsup = false;
                            }

                        }
                    } else
                    {
                        var cg = grid.grid as MyCubeGrid;
                        if (cg != null)
                        {
                            //cg.DestructibleBlocks = false;
                            if (!grid.shieldsup)
                            {
                                MyVisualScriptLogicProvider.ShowNotification("Jump Drive Aegis Online!", 5000, "Green", grid.grid.BigOwners.ToArray()[0]);
                                MyAPIGateway.Utilities.ShowNotification("Jump drive online", 5000);
                                MyAPIGateway.Utilities.ShowMessage("Aegis", "Jump drive online");
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

            MyAPIGateway.Utilities.ShowMessage("Aegis", "Damage: " + info.Amount + " target: " + target.GetType().ToString());
            if (target as IMySlimBlock != null)
            {
                var block = target as IMySlimBlock;
                if(block != null)
                {
                    var cg = block.CubeGrid;
                    MyGridShieldInfo sg = shieldedGrids.Find(x => x.grid == cg);
                    if(sg != null)
                    {
                        if(sg.shieldsup)
                        {

                            info.Amount = 0;
                            MyAPIGateway.Utilities.ShowMessage("Aegis", "Damage Deflected: " + info.Amount + " target: " + target.ToString());
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
                                MyAPIGateway.Utilities.ShowMessage("Aegis", "Grid has jump drive");
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
                        if (!jd.IsWorking)
                        {
                            //MyAPIGateway.Utilities.ShowNotification("Jump drive damaged", 5000);
                            //MyAPIGateway.Utilities.ShowMessage("Aegis", "Jump drive damaged");
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

                        if (GetJumpDrivesWorking(obj.CubeGrid))
                        {
                            //MyAPIGateway.Utilities.ShowNotification("Jump drive destroyed", 5000);
                            //MyAPIGateway.Utilities.ShowMessage("Aegis", "Jump drive destroyed");
                            MyVisualScriptLogicProvider.ShowNotification("Jump Drive: " + jd.CustomName + " Is Destroyed!", 5000, "red", jd.OwnerId);
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
                        MyVisualScriptLogicProvider.ShowNotification("Jump Drive Added", 5000, "Green", jd.OwnerId);

                        if (shieldedGrids.Find(x => x.grid == obj.CubeGrid) == null)
                            shieldedGrids.Add(new MyGridShieldInfo(obj.CubeGrid));


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
                shieldedGrids.Add(new MyGridShieldInfo(grid));
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

        public void Update()
        {

            jumpDrives = GetJumpDrives(grid);
            shieldStrength = GetShieldStrength(jumpDrives);
        }

        private void Grid_OnBlockIntegrityChanged(IMySlimBlock obj)
        {
            MyVisualScriptLogicProvider.ShowNotification("Block damage is: " + obj.AccumulatedDamage, 5000, "Red", obj.BuiltBy);
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
            shields /= jumpdrives.Count;
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

                    var objectbuilder = block.GetObjectBuilder();
                    if (objectbuilder.SubtypeName.Equals("LargeJumpDrive"))
                    {
                        var fobj = block.FatBlock;
                        jumpDrives.Add(fobj);
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