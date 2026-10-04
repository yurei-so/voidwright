using System;
using System.Collections.Generic;

namespace Voidwright.Core
{
    public sealed class RoutePlanner
    {
        private struct State : IEquatable<State>
        {
            public readonly GridPoint Point;
            public readonly int ApproachX;
            public readonly int ApproachY;
            public readonly int ApproachZ;
            public readonly bool HasApproach;

            public State(GridPoint point, GridPoint? previous)
            {
                Point = point;
                HasApproach = previous.HasValue;
                ApproachX = previous.HasValue ? point.X - previous.Value.X : 0;
                ApproachY = previous.HasValue ? point.Y - previous.Value.Y : 0;
                ApproachZ = previous.HasValue ? point.Z - previous.Value.Z : 0;
            }

            public GridPoint? PreviousPoint()
                => HasApproach
                    ? new GridPoint(Point.X - ApproachX, Point.Y - ApproachY, Point.Z - ApproachZ)
                    : (GridPoint?)null;

            public bool Equals(State other)
                => Point == other.Point && HasApproach == other.HasApproach &&
                   ApproachX == other.ApproachX && ApproachY == other.ApproachY &&
                   ApproachZ == other.ApproachZ;

            public override bool Equals(object obj) => obj is State && Equals((State)obj);
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Point.GetHashCode();
                    hash = (hash * 397) ^ ApproachX;
                    hash = (hash * 397) ^ ApproachY;
                    hash = (hash * 397) ^ ApproachZ;
                    return (hash * 397) ^ (HasApproach ? 1 : 0);
                }
            }
        }

        private sealed class Node
        {
            public State State;
            public double Score;
            public long Order;
        }

        private sealed class MinHeap
        {
            private readonly List<Node> values = new List<Node>();
            public int Count => values.Count;
            public void Push(Node node)
            {
                values.Add(node);
                var index = values.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (Compare(values[parent], node) <= 0) break;
                    values[index] = values[parent];
                    index = parent;
                }
                values[index] = node;
            }
            public Node Pop()
            {
                var result = values[0];
                var tail = values[values.Count - 1];
                values.RemoveAt(values.Count - 1);
                if (values.Count == 0) return result;
                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= values.Count) break;
                    var right = left + 1;
                    var child = right < values.Count && Compare(values[right], values[left]) < 0 ? right : left;
                    if (Compare(values[child], tail) >= 0) break;
                    values[index] = values[child];
                    index = child;
                }
                values[index] = tail;
                return result;
            }
            private static int Compare(Node left, Node right)
            {
                var score = left.Score.CompareTo(right.Score);
                return score != 0 ? score : left.Order.CompareTo(right.Order);
            }
        }

        public Route FindRoute(ITraversalModel model, GridPoint start, GridPoint goal, int maximumExpandedNodes)
        {
            // Space Engineers' mod-script whitelist prohibits common exception
            // constructors. Invalid inputs therefore fail closed as bounded
            // route results instead of throwing from game-loaded code.
            if (model == null)
                return new Route(RouteStatus.InvalidEndpoint, new GridPoint[0], 0, 0);
            if (maximumExpandedNodes < 1)
                return new Route(RouteStatus.SearchBudgetExceeded, new GridPoint[0], 0, 0);
            if (!model.CanOccupy(start) || !model.CanOccupy(goal))
                return new Route(RouteStatus.InvalidEndpoint, new GridPoint[0], 0, 0);

            var frontier = new MinHeap();
            var startState = new State(start, null);
            var origins = new Dictionary<State, State>();
            var costs = new Dictionary<State, double> { [startState] = 0 };
            long order = 0;
            frontier.Push(new Node { State = startState, Score = model.EstimateRemainingCost(start, goal), Order = order++ });
            var expanded = 0;

            while (frontier.Count > 0)
            {
                var current = frontier.Pop();
                double currentCost;
                if (!costs.TryGetValue(current.State, out currentCost)) continue;
                var expected = currentCost + model.EstimateRemainingCost(current.State.Point, goal);
                if (current.Score > expected + 0.000001) continue;
                if (current.State.Point == goal)
                    return BuildRoute(startState, current.State, origins, currentCost, expanded);
                if (expanded >= maximumExpandedNodes)
                    return new Route(RouteStatus.SearchBudgetExceeded, new GridPoint[0], 0, expanded);
                expanded++;

                foreach (var edge in model.GetNeighbors(current.State.Point, current.State.PreviousPoint()))
                {
                    if (edge.Cost <= 0 || !model.CanOccupy(edge.Destination)) continue;
                    var nextState = new State(edge.Destination, current.State.Point);
                    var candidate = currentCost + edge.Cost;
                    double known;
                    if (costs.TryGetValue(nextState, out known) && candidate >= known - 0.000001) continue;
                    costs[nextState] = candidate;
                    origins[nextState] = current.State;
                    frontier.Push(new Node
                    {
                        State = nextState,
                        Score = candidate + model.EstimateRemainingCost(edge.Destination, goal),
                        Order = order++
                    });
                }
            }
            return new Route(RouteStatus.NoPath, new GridPoint[0], 0, expanded);
        }

        private static Route BuildRoute(State start, State goal, IDictionary<State, State> origins, double cost, int expanded)
        {
            var points = new List<GridPoint> { goal.Point };
            var current = goal;
            while (!current.Equals(start))
            {
                current = origins[current];
                points.Add(current.Point);
            }
            points.Reverse();
            return new Route(RouteStatus.Found, points, cost, expanded);
        }
    }
}
