using System;
using System.Linq;
using Voidwright.Core;

static class Program
{
    private static int passed;

    static void Main()
    {
        Run("land routes around vehicle-sized obstruction", LandRoutesAroundObstacle);
        Run("land rejects excessive step", LandRejectsStep);
        Run("air routes over wall in 3D", AirRoutesOverWall);
        Run("air respects vehicle clearance", AirRespectsClearance);
        Run("planner reports budget exhaustion", BudgetIsBounded);
        Run("planner preserves approach heading", PlannerPreservesApproachHeading);
        Run("invalid input fails closed", InvalidInputFailsClosed);
        Run("mobility classification is conservative", MobilityClassificationIsConservative);
        Run("land guidance points toward target", LandGuidancePointsTowardTarget);
        Run("land guidance turns before accelerating", LandGuidanceTurnsBeforeAccelerating);
        Run("air guidance preserves three-dimensional intent", AirGuidanceIsThreeDimensional);
        Run("guidance stops inside arrival radius", GuidanceStopsAtArrival);
        Run("guidance rejects non-finite input", GuidanceRejectsNonFiniteInput);
        Run("guidance fuzz remains bounded and deterministic", GuidanceFuzzIsBoundedAndDeterministic);
        Console.WriteLine($"Voidwright core: {passed}/14 checks passed.");
    }

    private static void LandRoutesAroundObstacle()
    {
        var heights = new int[9, 7];
        var blocked = new bool[9, 7];
        for (var z = 1; z < 6; z++) blocked[4, z] = true;
        var vehicle = new VehicleProfile(MobilityClass.Land, 0, 0, 0, 1, 1, 0.25);
        var route = new RoutePlanner().FindRoute(
            new LandTraversalModel(heights, blocked, vehicle),
            new GridPoint(1, 0, 3), new GridPoint(7, 0, 3), 500);
        Check(route.Status == RouteStatus.Found, "route was not found");
        Check(route.Points.All(point => !blocked[point.X, point.Z]), "route crosses obstruction");
    }

    private static void LandRejectsStep()
    {
        var heights = new int[3, 1];
        heights[1, 0] = 2;
        var vehicle = new VehicleProfile(MobilityClass.Land, 0, 0, 0, 1, 1, 0);
        var route = new RoutePlanner().FindRoute(
            new LandTraversalModel(heights, new bool[3, 1], vehicle),
            new GridPoint(0, 0, 0), new GridPoint(2, 0, 0), 20);
        Check(route.Status == RouteStatus.NoPath, "vehicle climbed an excessive step");
    }

