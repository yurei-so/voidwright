using System.Collections.Generic;

namespace Voidwright.Core
{
    public struct TraversalEdge
    {
        public readonly GridPoint Destination;
        public readonly double Cost;

        public TraversalEdge(GridPoint destination, double cost)
        {
            Destination = destination;
            Cost = cost;
        }
    }

    public interface ITraversalModel
    {
        bool CanOccupy(GridPoint point);
        IEnumerable<TraversalEdge> GetNeighbors(GridPoint point, GridPoint? previous);
        double EstimateRemainingCost(GridPoint point, GridPoint goal);
    }
}
