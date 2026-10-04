using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Voidwright.Core;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Voidwright.Game
{
    public sealed class VehicleProbeSnapshot
    {
        public IMyRemoteControl Controller;
        public long ControllerId;
        public string ControllerName;
        public MobilityClassification Mobility;
        public int GridCount;
        public int BlockCount;
        public int WheelSuspensions;
        public int Thrusters;
        public Vector3D SizeMeters;
        public Vector3D Position;
        public Vector3D Forward;

        public string Signature()
        {
            return ControllerId + ":" + Mobility + ":" + WheelSuspensions + ":" + Thrusters;
        }

        public override string ToString()
        {
            return "controller='" + ControllerName + "' id=" + ControllerId +
                   " mobility=" + Mobility + " physicalGrids=" + GridCount +
                   " blocks=" + BlockCount + " wheels=" + WheelSuspensions +
                   " thrusters=" + Thrusters + " envelope=" +
                   Math.Round(SizeMeters.X, 1) + "x" + Math.Round(SizeMeters.Y, 1) + "x" +
                   Math.Round(SizeMeters.Z, 1) + "m position=" + Vector(Position) +
                   " forward=" + Vector(Forward);
        }

        private static string Vector(Vector3D value)
            => Math.Round(value.X, 3) + "," + Math.Round(value.Y, 3) + "," + Math.Round(value.Z, 3);
    }

    public sealed class VehicleProbe
    {
        public const string OptInMarker = "[Voidwright]";
        public const string ArmedMarker = "[Voidwright:Armed]";

        public List<VehicleProbeSnapshot> ObserveOptedInVehicles()
        {
            var snapshots = new List<VehicleProbeSnapshot>();
            if (MyAPIGateway.Entities == null) return snapshots;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
            var seenControllers = new HashSet<long>();

            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                var controllers = grid.GetFatBlocks<IMyRemoteControl>();
                foreach (var controller in controllers)
                {
                    if (controller == null || controller.MarkedForClose ||
                        controller.CustomName == null ||
                        !IsOptedIn(controller.CustomName) ||
                        !seenControllers.Add(controller.EntityId))
                        continue;
                    snapshots.Add(Observe(controller));
                }
            }
            return snapshots;
        }

        public static bool IsArmed(string name)
            => name != null && name.IndexOf(ArmedMarker, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsOptedIn(string name)
            => name != null && (name.IndexOf(OptInMarker, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf(ArmedMarker, StringComparison.OrdinalIgnoreCase) >= 0);

        private static VehicleProbeSnapshot Observe(IMyRemoteControl controller)
        {
            var grids = new List<IMyCubeGrid>();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(controller.CubeGrid, GridLinkTypeEnum.Physical, grids);
            if (grids.Count == 0) grids.Add(controller.CubeGrid);

            var minimum = new Vector3D(double.MaxValue);
            var maximum = new Vector3D(double.MinValue);
            var blocks = 0;
            var wheels = 0;
            var thrusters = 0;
            foreach (var grid in grids)
            {
                var box = grid.WorldAABB;
                minimum = Vector3D.Min(minimum, box.Min);
                maximum = Vector3D.Max(maximum, box.Max);
                var slimBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(slimBlocks, null);
                blocks += slimBlocks.Count;
                foreach (var ignored in grid.GetFatBlocks<IMyMotorSuspension>()) wheels++;
                foreach (var ignored in grid.GetFatBlocks<IMyThrust>()) thrusters++;
            }

            return new VehicleProbeSnapshot
            {
                Controller = controller,
                ControllerId = controller.EntityId,
                ControllerName = controller.CustomName,
                Mobility = MobilityClassifier.Classify(wheels, thrusters),
                GridCount = grids.Count,
                BlockCount = blocks,
                WheelSuspensions = wheels,
                Thrusters = thrusters,
                SizeMeters = maximum - minimum,
                Position = controller.GetPosition(),
                Forward = controller.WorldMatrix.Forward
            };
        }
    }
}
