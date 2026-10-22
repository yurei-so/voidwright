using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Server-only read path for a player-consented, exact-grid capture. It has
    /// no command, movement, or actuator-write methods. Consent entry points are
    /// deliberately separate so only a trusted UI/service can call TryStart.
    /// </summary>
    internal sealed class DrivingLabPassiveTelemetryAdapter
    {
        private sealed class Frame
        {
            public string Json;
            public int ByteCount;
            public DateTime CapturedAtUtc;
        }

        private DrivingLabPassiveTelemetrySelection selection;
        private readonly List<Frame> frames = new List<Frame>();
        private DateTime lastCaptureUtc;
        private long sequence;
        private long droppedRecords;
        private int bufferedBytes;

        public bool IsActive { get { return selection != null && !selection.IsRevoked; } }

        public bool TryStart(
            IMyPlayer authenticatedPlayer,
            string sessionId,
            string runId,
            long gridEntityId,
            DateTime nowUtc,
            DateTime expiresAtUtc,
            int maximumSampleRateHz,
            int maximumBufferedRecords,
            int maximumBufferedBytes)
        {
            DrivingLabPassiveTelemetryRejection ignored;
            return TryStart(authenticatedPlayer, sessionId, runId, gridEntityId, nowUtc, expiresAtUtc,
                maximumSampleRateHz, maximumBufferedRecords, maximumBufferedBytes, out ignored);
        }

        public bool TryStart(
            IMyPlayer authenticatedPlayer,
            string sessionId,
            string runId,
            long gridEntityId,
            DateTime nowUtc,
            DateTime expiresAtUtc,
            int maximumSampleRateHz,
            int maximumBufferedRecords,
            int maximumBufferedBytes,
            out DrivingLabPassiveTelemetryRejection rejection)
        {
            if (authenticatedPlayer == null || authenticatedPlayer.IsBot || authenticatedPlayer.IdentityId <= 0 ||
                selection != null)
            {
                rejection = DrivingLabPassiveTelemetryRejection.InvalidIdentity;
                return false;
            }

            rejection = CreateBoundSelection(authenticatedPlayer, sessionId, runId, gridEntityId,
                nowUtc, expiresAtUtc, maximumSampleRateHz, maximumBufferedRecords, maximumBufferedBytes,
                out var bound);
            if (rejection != DrivingLabPassiveTelemetryRejection.None || bound == null) return false;

            selection = bound;
            frames.Clear();
            sequence = 0;
            droppedRecords = 0;
            bufferedBytes = 0;
            lastCaptureUtc = DateTime.MinValue;
            return true;
        }

        public DrivingLabPassiveTelemetryRejection Diagnose(
            IMyPlayer authenticatedPlayer, string sessionId, string runId, long gridEntityId, DateTime nowUtc)
        {
            if (authenticatedPlayer == null || authenticatedPlayer.IsBot || authenticatedPlayer.IdentityId <= 0 ||
                selection != null)
                return DrivingLabPassiveTelemetryRejection.InvalidIdentity;

            return CreateBoundSelection(authenticatedPlayer, sessionId, runId, gridEntityId,
                nowUtc, nowUtc.ToUniversalTime().Add(DrivingLabPassiveTelemetryPolicy.MaximumConsentLifetime),
                DrivingLabPassiveTelemetryPolicy.MaximumSampleRateHz,
                DrivingLabPassiveTelemetryPolicy.MaximumBufferedRecords,
                DrivingLabPassiveTelemetryPolicy.MaximumBufferedBytes, out _);
        }

        private static DrivingLabPassiveTelemetryRejection CreateBoundSelection(
            IMyPlayer authenticatedPlayer, string sessionId, string runId, long gridEntityId,
            DateTime nowUtc, DateTime expiresAtUtc, int maximumSampleRateHz,
            int maximumBufferedRecords, int maximumBufferedBytes,
            out DrivingLabPassiveTelemetrySelection bound)
        {
            bound = null;
            var request = new DrivingLabPassiveConsentRequest
            {
                ExplicitOptIn = true,
                AuthenticatedPlayerIdentityId = authenticatedPlayer.IdentityId,
                SessionId = sessionId,
                RunId = runId,
                GridEntityId = gridEntityId,
                ConfirmedAtUtc = nowUtc.ToUniversalTime(),
                ExpiresAtUtc = expiresAtUtc.ToUniversalTime(),
                MaximumSampleRateHz = maximumSampleRateHz,
                MaximumBufferedRecords = maximumBufferedRecords,
                MaximumBufferedBytes = maximumBufferedBytes
            };
            var inventory = ReadIdentityInventory(sessionId, runId, gridEntityId,
                authenticatedPlayer.IdentityId, nowUtc);
            return DrivingLabPassiveTelemetryPolicy.TryBind(request, inventory, nowUtc, out bound);
        }

        public bool Revoke(long authenticatedPlayerIdentityId)
        {
            if (selection == null || selection.PlayerIdentityId != authenticatedPlayerIdentityId) return false;
            selection.Revoke();
            ExportAndClear();
            return true;
        }

        public void Update()
        {
            var current = selection;
            if (current == null) return;
            var now = DateTime.UtcNow;
            if (current.IsRevoked || now >= current.ExpiresAtUtc)
            {
                ExportAndClear();
                return;
            }

            var inventory = ReadIdentityInventory(current.SessionId, current.RunId, current.GridEntityId,
                current.PlayerIdentityId, now);
            var binding = DrivingLabPassiveTelemetryPolicy.ValidateCurrentBinding(current, inventory, now);
            if (binding != DrivingLabPassiveTelemetryRejection.None)
            {
                current.Revoke();
                ExportAndClear();
                return;
            }

            var spacing = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + current.MaximumSampleRateHz - 1) /
                                             current.MaximumSampleRateHz);
            if (lastCaptureUtc != DateTime.MinValue && now - lastCaptureUtc < spacing) return;
            lastCaptureUtc = now;

            try
            {
                var frame = ObserveExactGrid(current, now);
                if (frame == null)
                {
                    droppedRecords++;
                    return;
                }
                if (!DrivingLabPassiveTelemetryPolicy.CanBuffer(
                        current, frames.Count, bufferedBytes, frame.ByteCount))
                {
                    droppedRecords++;
                    return;
                }
                frames.Add(frame);
                bufferedBytes += frame.ByteCount;
                sequence++;
            }
            catch (Exception exception)
            {
                droppedRecords++;
                if (exception is OutOfMemoryException || exception is StackOverflowException) throw;
                MyLog.Default.WriteLineAndConsole("[VRageCage][DRIVING-LAB][PASSIVE] sample unavailable: " +
                                                  exception.GetType().Name + ".");
            }
        }

        public void StopAndExport()
        {
            if (selection == null) return;
            selection.Revoke();
            ExportAndClear();
        }

        private static DrivingLabPassiveIdentityInventory ReadIdentityInventory(
            string sessionId, string runId, long gridEntityId, long authenticatedPlayerIdentityId, DateTime nowUtc)
        {
            var result = new DrivingLabPassiveIdentityInventory
            {
                SessionId = sessionId,
                RunId = runId,
                CapturedAtUtc = nowUtc.ToUniversalTime(),
                SimulationTick = MyAPIGateway.Session == null ? -1 : MyAPIGateway.Session.GameplayFrameCounter,
                Complete = false,
                IdentityVerified = false,
                GridEntityIds = new long[0],
                ConnectedHumanIdentityIds = new long[0],
                OccupantGridEntityId = gridEntityId,
                GridOccupantIdentityIds = new long[0],
                GridControllerHumanIdentityIds = new long[0],
                ControllerInventoryComplete = false
            };
            if (MyAPIGateway.Session == null || MyAPIGateway.Players == null ||
                MyAPIGateway.Entities == null || gridEntityId <= 0)
                return result;

            var grid = MyAPIGateway.Entities.GetEntityById(gridEntityId) as IMyCubeGrid;
            if (grid == null || grid.MarkedForClose || grid.EntityId != gridEntityId) return result;

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, player => player != null && !player.IsBot);
            var connected = new List<long>();
            IMyPlayer authenticatedPlayer = null;
            foreach (var player in players)
            {
                if (player.IdentityId <= 0) return result;
                connected.Add(player.IdentityId);
                if (player.IdentityId == authenticatedPlayerIdentityId) authenticatedPlayer = player;
            }
            if (authenticatedPlayer == null) return result;
            var controlledEntity = authenticatedPlayer.Controller == null ||
                authenticatedPlayer.Controller.ControlledEntity == null
                ? null : authenticatedPlayer.Controller.ControlledEntity.Entity;
            var controlledController = controlledEntity as IMyShipController;
            var controlledCockpit = controlledEntity as IMyCockpit;
            IMyTerminalBlock controlledSeat = (IMyTerminalBlock)controlledController ?? controlledCockpit;
            var controlsExactController = controlledSeat != null && !controlledSeat.MarkedForClose &&
                controlledSeat.CubeGrid != null && controlledSeat.CubeGrid.EntityId == gridEntityId;

            var occupants = new List<long>();
            foreach (var cockpit in grid.GetFatBlocks<IMyCockpit>())
            {
                if (cockpit == null || cockpit.MarkedForClose) continue;
                if (controlsExactController && cockpit.EntityId == controlledSeat.EntityId)
                {
                    occupants.Add(authenticatedPlayerIdentityId);
                    continue;
                }
                if (cockpit.Pilot == null)
                    continue;
                if (!cockpit.Pilot.IsPlayer || cockpit.Pilot.IsBot) continue;
                if (cockpit.Pilot.ControllerInfo == null || cockpit.Pilot.ControllerInfo.ControllingIdentityId <= 0)
                    return result;
                occupants.Add(cockpit.Pilot.ControllerInfo.ControllingIdentityId);
            }
            if (controlsExactController && !occupants.Contains(authenticatedPlayerIdentityId))
                occupants.Add(authenticatedPlayerIdentityId);

            var gridHumanControllers = new List<long>();
            foreach (var controller in grid.GetFatBlocks<IMyShipController>())
            {
                if (controller == null || controller.MarkedForClose) continue;
                if (controlsExactController && controller.EntityId == controlledSeat.EntityId)
                {
                    gridHumanControllers.Add(authenticatedPlayerIdentityId);
                    continue;
                }
                if (!controller.IsUnderControl) continue;
                var info = controller.ControllerInfo;
                if (info == null || info.ControllingIdentityId <= 0) return result;
                if (connected.Contains(info.ControllingIdentityId)) gridHumanControllers.Add(info.ControllingIdentityId);
            }
            if (controlsExactController && !gridHumanControllers.Contains(authenticatedPlayerIdentityId))
                gridHumanControllers.Add(authenticatedPlayerIdentityId);

            result.GridEntityIds = new[] { grid.EntityId };
            result.ConnectedHumanIdentityIds = connected;
            result.GridOccupantIdentityIds = occupants;
            result.GridControllerHumanIdentityIds = gridHumanControllers;
            result.ControllerInventoryComplete = true;
            result.OccupantGridEntityId = grid.EntityId;
            result.Complete = true;
            result.IdentityVerified = true;
            return result;
        }

        private Frame ObserveExactGrid(DrivingLabPassiveTelemetrySelection approved, DateTime nowUtc)
        {
            var grid = MyAPIGateway.Entities.GetEntityById(approved.GridEntityId) as IMyCubeGrid;
            if (grid == null || grid.MarkedForClose || grid.EntityId != approved.GridEntityId || grid.Physics == null)
                return null;

            var matrix = grid.WorldMatrix;
            var velocity = new Vector3D(grid.Physics.LinearVelocity);
            var speed = velocity.Length();
            var session = MyAPIGateway.Session;
            if (session == null || !Finite(speed) || !Finite(velocity.X) || !Finite(velocity.Y) || !Finite(velocity.Z))
                return null;
            var tick = session.GameplayFrameCounter;
            var simulationTime = session.ElapsedPlayTime.TotalSeconds;
            if (tick < 0 || !Finite(simulationTime) || simulationTime < 0) return null;

            IMyPlayer authenticatedPlayer = null;
            var players = new List<IMyPlayer>();
            if (MyAPIGateway.Players != null)
                MyAPIGateway.Players.GetPlayers(players, player => player != null && !player.IsBot &&
                    player.IdentityId == approved.PlayerIdentityId);
            if (players.Count == 1) authenticatedPlayer = players[0];
            var controlledEntity = authenticatedPlayer == null || authenticatedPlayer.Controller == null ||
                authenticatedPlayer.Controller.ControlledEntity == null
                ? null : authenticatedPlayer.Controller.ControlledEntity.Entity;
            var controlledController = controlledEntity as IMyShipController;
            var controlledCockpit = controlledEntity as IMyCockpit;
            var controlledSeatEntityId = controlledController != null ? controlledController.EntityId :
                controlledCockpit != null ? controlledCockpit.EntityId : 0;
            var controller = grid.GetFatBlocks<IMyShipController>()
                .FirstOrDefault(candidate => candidate != null && !candidate.MarkedForClose &&
                    ((candidate.Pilot != null && candidate.Pilot.IsPlayer && !candidate.Pilot.IsBot &&
                      candidate.Pilot.ControllerInfo != null &&
                      candidate.Pilot.ControllerInfo.ControllingIdentityId == approved.PlayerIdentityId) ||
                     (controlledSeatEntityId > 0 && candidate.EntityId == controlledSeatEntityId)));
            IMyTerminalBlock seatBlock = (IMyTerminalBlock)controller ?? controlledController ?? (IMyTerminalBlock)controlledCockpit;
            IReadOnlyList<DrivingLabPassiveWheelTelemetry> wheelStates = null;
            var wheelController = controller as MyShipController;
            if (wheelController != null && wheelController.GridWheels != null)
                wheelStates = wheelController.GridWheels.Wheels.Where(wheel => wheel != null).Select(wheel =>
                    new DrivingLabPassiveWheelTelemetry
                    {
                        EntityId = wheel.EntityId,
                        BrakingEnabled = wheel.BrakingEnabled,
                        ActiveBrake = wheel.Brake,
                        Handbrake = wheel.Handbrake,
                        SteeringOverride = wheel.SteeringOverride,
                        PropulsionOverride = wheel.PropulsionOverride
                    }).ToList();
            var telemetryFrame = new DrivingLabPassiveTelemetryFrame
            {
                CapturedAtUtc = nowUtc.ToUniversalTime(),
                SimulationTick = tick,
                SimulationTimeSeconds = simulationTime,
                CockpitEntityId = seatBlock == null ? (long?)null : seatBlock.EntityId,
                CockpitDefinitionType = seatBlock == null ? null : seatBlock.BlockDefinition.TypeId.ToString(),
                CockpitSubtypeId = seatBlock == null ? null : seatBlock.BlockDefinition.SubtypeName,
                PositionX = matrix.Translation.X,
                PositionY = matrix.Translation.Y,
                PositionZ = matrix.Translation.Z,
                RightX = matrix.Right.X,
                RightY = matrix.Right.Y,
                RightZ = matrix.Right.Z,
                UpX = matrix.Up.X,
                UpY = matrix.Up.Y,
                UpZ = matrix.Up.Z,
                ForwardX = matrix.Forward.X,
                ForwardY = matrix.Forward.Y,
                ForwardZ = matrix.Forward.Z,
                VelocityX = velocity.X,
                VelocityY = velocity.Y,
                VelocityZ = velocity.Z,
                SpeedMetersPerSecond = speed,
                WheelStates = wheelStates
            };
            var json = DrivingLabPassiveTelemetryRecordFormatter.Format(
                approved, telemetryFrame, sequence + 1, droppedRecords);
            var byteCount = Encoding.UTF8.GetByteCount(json) + 1;
            if (byteCount > 1024 * 1024) return null;
            return new Frame { Json = json, ByteCount = byteCount, CapturedAtUtc = nowUtc.ToUniversalTime() };
        }

        private void ExportAndClear()
        {
            var current = selection;
            selection = null;
            if (current == null) return;
            try
            {
                var builder = new StringBuilder(bufferedBytes);
                foreach (var frame in frames) builder.Append(frame.Json).Append('\n');
                var filename = "driving-lab-passive-" + current.RunId + ".jsonl";
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           filename, typeof(MySessionComponentBase)))
                    writer.Write(builder.ToString());
                var first = frames.Count == 0 ? (DateTime?)null : frames[0].CapturedAtUtc;
                var last = frames.Count == 0 ? (DateTime?)null : frames[frames.Count - 1].CapturedAtUtc;
                var summary = "{\"schemaVersion\":1,\"source\":\"server-passive-grid\",\"permissionScope\":\"read_only_observation\"," +
                    "\"runId\":" + Quote(current.RunId) + ",\"sessionId\":" + Quote(current.SessionId) +
                    ",\"gridEntityId\":" + current.GridEntityId.ToString(CultureInfo.InvariantCulture) +
                    ",\"playerIdentityId\":" + current.PlayerIdentityId.ToString(CultureInfo.InvariantCulture) +
                    ",\"recordCount\":" + frames.Count.ToString(CultureInfo.InvariantCulture) +
                    ",\"droppedRecordsTotal\":" + droppedRecords.ToString(CultureInfo.InvariantCulture) +
                    ",\"bufferedBytes\":" + bufferedBytes.ToString(CultureInfo.InvariantCulture) +
                    ",\"maximumSampleRateHz\":" + current.MaximumSampleRateHz.ToString(CultureInfo.InvariantCulture) +
                    ",\"firstCapturedAtUtc\":" + (first.HasValue ? Quote(first.Value.ToString("O", CultureInfo.InvariantCulture)) : "null") +
                    ",\"lastCapturedAtUtc\":" + (last.HasValue ? Quote(last.Value.ToString("O", CultureInfo.InvariantCulture)) : "null") +
                    ",\"freshness\":\"UTC sample timestamps and simulation ticks in each record; source inventory rechecked within five seconds\"}";
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           "driving-lab-passive-" + current.RunId + ".summary.json", typeof(MySessionComponentBase)))
                    writer.Write(summary);
                MyLog.Default.WriteLineAndConsole("[VRageCage][DRIVING-LAB][PASSIVE] exported " +
                    frames.Count.ToString(CultureInfo.InvariantCulture) + " records; dropped=" +
                    droppedRecords.ToString(CultureInfo.InvariantCulture) + "; source=server-passive-grid; scope=read_only_observation.");
            }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole("[VRageCage][DRIVING-LAB][PASSIVE] export failed: " +
                                                  exception.GetType().Name + ".");
            }
            frames.Clear();
            bufferedBytes = 0;
        }

        private static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default: if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); else output.Append(character); break;
                }
            }
            return output.Append('"').ToString();
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