    private static void AirRoutesOverWall()
    {
        var blocked = new bool[7, 5, 3];
        for (var y = 0; y < 3; y++) for (var z = 0; z < 3; z++) blocked[3, y, z] = true;
        var vehicle = new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0.1);
        var route = new RoutePlanner().FindRoute(
            new AirTraversalModel(blocked, vehicle),
            new GridPoint(1, 1, 1), new GridPoint(5, 1, 1), 1000);
        Check(route.Status == RouteStatus.Found, "3D route was not found");
        Check(route.Points.Any(point => point.Y >= 3), "route did not use vertical clearance");
    }

    private static void AirRespectsClearance()
    {
        var blocked = new bool[7, 5, 5];
        for (var y = 0; y < 5; y++)
        {
            blocked[3, y, 0] = true;
            blocked[3, y, 2] = true;
            blocked[3, y, 3] = true;
            blocked[3, y, 4] = true;
        }
        var vehicle = new VehicleProfile(MobilityClass.Air, 0, 0, 1, 0, 0, 0);
        var route = new RoutePlanner().FindRoute(
            new AirTraversalModel(blocked, vehicle),
            new GridPoint(1, 2, 2), new GridPoint(5, 2, 2), 1000);
        Check(route.Status == RouteStatus.NoPath, "oversized vehicle used a one-cell gap");
    }

    private static void BudgetIsBounded()
    {
        var model = new AirTraversalModel(new bool[20, 20, 20],
            new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0));
        var route = new RoutePlanner().FindRoute(model,
            new GridPoint(0, 0, 0), new GridPoint(19, 19, 19), 1);
        Check(route.Status == RouteStatus.SearchBudgetExceeded, "budget was not enforced");
        Check(route.ExpandedNodes == 1, "expanded node count is inaccurate");
    }

    private static void PlannerPreservesApproachHeading()
    {
        var route = new RoutePlanner().FindRoute(
            new HeadingTrapModel(),
            new GridPoint(0, 0, 0), new GridPoint(2, 0, 0), 20);
        Check(route.Status == RouteStatus.Found, "heading-aware route was not found");
        Check(Math.Abs(route.Cost - 3) < 0.000001, "planner discarded the better arrival heading");
        Check(route.Points.Count == 4 && route.Points[1] == new GridPoint(0, 0, 1),
            "planner selected the cheap arrival with the expensive exit");
    }

    private static void InvalidInputFailsClosed()
    {
        var planner = new RoutePlanner();
        var point = new GridPoint(0, 0, 0);
        Check(planner.FindRoute(null!, point, point, 10).Status == RouteStatus.InvalidEndpoint,
            "null model did not fail closed");
        var model = new AirTraversalModel(new bool[1, 1, 1],
            new VehicleProfile(MobilityClass.Air, -1, -1, -1, -1, -1, -1));
        Check(planner.FindRoute(model, point, point, 0).Status == RouteStatus.SearchBudgetExceeded,
            "invalid budget did not fail closed");
    }

    private static void MobilityClassificationIsConservative()
    {
        Check(MobilityClassifier.Classify(0, 0) == MobilityClassification.Unknown, "empty grid was classified");
        Check(MobilityClassifier.Classify(1, 0) == MobilityClassification.Unknown, "single wheel was called LAND");
        Check(MobilityClassifier.Classify(4, 0) == MobilityClassification.Land, "rover was not LAND");
        Check(MobilityClassifier.Classify(0, 6) == MobilityClassification.Air, "thruster craft was not AIR");
        Check(MobilityClassifier.Classify(4, 6) == MobilityClassification.Hybrid, "hybrid evidence was flattened");
    }

    private static void LandGuidancePointsTowardTarget()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 2, 0, 10, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Tracking, "land target was not tracked");
        Check(command.Forward > 0, "land guidance did not request forward motion");
        Check(command.Yaw > 0, "right-hand target did not request a right turn");
        Check(command.Right == 0 && command.Up == 0, "land guidance leaked lateral thrust");
    }

    private static void LandGuidanceTurnsBeforeAccelerating()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 10, 0, 0, 0, 20, 1);
        Check(command.Yaw == 1, "side target did not request maximum bounded steering");
        Check(command.Forward == 0, "land guidance accelerated while perpendicular to target");
    }

    private static void AirGuidanceIsThreeDimensional()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Air, 4, 3, 12, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Tracking, "air target was not tracked");
        Check(command.Forward > 0 && command.Right > 0 && command.Up > 0,
            "air guidance lost an axis");
        Check(Math.Abs(command.Forward) <= 1 && Math.Abs(command.Right) <= 1 &&
              Math.Abs(command.Up) <= 1 && Math.Abs(command.Yaw) <= 1,
            "air guidance escaped normalized bounds");
    }

    private static void GuidanceStopsAtArrival()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 0.2, 0, 0.2, 3, 20, 1);
        Check(command.Status == GuidanceStatus.Arrived, "arrival was not recognized");
        Check(command.Forward == 0 && command.Yaw == 0, "arrival command was not neutral");
    }

    private static void GuidanceRejectsNonFiniteInput()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Air, double.NaN, 0, 10, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Invalid, "non-finite target was accepted");
        Check(command.Forward == 0 && command.Right == 0 && command.Up == 0 && command.Yaw == 0,
            "invalid guidance was not neutral");
    }

    private static void GuidanceFuzzIsBoundedAndDeterministic()
    {
        var random = new Random(8826);
        var follower = new RouteFollower();
        for (var index = 0; index < 10000; index++)
        {
            var mobility = index % 2 == 0 ? MobilityClass.Land : MobilityClass.Air;
            var right = (random.NextDouble() * 2000) - 1000;
            var up = (random.NextDouble() * 2000) - 1000;
            var forward = (random.NextDouble() * 2000) - 1000;
            var speed = (random.NextDouble() * 200) - 50;
            var first = follower.FollowLocalTarget(mobility, right, up, forward, speed, 100, 0.5);
            var second = follower.FollowLocalTarget(mobility, right, up, forward, speed, 100, 0.5);
            Check(first.Status == second.Status && first.Forward == second.Forward &&
                  first.Right == second.Right && first.Up == second.Up && first.Yaw == second.Yaw,
                "guidance changed for identical input");
            Check(Math.Abs(first.Forward) <= 1 && Math.Abs(first.Right) <= 1 &&
                  Math.Abs(first.Up) <= 1 && Math.Abs(first.Yaw) <= 1,
                "fuzz guidance escaped normalized bounds");
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HeadingTrapModel : ITraversalModel
    {
        private static readonly GridPoint Start = new GridPoint(0, 0, 0);
        private static readonly GridPoint Junction = new GridPoint(1, 0, 0);
        private static readonly GridPoint Turn = new GridPoint(0, 0, 1);
        private static readonly GridPoint Goal = new GridPoint(2, 0, 0);

        public bool CanOccupy(GridPoint point)
            => point == Start || point == Junction || point == Turn || point == Goal;

        public System.Collections.Generic.IEnumerable<TraversalEdge> GetNeighbors(
            GridPoint point, GridPoint? previous)
        {
            if (point == Start)
            {
                yield return new TraversalEdge(Junction, 1);
                yield return new TraversalEdge(Turn, 1);
            }
            else if (point == Turn)
            {
                yield return new TraversalEdge(Junction, 1);
            }
            else if (point == Junction)
            {
                yield return new TraversalEdge(Goal, previous.HasValue && previous.Value == Turn ? 1 : 100);
            }
        }

        public double EstimateRemainingCost(GridPoint point, GridPoint goal) => 0;
    }
}
