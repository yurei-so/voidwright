using System;
using VRageMath;
using Voidwright.Core;

namespace Voidwright.Game
{
    /// <summary>Projects the latest game snapshot into the transport-neutral wireframe query model.</summary>
    public static class AgentWireframeSnapshotFactory
    {
        public static AgentWireframeModel Create(AgentCoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            var model = new AgentWireframeModel
            {
                AgentId = snapshot.AgentId,
                CapturedSimulationTick = snapshot.CapturedSimulationTick,
                SourceSnapshotComplete = snapshot.IsComplete
            };
            foreach (var grid in snapshot.PhysicalGrids)
            foreach (var block in grid.Blocks)
            {
                var centerOffset = block.WorldCenter - snapshot.CorePosition;
                var center = ToCore(centerOffset, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
                var right = ToCore(grid.Right, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
                var up = ToCore(grid.Up, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
                // Grid local +Z is the grid's backward axis; the snapshot stores forward.
                var backward = ToCore(-grid.Forward, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
                var halfX = Math.Max(1, block.Max.X - block.Min.X + 1) * grid.GridSizeMeters * 0.5;
                var halfY = Math.Max(1, block.Max.Y - block.Min.Y + 1) * grid.GridSizeMeters * 0.5;
                var halfZ = Math.Max(1, block.Max.Z - block.Min.Z + 1) * grid.GridSizeMeters * 0.5;
                var geometry = new AgentWireframeBlock
                {
                    GridEntityId = grid.EntityId,
                    BlockEntityId = block.EntityId,
                    DefinitionType = block.DefinitionType,
                    SubtypeId = block.SubtypeId,
                    DamageRatio = block.DamageRatio,
                    IsFunctional = block.IsFunctional,
                    HasIntegrityData = true
                };
                for (var xSign = -1; xSign <= 1; xSign += 2)
                for (var ySign = -1; ySign <= 1; ySign += 2)
                for (var zSign = -1; zSign <= 1; zSign += 2)
                    geometry.Corners.Add(new AgentWireframePoint(
                        center.X + right.X * halfX * xSign + up.X * halfY * ySign + backward.X * halfZ * zSign,
                        center.Y + right.Y * halfX * xSign + up.Y * halfY * ySign + backward.Y * halfZ * zSign,
                        center.Z + right.Z * halfX * xSign + up.Z * halfY * ySign + backward.Z * halfZ * zSign));
                model.Blocks.Add(geometry);
            }
            return model;
        }

        private static AgentWireframePoint ToCore(
            Vector3D vector,
            Vector3D coreRight,
            Vector3D coreUp,
            Vector3D coreForward)
            => new AgentWireframePoint(
                Vector3D.Dot(vector, coreRight),
                Vector3D.Dot(vector, coreUp),
                Vector3D.Dot(vector, coreForward));
    }
}
