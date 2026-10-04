using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Voidwright.Core;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Voidwright.Game
{
    public sealed class RoutePreviewManager
    {
        private const int RadiusCells = 10;
        private const int DiameterCells = (RadiusCells * 2) + 1;
        private const int MaximumMarkers = 12;
        private readonly Dictionary<long, PreviewState> states = new Dictionary<long, PreviewState>();
        private readonly RoutePlanner planner = new RoutePlanner();

        private sealed class PreviewState
        {
            public string Signature;
            public readonly List<IMyGps> Markers = new List<IMyGps>();
        }

        public void Update(IList<VehicleProbeSnapshot> vehicles)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.GPS == null ||
                MyAPIGateway.Session.Player == null)
                return;

            var active = new HashSet<long>();
            foreach (var vehicle in vehicles)
            {
                if (vehicle.Controller == null || vehicle.Controller.MarkedForClose) continue;
                active.Add(vehicle.ControllerId);
                UpdateVehicle(vehicle);
            }

            var removed = new List<long>();
            foreach (var pair in states)
                if (!active.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed)
            {
                Clear(states[id]);
                states.Remove(id);
            }
        }

        public void ClearAll()
        {
            foreach (var state in states.Values) Clear(state);
            states.Clear();
        }

        private void UpdateVehicle(VehicleProbeSnapshot vehicle)
        {
            var waypoints = new List<MyWaypointInfo>();
            vehicle.Controller.GetWaypointInfo(waypoints);
            PreviewState state;
            if (!states.TryGetValue(vehicle.ControllerId, out state))
            {
                state = new PreviewState();
                states[vehicle.ControllerId] = state;
            }

            if (waypoints.Count == 0 ||
                vehicle.Mobility == MobilityClassification.Unknown)
            {
                if (state.Signature != null)
                {
                    Clear(state);
                    state.Signature = null;
                }
                return;
            }

            var destination = waypoints[0].Coords;
            var origin = vehicle.Controller.GetPosition();
            var cellSize = CalculateCellSize(vehicle.SizeMeters);
            var signature = BuildSignature(vehicle, origin, destination, cellSize);
            if (state.Signature == signature) return;

            Clear(state);
            state.Signature = signature;
            if (vehicle.Mobility == MobilityClassification.Hybrid)
            {
                PreviewRoute(state, vehicle, MobilityClassification.Land, "L", new Color(166, 119, 255), origin, destination, cellSize);
                PreviewRoute(state, vehicle, MobilityClassification.Air, "A", new Color(78, 205, 255), origin, destination, cellSize);
                return;
            }
            var label = vehicle.Mobility == MobilityClassification.Land ? "L" : "A";
            var color = vehicle.Mobility == MobilityClassification.Land
                ? new Color(166, 119, 255)
                : new Color(78, 205, 255);
            PreviewRoute(state, vehicle, vehicle.Mobility, label, color, origin, destination, cellSize);
        }

        private void PreviewRoute(PreviewState state, VehicleProbeSnapshot vehicle,
            MobilityClassification plannedMobility, string label, Color color,
            Vector3D origin, Vector3D destination, double cellSize)
        {
            var route = Plan(vehicle, plannedMobility, origin, destination, cellSize);
            if (route.Status == RouteStatus.Found)
                Render(state, vehicle, plannedMobility, label, color, route, origin, cellSize);
            MyLog.Default.WriteLineAndConsole("Voidwright preview: " + vehicle.ControllerName +
                                              " mode=" + plannedMobility + " status=" + route.Status +
                                              " points=" + route.Points.Count +
                                              " cost=" + Math.Round(route.Cost, 2));
        }

        private Route Plan(VehicleProbeSnapshot vehicle, MobilityClassification plannedMobility,
            Vector3D origin, Vector3D destination, double cellSize)
        {
            var matrix = vehicle.Controller.WorldMatrix;
            var relative = destination - origin;
            var goalX = ClampCell((int)Math.Round(Vector3D.Dot(relative, matrix.Right) / cellSize));
            var goalZ = ClampCell((int)Math.Round(Vector3D.Dot(relative, matrix.Forward) / cellSize));
            if (plannedMobility == MobilityClassification.Land)
            {
                var heights = new int[DiameterCells, DiameterCells];
                var blocked = new bool[DiameterCells, DiameterCells];
                var model = new LandTraversalModel(heights, blocked,
                    new VehicleProfile(MobilityClass.Land, 0, 0, 0, 1, 1, 0.15));
                return planner.FindRoute(model,
                    new GridPoint(RadiusCells, 0, RadiusCells),
                    new GridPoint(RadiusCells + goalX, 0, RadiusCells + goalZ), 2000);
            }

            var goalY = ClampCell((int)Math.Round(Vector3D.Dot(relative, matrix.Up) / cellSize));
            var air = new AirTraversalModel(new bool[DiameterCells, DiameterCells, DiameterCells],
                new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0.1));
            return planner.FindRoute(air,
                new GridPoint(RadiusCells, RadiusCells, RadiusCells),
                new GridPoint(RadiusCells + goalX, RadiusCells + goalY, RadiusCells + goalZ), 6000);
        }

        private static void Render(PreviewState state, VehicleProbeSnapshot vehicle,
            MobilityClassification plannedMobility, string label, Color color,
            Route route, Vector3D origin, double cellSize)
        {
            var matrix = vehicle.Controller.WorldMatrix;
            var stride = Math.Max(1, (int)Math.Ceiling((route.Points.Count - 1) / (double)MaximumMarkers));
            var number = 0;
            for (var index = stride; index < route.Points.Count; index += stride)
            {
                var point = route.Points[index];
                var verticalCells = plannedMobility == MobilityClassification.Land
                    ? point.Y
                    : point.Y - RadiusCells;
                var world = origin +
                            (matrix.Right * ((point.X - RadiusCells) * cellSize)) +
                            (matrix.Up * (verticalCells * cellSize)) +
                            (matrix.Forward * ((point.Z - RadiusCells) * cellSize));
                number++;
                var id = vehicle.ControllerId.ToString();
                var shortId = id.Length > 4 ? id.Substring(id.Length - 4) : id;
                var gps = MyAPIGateway.Session.GPS.Create(
                    "VW-" + label + number + " #" + shortId,
                    "Voidwright " + plannedMobility + " free-space horizon for " +
                    vehicle.ControllerName + "; collision sampling is not active yet.",
                    world, true, true);
                if (gps == null) continue;
                gps.GPSColor = color;
                MyAPIGateway.Session.GPS.AddLocalGps(gps);
                state.Markers.Add(gps);
            }
        }

        private static string BuildSignature(VehicleProbeSnapshot vehicle, Vector3D origin, Vector3D destination, double cellSize)
        {
            return vehicle.ControllerId + ":" + vehicle.Mobility + ":" +
                   Math.Round(origin.X / cellSize) + ":" + Math.Round(origin.Y / cellSize) + ":" +
                   Math.Round(origin.Z / cellSize) + ":" +
                   Math.Round(destination.X, 1) + ":" + Math.Round(destination.Y, 1) + ":" +
                   Math.Round(destination.Z, 1);
        }

        private static int ClampCell(int value)
        {
            if (value < -RadiusCells) return -RadiusCells;
            if (value > RadiusCells) return RadiusCells;
            return value;
        }

        private static double CalculateCellSize(Vector3D size)
        {
            var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            return Math.Max(5, Math.Ceiling((largest + 2) / 5) * 5);
        }

        private static void Clear(PreviewState state)
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.GPS != null)
                foreach (var marker in state.Markers)
                    MyAPIGateway.Session.GPS.RemoveLocalGps(marker);
            state.Markers.Clear();
        }
    }
}
