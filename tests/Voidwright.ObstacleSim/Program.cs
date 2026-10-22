using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voidwright.Core;

internal static class Program
{
    private const double RangeMeters = 8.0;
    private const double RequiredClearanceMeters = 3.5;
    private const double TimeStepSeconds = 0.2;
    private const double MaximumSpeedMetersPerSecond = 0.45;
    private const double MaximumYawRateRadiansPerSecond = 1.2;
    private const double RoverRadiusMeters = 0.45;
    private const double ArrivalRadiusMeters = 0.5;
    private const int MaximumSteps = 4000;
    private static readonly double[] BeamAngles = Enumerable.Range(0, 13)
        .Select(index => (index * 10 - 60) * Math.PI / 180.0).ToArray();

    private static int Main()
    {
        var report = new SimulationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Model = "grid-world waypoint rover with ideal in-place turns; separate 2D kinematic depth-policy probe",
            PolicySource = "Data/Scripts/Voidwright/Core/DepthAvoidancePolicy.cs",
            Setup = new Dictionary<string, string>
            {
                ["alternative_path_grid"] = "31 x 25 cells; 1 m per cell; zero elevation",
                ["alternative_path_obstacle"] = "blocked cells x=13..17, z=8..12; direct route x=15 is blocked",
                ["alternative_path_vehicle"] = "LAND footprint half-width=1 cell, half-length=1 cell; independent swept rectangle half-width/length=1.25 m",
                ["alternative_path_start_goal"] = "start=(15,0,2), goal=(15,0,22), max expanded nodes=5000",
                ["no_path_grid"] = "9 x 11 cells; wall blocks every x at z=5; start=(4,0,1), goal=(4,0,9)",
                ["depth_policy"] = "independent synthetic probe: 13 beams from -60 to +60 degrees in 10 degree steps; range=8 m; required clearance=3.5 m"
            },
            Limits = new[]
            {
                "This is a synthetic policy simulation, not a Space Engineers engine or ModAPI run.",
                "Rover steering, acceleration, collision geometry, and depth-ray results are simplified and not calibrated to a game vehicle.",
                "The game-facing plugin adapter and server APIs are not loaded by this harness."
            },
            Scenarios = new[]
            {
                RunLandPlannerDetour(),
                RunLandPlannerNoPath(),
                RunBlockedCorridorStop(),
                RunUnlocatedTargetStop(),
                RunMalformedSensorStop()
            },
            ExploratoryDepthPolicyProbes = new[]
            {
                RunObstacleDetour(useFootprintSweep: false),
                RunObstacleDetour(useFootprintSweep: true)
            }
        };

        var repoRoot = Directory.GetCurrentDirectory();
        var artifactsDirectory = Path.Combine(repoRoot, "artifacts", "land-obstacle-corridor-2026-10-17");
        Directory.CreateDirectory(artifactsDirectory);
        var reportPath = Path.Combine(artifactsDirectory, "synthetic-simulation.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");

