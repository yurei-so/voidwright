using Voidwright.Core;

namespace Voidwright.AgentMcp;

public static class AgentWireframeMapper
{
    public static AgentWireframeModel Create(AgentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var model = new AgentWireframeModel
        {
            AgentId = snapshot.AgentId,
            CapturedSimulationTick = snapshot.CapturedSimulationTick,
            SourceSnapshotComplete = snapshot.GridCoverageComplete && snapshot.BlockCoverageComplete
        };

        foreach (var grid in snapshot.PhysicalGrids)
        foreach (var block in grid.Blocks)
        {
            var center = ToCore(Subtract(block.WorldCenter, snapshot.CorePosition),
                snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
            var right = ToCore(grid.Right, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
            var up = ToCore(grid.Up, snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
            var backward = ToCore(Negate(grid.Forward), snapshot.CoreRight, snapshot.CoreUp, snapshot.CoreForward);
            var halfX = Math.Max(1, block.Max[0] - block.Min[0] + 1) * grid.GridSizeMeters * 0.5;
            var halfY = Math.Max(1, block.Max[1] - block.Min[1] + 1) * grid.GridSizeMeters * 0.5;
            var halfZ = Math.Max(1, block.Max[2] - block.Min[2] + 1) * grid.GridSizeMeters * 0.5;
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
            {
                geometry.Corners.Add(new AgentWireframePoint(
                    center.X + right.X * halfX * xSign + up.X * halfY * ySign + backward.X * halfZ * zSign,
                    center.Y + right.Y * halfX * xSign + up.Y * halfY * ySign + backward.Y * halfZ * zSign,
                    center.Z + right.Z * halfX * xSign + up.Z * halfY * ySign + backward.Z * halfZ * zSign));
            }
            model.Blocks.Add(geometry);
        }
        return model;
    }

    private static double[] Subtract(double[] left, double[] right)
        => [left[0] - right[0], left[1] - right[1], left[2] - right[2]];

    private static double[] Negate(double[] value)
        => [-value[0], -value[1], -value[2]];

    private static AgentWireframePoint ToCore(
        double[] vector, double[] coreRight, double[] coreUp, double[] coreForward)
        => new(
            Dot(vector, coreRight),
            Dot(vector, coreUp),
            Dot(vector, coreForward));

    private static double Dot(double[] left, double[] right)
        => left[0] * right[0] + left[1] * right[1] + left[2] * right[2];
}
