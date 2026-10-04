using System;
using System.Collections.Generic;

namespace Voidwright.Core
{
    public sealed class AirTraversalModel : ITraversalModel
    {
        private static readonly int[,] Directions =
        {
            { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 },
            { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 }
        };
        private readonly bool[,,] blocked;
        private readonly VehicleProfile vehicle;

        public AirTraversalModel(bool[,,] blocked, VehicleProfile vehicle)
        {
            this.blocked = blocked ?? new bool[0, 0, 0];
            this.vehicle = vehicle ?? new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0);
        }

        public bool CanOccupy(GridPoint point)
        {
            for (var x = point.X - vehicle.HalfWidthCells; x <= point.X + vehicle.HalfWidthCells; x++)
            for (var y = point.Y - vehicle.HalfHeightCells; y <= point.Y + vehicle.HalfHeightCells; y++)
            for (var z = point.Z - vehicle.HalfLengthCells; z <= point.Z + vehicle.HalfLengthCells; z++)
            {
                if (!Inside(x, y, z) || blocked[x, y, z]) return false;
            }
            return true;
        }

        public IEnumerable<TraversalEdge> GetNeighbors(GridPoint point, GridPoint? previous)
        {
            for (var index = 0; index < Directions.GetLength(0); index++)
            {
                var next = new GridPoint(
                    point.X + Directions[index, 0],
                    point.Y + Directions[index, 1],
                    point.Z + Directions[index, 2]);
                var cost = 1.0;
                if (previous.HasValue)
                {
                    var beforeX = point.X - previous.Value.X;
                    var beforeY = point.Y - previous.Value.Y;
                    var beforeZ = point.Z - previous.Value.Z;
                    if (beforeX != Directions[index, 0] || beforeY != Directions[index, 1] || beforeZ != Directions[index, 2])
                        cost += vehicle.TurnPenalty;
                }
                yield return new TraversalEdge(next, cost);
            }
        }

        public double EstimateRemainingCost(GridPoint point, GridPoint goal)
            => Math.Abs(point.X - goal.X) + Math.Abs(point.Y - goal.Y) + Math.Abs(point.Z - goal.Z);

        private bool Inside(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 &&
               x < blocked.GetLength(0) && y < blocked.GetLength(1) && z < blocked.GetLength(2);
    }
}