        foreach (var scenario in report.Scenarios)
            Console.WriteLine($"{(scenario.Passed ? "PASS" : "FAIL")} {scenario.Name}: {scenario.Result}");
        Console.WriteLine($"Obstacle simulation: {report.Scenarios.Count(scenario => scenario.Passed)}/{report.Scenarios.Length} scenarios passed.");
        foreach (var probe in report.ExploratoryDepthPolicyProbes)
            Console.WriteLine($"Depth-policy probe (non-gating): {probe.Name}: {probe.Result}; see diagnostic metrics.");
        Console.WriteLine($"Diagnostics: {Path.GetRelativePath(repoRoot, reportPath)}");
        WriteProbeArtifact(artifactsDirectory, "depth-policy-centerline-probe.json",
            "One centerline ray per heading; circular rover collision check",
            report.ExploratoryDepthPolicyProbes[0]);
        WriteProbeArtifact(artifactsDirectory, "depth-policy-footprint-probe.json",
            "Three lateral ray origins per heading across the modeled rover width; circular rover collision check",
            report.ExploratoryDepthPolicyProbes[1]);
        return report.Scenarios.All(scenario => scenario.Passed) ? 0 : 1;
    }

    private static void WriteProbeArtifact(string directory, string fileName, string sensorModel,
        ScenarioResult result)
    {
        var artifact = new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Model = "deterministic 2D kinematic rover; synthetic ray-to-axis-aligned-box depth sensor",
            PolicySource = "Data/Scripts/Voidwright/Core/DepthAvoidancePolicy.cs",
            SensorModel = sensorModel,
            Gating = false,
            Limitation = "Exploratory synthetic diagnostic only; not calibrated against Space Engineers physics, raycasts, or ModAPI.",
            Scenario = result
        };
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static ScenarioResult RunLandPlannerDetour()
    {
        const int width = 31;
        const int depth = 25;
        const int xOrigin = 15;
        var heights = new int[width, depth];
        var blocked = new bool[width, depth];
        for (var x = 13; x <= 17; x++)
        for (var z = 8; z <= 12; z++)
            blocked[x, z] = true;

        var vehicle = new VehicleProfile(MobilityClass.Land, 1, 0, 1, 0, 0, 0.25);
        var model = new LandTraversalModel(heights, blocked, vehicle);
        var start = new GridPoint(xOrigin, 0, 2);
        var goal = new GridPoint(xOrigin, 0, 22);
        var route = new RoutePlanner().FindRoute(model, start, goal, 5000);
        var follower = new RouteFollower();
        var trace = new List<StepDiagnostic>();
        var current = start;
        var heading = 0.0;
        var turns = 0;
        var commandedForwardSteps = 0;
        var minimumFootprintClearance = double.MaxValue;
        var maximumLateralDeviation = 0.0;
        var pathValid = route.Status == RouteStatus.Found && route.Points.Count >= 2;

        if (pathValid)
        {
            for (var index = 0; index < route.Points.Count; index++)
            {
                var waypoint = route.Points[index];
                if (!model.CanOccupy(waypoint)) pathValid = false;
                maximumLateralDeviation = Math.Max(maximumLateralDeviation, Math.Abs(waypoint.X - xOrigin));
                if (index == 0) continue;

                var previous = route.Points[index - 1];
                var dx = waypoint.X - previous.X;
                var dz = waypoint.Z - previous.Z;
                if (Math.Abs(dx) + Math.Abs(dz) != 1) pathValid = false;

                var worldRight = dx;
                var worldForward = dz;
                var localRight = worldRight * Math.Cos(heading) - worldForward * Math.Sin(heading);
                var localForward = worldRight * Math.Sin(heading) + worldForward * Math.Cos(heading);
                var guidance = follower.FollowLocalTarget(
                    MobilityClass.Land, localRight, 0, localForward, 0, 0.45, 0.1);
                if (guidance.Status != GuidanceStatus.Tracking ||
                    Math.Abs(guidance.Forward) > 1 || Math.Abs(guidance.Yaw) > 1)
                    pathValid = false;

                var nextHeading = Math.Atan2(dx, dz);
                if (Math.Abs(WrapRadians(nextHeading - heading)) > 0.01)
                {
                    turns++;
                    // The grid scenario models a low-speed rover that can turn in place.
                    // It does not claim a calibrated Space Engineers steering response.
                    if (Math.Abs(localForward) < 0.01 && guidance.Forward != 0)
                        pathValid = false;
                    heading = nextHeading;
                }
                else if (guidance.Forward > 0)
                {
                    commandedForwardSteps++;
                }

                for (var sample = 0; sample <= 4; sample++)
                {
                    var amount = sample / 4.0;
                    var x = (previous.X + dx * amount) - xOrigin;
                    var z = previous.Z + dz * amount;
                    var clearance = FootprintClearance(x, z, blocked, xOrigin, 1.25, 1.25);
                    minimumFootprintClearance = Math.Min(minimumFootprintClearance, clearance);
                    if (clearance < 0.20) pathValid = false;
                }

                current = waypoint;
                trace.Add(new StepDiagnostic
                {
                    Step = index,
                    X = waypoint.X - xOrigin,
                    Z = waypoint.Z,
                    HeadingDegrees = Round(heading * 180.0 / Math.PI),
                    TargetBearingDegrees = 0,
                    Decision = "PlannedWaypoint",
                    ChosenHeadingDegrees = Round(heading * 180.0 / Math.PI),
                    MinimumSensorClearanceMeters = null,
                    RoverSurfaceClearanceMeters = Round(minimumFootprintClearance),
                    TargetDistanceMeters = Round(Math.Abs(goal.X - current.X) + Math.Abs(goal.Z - current.Z)),
                    SpeedMetersPerSecond = 0
                });
            }
        }

        var directBlocked = Enumerable.Range(start.Z, goal.Z - start.Z + 1)
            .Any(z => blocked[xOrigin, z]);
        var arrived = current == goal;
        var passed = pathValid && directBlocked && arrived && maximumLateralDeviation >= 3 &&
                     minimumFootprintClearance >= 0.20 && commandedForwardSteps > 0;
        return new ScenarioResult
        {
            Name = "land planner follows an alternative around a blocking obstacle",
            Passed = passed,
            Result = route.Status.ToString(),
            Criteria = new[]
            {
                "the straight start-to-goal column intersects the obstacle",
                "planner returns adjacent land waypoints that the rover profile can occupy",
                "the simulated rover follows every waypoint and deviates at least 3 cells around the obstacle",
                "continuous rectangular footprint clearance stays at or above 0.20 m"
            },
            Metrics = new Dictionary<string, double?>
            {
                ["route_steps"] = Math.Max(0, route.Points.Count - 1),
                ["route_cost"] = route.Cost,
                ["expanded_nodes"] = route.ExpandedNodes,
                ["maximum_lateral_deviation_cells"] = maximumLateralDeviation,
                ["minimum_footprint_clearance_m"] = minimumFootprintClearance,
                ["turns"] = turns,
                ["forward_guidance_steps"] = commandedForwardSteps,
                ["destination_reached"] = arrived ? 1 : 0
            },
            Trace = trace.ToArray()
        };
    }

    private static ScenarioResult RunLandPlannerNoPath()
    {
        var heights = new int[9, 11];
        var blocked = new bool[9, 11];
        for (var x = 0; x < 9; x++) blocked[x, 5] = true;
        var vehicle = new VehicleProfile(MobilityClass.Land, 1, 0, 1, 0, 0, 0.25);
        var route = new RoutePlanner().FindRoute(
            new LandTraversalModel(heights, blocked, vehicle),
            new GridPoint(4, 0, 1), new GridPoint(4, 0, 9), 1000);
        var stopped = route.Status == RouteStatus.NoPath;
        return new ScenarioResult
        {
            Name = "full-width wall has no safe land route",
            Passed = stopped,
            Result = stopped ? "neutral-stop:NoPath" : route.Status.ToString(),
            Criteria = new[] { "planner returns NoPath and produces zero rover moves" },
            Metrics = new Dictionary<string, double?>
            {
                ["expanded_nodes"] = route.ExpandedNodes,
                ["rover_moves_after_no_path"] = 0,
                ["route_points"] = route.Points.Count
            }
        };
    }

    private static double FootprintClearance(double centerX, double centerZ, bool[,] blocked,
        int xOrigin, double halfWidth, double halfLength)
    {
        var minimum = double.MaxValue;
        for (var x = 0; x < blocked.GetLength(0); x++)
        for (var z = 0; z < blocked.GetLength(1); z++)
        {
            if (!blocked[x, z]) continue;
            var obstacleX = x - xOrigin;
            var deltaX = Math.Max(0, Math.Abs(centerX - obstacleX) - (halfWidth + 0.5));
            var deltaZ = Math.Max(0, Math.Abs(centerZ - z) - (halfLength + 0.5));
            if (deltaX == 0 && deltaZ == 0) return -0.01;
            minimum = Math.Min(minimum, Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ));
        }
        return minimum;
    }

    private static ScenarioResult RunObstacleDetour(bool useFootprintSweep)
    {
        var obstacle = new Box(-0.8, 0.8, 6.0, 8.0);
        var rover = new Rover { Position = new Point(0, 0), HeadingRadians = 0 };
        var target = new Point(0, 18);
        var rows = new List<StepDiagnostic>();
        var avoidCount = 0;
        var maxAbsX = 0.0;
        var minimumSurfaceClearance = double.MaxValue;
        var largestSpeed = 0.0;
        var terminal = "timeout";

        for (var step = 0; step < MaximumSteps; step++)
        {
            var bearing = RelativeBearing(rover.Position, rover.HeadingRadians, target);
            var clearances = Scan(rover, new[] { obstacle }, useFootprintSweep);
            var decision = DepthAvoidancePolicy.SelectHeading(
                bearing, BeamAngles, clearances, RangeMeters, RequiredClearanceMeters,
                rover.PreviousWasAvoid ? rover.PreviousChosenHeading : 0,
                rover.ConsecutiveClearFrames);
            if (decision.Status == DepthAvoidanceStatus.Avoid) avoidCount++;
            var distanceToTarget = Distance(rover.Position, target);
            var surfaceClearance = SurfaceClearance(rover.Position, obstacle, RoverRadiusMeters);
            minimumSurfaceClearance = Math.Min(minimumSurfaceClearance, surfaceClearance);
            maxAbsX = Math.Max(maxAbsX, Math.Abs(rover.Position.X));
            largestSpeed = Math.Max(largestSpeed, rover.SpeedMetersPerSecond);

            if (step % 20 == 0 || decision.Status != DepthAvoidanceStatus.Clear || distanceToTarget <= 1.5)
                rows.Add(Diagnostic(step, rover, target, bearing, decision, distanceToTarget, surfaceClearance));

            if (decision.Status != DepthAvoidanceStatus.Clear && decision.Status != DepthAvoidanceStatus.Avoid)
            {
                terminal = "stopped:" + decision.Status;
                break;
            }

            if (distanceToTarget <= ArrivalRadiusMeters && rover.SpeedMetersPerSecond <= 0.05)
            {
                terminal = "arrived";
                break;
            }

            Advance(rover, target, decision);
            var nextClearance = SurfaceClearance(rover.Position, obstacle, RoverRadiusMeters);
            minimumSurfaceClearance = Math.Min(minimumSurfaceClearance, nextClearance);
            maxAbsX = Math.Max(maxAbsX, Math.Abs(rover.Position.X));
            largestSpeed = Math.Max(largestSpeed, rover.SpeedMetersPerSecond);
            if (nextClearance < 0)
            {
                terminal = "collision";
                rows.Add(Diagnostic(step + 1, rover, target, bearing, decision,
                    Distance(rover.Position, target), nextClearance));
                break;
            }
            if (Distance(rover.Position, target) <= ArrivalRadiusMeters)
            {
                rover.SpeedMetersPerSecond = Math.Max(0, rover.SpeedMetersPerSecond - 0.8 * TimeStepSeconds);
                if (rover.SpeedMetersPerSecond <= 0.05)
                {
                    terminal = "arrived";
                    break;
                }
            }
        }

        var passed = terminal == "arrived" && avoidCount > 0 && maxAbsX >= 1.0 &&
                     minimumSurfaceClearance >= 0 && largestSpeed <= MaximumSpeedMetersPerSecond + 1e-9;
        return new ScenarioResult
        {
            Name = useFootprintSweep
                ? "reactive depth policy with footprint-swept sensor origins"
                : "reactive depth policy with centerline sensor origins",
            Passed = passed,
            Result = terminal,
            Criteria = new[]
            {
                "arrives within 0.5 m of destination and settles below 0.05 m/s",
                "at least one Avoid decision and at least 1.0 m lateral displacement",
                "rover footprint does not intersect the obstacle",
                "speed never exceeds 0.45 m/s"
            },
            Metrics = new Dictionary<string, double?>
            {
                ["avoid_decisions"] = avoidCount,
                ["maximum_abs_x_m"] = maxAbsX,
                ["minimum_rover_surface_clearance_m"] = minimumSurfaceClearance,
                ["maximum_speed_mps"] = largestSpeed,
                ["final_target_distance_m"] = Distance(rover.Position, target),
                ["final_speed_mps"] = rover.SpeedMetersPerSecond,
                ["steps"] = rows.Count == 0 ? 0 : rows[rows.Count - 1].Step
            },
            Trace = rows.ToArray()
        };
    }

    private static ScenarioResult RunBlockedCorridorStop()
    {
        var wall = new Box(-12, 12, 3, 4);
        var rover = new Rover { Position = new Point(0, 0), HeadingRadians = 0, SpeedMetersPerSecond = 0.2 };
        var target = new Point(0, 12);
        var bearing = RelativeBearing(rover.Position, rover.HeadingRadians, target);
        var clearances = Scan(rover, new[] { wall });
        var decision = DepthAvoidancePolicy.SelectHeading(
            bearing, BeamAngles, clearances, RangeMeters, RequiredClearanceMeters, 0);
        var neutral = decision.Status == DepthAvoidanceStatus.NoClearCorridor &&
                      !DepthAvoidancePolicy.IsNavigationPermitted(decision.Status);
        var result = new ScenarioResult
        {
            Name = "wall blocks every safe corridor",
            Passed = neutral,
            Result = neutral ? "neutral-stop:NoClearCorridor" : "unexpected:" + decision.Status,
            Criteria = new[]
            {
                "no three-beam corridor meets 3.4 m clearance",
                "policy status is NoClearCorridor so the game adapter must terminate with zero drive and steering"
            },
            Metrics = new Dictionary<string, double?>
            {
                ["minimum_scan_clearance_m"] = clearances.Min(),
                ["maximum_scan_clearance_m"] = clearances.Max(),
                ["starting_speed_mps"] = rover.SpeedMetersPerSecond,
                ["navigation_permitted"] = neutral ? 0 : 1,
                ["game_adapter_actuation_executed"] = 0
            },
            Trace = new[] { Diagnostic(0, rover, target, bearing, decision, Distance(rover.Position, target), SurfaceClearance(rover.Position, wall, RoverRadiusMeters)) }
        };
        return result;
    }

    private static ScenarioResult RunUnlocatedTargetStop()
    {
        var rover = new Rover { Position = new Point(0, 0), HeadingRadians = 0, SpeedMetersPerSecond = 0.2 };
        var target = new Point(Math.Sin(75 * Math.PI / 180.0) * 12, Math.Cos(75 * Math.PI / 180.0) * 12);
        var bearing = RelativeBearing(rover.Position, rover.HeadingRadians, target);
        var decision = DepthAvoidancePolicy.SelectHeading(
            bearing, BeamAngles, Enumerable.Repeat(RangeMeters, BeamAngles.Length).ToArray(),
            RangeMeters, RequiredClearanceMeters, 0);
        return new ScenarioResult
        {
            Name = "destination outside forward sensor fan",
            Passed = decision.Status == DepthAvoidanceStatus.TargetOutOfView &&
                     !DepthAvoidancePolicy.IsNavigationPermitted(decision.Status),
            Result = decision.Status == DepthAvoidanceStatus.TargetOutOfView
                ? "neutral-stop:TargetOutOfView" : "unexpected:" + decision.Status,
            Criteria = new[] { "target bearing beyond +/-60 degrees fails closed without issuing motion" },
            Metrics = new Dictionary<string, double?>
            {
                ["target_bearing_degrees"] = bearing * 180.0 / Math.PI,
                ["navigation_permitted"] = DepthAvoidancePolicy.IsNavigationPermitted(decision.Status) ? 1 : 0,
                ["game_adapter_actuation_executed"] = 0
            },
            Trace = new[] { Diagnostic(0, rover, target, bearing, decision, Distance(rover.Position, target), double.NaN) }
        };
    }

    private static ScenarioResult RunMalformedSensorStop()
    {
        var rover = new Rover { Position = new Point(0, 0), HeadingRadians = 0, SpeedMetersPerSecond = 0.2 };
        var target = new Point(0, 10);
        var clearances = Enumerable.Repeat(RangeMeters, BeamAngles.Length).ToArray();
        clearances[5] = double.NaN;
        var bearing = RelativeBearing(rover.Position, rover.HeadingRadians, target);
        var decision = DepthAvoidancePolicy.SelectHeading(
            bearing, BeamAngles, clearances, RangeMeters, RequiredClearanceMeters, 0);
        return new ScenarioResult
        {
            Name = "malformed depth scan",
            Passed = decision.Status == DepthAvoidanceStatus.InvalidScan &&
                     !DepthAvoidancePolicy.IsNavigationPermitted(decision.Status),
            Result = decision.Status == DepthAvoidanceStatus.InvalidScan
                ? "neutral-stop:InvalidScan" : "unexpected:" + decision.Status,
            Criteria = new[] { "NaN depth reading is rejected and the game adapter must issue no movement" },
            Metrics = new Dictionary<string, double?>
            {
                ["nan_beam_index"] = 5,
                ["navigation_permitted"] = DepthAvoidancePolicy.IsNavigationPermitted(decision.Status) ? 1 : 0,
                ["game_adapter_actuation_executed"] = 0
            },
            Trace = new[] { Diagnostic(0, rover, target, bearing, decision, Distance(rover.Position, target), double.NaN) }
        };
    }

    private static void Advance(Rover rover, Point target, DepthAvoidanceDecision decision)
    {
        var targetBearing = RelativeBearing(rover.Position, rover.HeadingRadians, target);
        var commandedHeading = Math.Abs(decision.HeadingRadians) < 1e-12 ? targetBearing : decision.HeadingRadians;
        var headingError = WrapRadians(commandedHeading);
        var steering = Clamp(headingError / (Math.PI / 2), -1, 1);
        rover.HeadingRadians = WrapRadians(rover.HeadingRadians + steering * MaximumYawRateRadiansPerSecond * TimeStepSeconds);

        // Bounded kinematics: steering reduces forward demand; acceleration is limited.
        var requestedSpeed = MaximumSpeedMetersPerSecond * Math.Max(0, 1.0 - Math.Abs(steering) * 0.6);
        var speedDelta = Clamp(requestedSpeed - rover.SpeedMetersPerSecond, -0.5 * TimeStepSeconds, 0.5 * TimeStepSeconds);
        rover.SpeedMetersPerSecond = Clamp(rover.SpeedMetersPerSecond + speedDelta, 0, MaximumSpeedMetersPerSecond);
        var forward = new Point(Math.Sin(rover.HeadingRadians), Math.Cos(rover.HeadingRadians));
        rover.Position = new Point(rover.Position.X + forward.X * rover.SpeedMetersPerSecond * TimeStepSeconds,
                                   rover.Position.Z + forward.Z * rover.SpeedMetersPerSecond * TimeStepSeconds);
        rover.PreviousChosenHeading = decision.HeadingRadians;
        rover.PreviousWasAvoid = decision.Status == DepthAvoidanceStatus.Avoid;
        rover.ConsecutiveClearFrames = decision.ConsecutiveClearFrames;
    }

    private static double[] Scan(Rover rover, IReadOnlyList<Box> obstacles, bool useFootprintSweep = true)
    {
        var forward = new Point(Math.Sin(rover.HeadingRadians), Math.Cos(rover.HeadingRadians));
        var right = new Point(Math.Cos(rover.HeadingRadians), -Math.Sin(rover.HeadingRadians));
        var centerOrigin = new Point(rover.Position.X + forward.X, rover.Position.Z + forward.Z);
        var lateralOffsets = useFootprintSweep
            ? new[] { -RoverRadiusMeters, 0.0, RoverRadiusMeters }
            : new[] { 0.0 };
        var clearances = new double[BeamAngles.Length];
        for (var index = 0; index < BeamAngles.Length; index++)
        {
            var angle = rover.HeadingRadians + BeamAngles[index];
            var direction = new Point(Math.Sin(angle), Math.Cos(angle));
            var nearest = RangeMeters;
            foreach (var lateralOffset in lateralOffsets)
            {
                var origin = new Point(centerOrigin.X + right.X * lateralOffset,
                                       centerOrigin.Z + right.Z * lateralOffset);
                for (var obstacleIndex = 0; obstacleIndex < obstacles.Count; obstacleIndex++)
                    nearest = Math.Min(nearest, RayBoxDistance(origin, direction, obstacles[obstacleIndex], RangeMeters));
            }
            clearances[index] = nearest;
        }
        return clearances;
    }

    private static double RayBoxDistance(Point origin, Point direction, Box box, double maximumRange)
    {
        var near = 0.0;
        var far = maximumRange;
        if (!ClipAxis(origin.X, direction.X, box.MinX, box.MaxX, ref near, ref far) ||
            !ClipAxis(origin.Z, direction.Z, box.MinZ, box.MaxZ, ref near, ref far) ||
            far < 0 || near > maximumRange)
            return maximumRange;
        return Math.Max(0, near);
    }

    private static bool ClipAxis(double origin, double direction, double minimum, double maximum,
        ref double near, ref double far)
    {
        if (Math.Abs(direction) < 1e-12) return origin >= minimum && origin <= maximum;
        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        if (first > second) (first, second) = (second, first);
        near = Math.Max(near, first);
        far = Math.Min(far, second);
        return near <= far;
    }

    private static double SurfaceClearance(Point position, Box obstacle, double roverRadius)
    {
        var closestX = Clamp(position.X, obstacle.MinX, obstacle.MaxX);
        var closestZ = Clamp(position.Z, obstacle.MinZ, obstacle.MaxZ);
        return Math.Sqrt((position.X - closestX) * (position.X - closestX) +
                         (position.Z - closestZ) * (position.Z - closestZ)) - roverRadius;
    }

    private static double RelativeBearing(Point position, double heading, Point target)
    {
        var deltaX = target.X - position.X;
        var deltaZ = target.Z - position.Z;
        var right = deltaX * Math.Cos(heading) - deltaZ * Math.Sin(heading);
        var forward = deltaX * Math.Sin(heading) + deltaZ * Math.Cos(heading);
        return Math.Atan2(right, forward);
    }

    private static StepDiagnostic Diagnostic(int step, Rover rover, Point target, double bearing,
        DepthAvoidanceDecision decision, double distance, double obstacleClearance)
        => new StepDiagnostic
        {
            Step = step,
            X = Round(rover.Position.X),
            Z = Round(rover.Position.Z),
            HeadingDegrees = Round(rover.HeadingRadians * 180.0 / Math.PI),
            TargetBearingDegrees = Round(bearing * 180.0 / Math.PI),
            Decision = decision.Status.ToString(),
            ChosenHeadingDegrees = Round(decision.HeadingRadians * 180.0 / Math.PI),
            MinimumSensorClearanceMeters = Round(decision.MinimumClearanceMeters),
            RoverSurfaceClearanceMeters = Round(obstacleClearance),
            TargetDistanceMeters = Round(distance),
            SpeedMetersPerSecond = Round(rover.SpeedMetersPerSecond)
        };

    private static double Distance(Point left, Point right)
        => Math.Sqrt((left.X - right.X) * (left.X - right.X) + (left.Z - right.Z) * (left.Z - right.Z));

    private static double WrapRadians(double angle)
    {
        while (angle > Math.PI) angle -= 2 * Math.PI;
        while (angle < -Math.PI) angle += 2 * Math.PI;
        return angle;
    }

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Max(minimum, Math.Min(maximum, value));

    private static double? Round(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? null : Math.Round(value, 4);

    private sealed class Rover
    {
        public Point Position;
        public double HeadingRadians;
        public double SpeedMetersPerSecond;
        public double PreviousChosenHeading;
        public bool PreviousWasAvoid;
        public int ConsecutiveClearFrames;
    }

    private readonly struct Point
    {
        public readonly double X;
        public readonly double Z;
        public Point(double x, double z) { X = x; Z = z; }
    }

    private readonly struct Box
    {
        public readonly double MinX;
        public readonly double MaxX;
        public readonly double MinZ;
        public readonly double MaxZ;
        public Box(double minX, double maxX, double minZ, double maxZ)
        { MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; }
    }

    private sealed class ScenarioResult
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string Result { get; set; } = "";
        public string[] Criteria { get; set; } = Array.Empty<string>();
        public Dictionary<string, double?> Metrics { get; set; } = new Dictionary<string, double?>();
        public StepDiagnostic[] Trace { get; set; } = Array.Empty<StepDiagnostic>();
    }

    private sealed class SimulationReport
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string Model { get; set; } = "";
        public string PolicySource { get; set; } = "";
        public Dictionary<string, string> Setup { get; set; } = new Dictionary<string, string>();
        public string[] Limits { get; set; } = Array.Empty<string>();
        public ScenarioResult[] Scenarios { get; set; } = Array.Empty<ScenarioResult>();
        public ScenarioResult[] ExploratoryDepthPolicyProbes { get; set; } = Array.Empty<ScenarioResult>();
    }

    private sealed class StepDiagnostic
    {
        public int Step { get; set; }
        public double? X { get; set; }
        public double? Z { get; set; }
        public double? HeadingDegrees { get; set; }
        public double? TargetBearingDegrees { get; set; }
        public string Decision { get; set; } = "";
        public double? ChosenHeadingDegrees { get; set; }
        public double? MinimumSensorClearanceMeters { get; set; }
        public double? RoverSurfaceClearanceMeters { get; set; }
        public double? TargetDistanceMeters { get; set; }
        public double? SpeedMetersPerSecond { get; set; }
    }
}
