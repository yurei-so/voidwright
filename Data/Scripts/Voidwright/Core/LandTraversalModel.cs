using System;
using System.Collections.Generic;

namespace Voidwright.Core
{
    public sealed class LandTraversalModel : ITraversalModel
    {
        private static readonly int[,] Directions = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        private readonly int[,] heights;
        private readonly bool[,] blocked;
        private readonly VehicleProfile vehicle;

        public LandTraversalModel(int[,] heights, bool[,] blocked, VehicleProfile vehicle)
        {
            this.heights = heights ?? new int[0, 0];
            this.blocked = blocked ?? new bool[0, 0];
            this.vehicle = vehicle ?? new VehicleProfile(MobilityClass.Land, 0, 0, 0, 0, 0, 0);
        }

        public bool CanOccupy(GridPoint point)
        {
            if (!Inside(point.X, point.Z)) return false;
            var centerHeight = heights[point.X, point.Z];
            if (point.Y != centerHeight) return false;
            for (var x = point.X - vehicle.HalfWidthCells; x <= point.X + vehicle.HalfWidthCells; x++)
            for (var z = point.Z - vehicle.HalfLengthCells; z <= point.Z + vehicle.HalfLengthCells; z++)
            {
                if (!Inside(x, z) || blocked[x, z]) return false;
                if (Math.Abs(heights[x, z] - centerHeight) > vehicle.MaximumStepCells) return false;
            }
            return true;
        }

        public IEnumerable<TraversalEdge> GetNeighbors(GridPoint point, GridPoint? previous)
        {
            for (var index = 0; index < Directions.GetLength(0); index++)
            {
                var x = point.X + Directions[index, 0];
                var z = point.Z + Directions[index, 1];
                if (!Inside(x, z)) continue;
                var next = new GridPoint(x, heights[x, z], z);
                var rise = Math.Abs(next.Y - point.Y);
                if (rise > vehicle.MaximumStepCells || rise > vehicle.MaximumSlope) continue;
                var cost = 1.0 + rise;
                if (previous.HasValue)
                {
                    var beforeX = point.X - previous.Value.X;
                    var beforeZ = point.Z - previous.Value.Z;
                    if (beforeX != Directions[index, 0] || beforeZ != Directions[index, 1])
                        cost += vehicle.TurnPenalty;
                }
                yield return new TraversalEdge(next, cost);
            }
        }

        public double EstimateRemainingCost(GridPoint point, GridPoint goal)
            => Math.Abs(point.X - goal.X) + Math.Abs(point.Z - goal.Z);

        private bool Inside(int x, int z)
            => x >= 0 && z >= 0 &&
               x < heights.GetLength(0) && z < heights.GetLength(1) &&
               x < blocked.GetLength(0) && z < blocked.GetLength(1);
    }
}
