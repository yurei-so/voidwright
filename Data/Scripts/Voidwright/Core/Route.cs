using System.Collections.Generic;

namespace Voidwright.Core
{
    public enum RouteStatus { Found, NoPath, SearchBudgetExceeded, InvalidEndpoint }

    public sealed class Route
    {
        public RouteStatus Status { get; private set; }
        public IReadOnlyList<GridPoint> Points { get; private set; }
        public double Cost { get; private set; }
        public int ExpandedNodes { get; private set; }

        public Route(RouteStatus status, IList<GridPoint> points, double cost, int expandedNodes)
        {
            Status = status;
            Points = new List<GridPoint>(points).AsReadOnly();
            Cost = cost;
            ExpandedNodes = expandedNodes;
        }
    }
}
