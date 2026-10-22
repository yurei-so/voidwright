using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using Voidwright.Core;

namespace Voidwright.Game
{
    /// <summary>
    /// Publishes a bounded, read-only Agent Core snapshot for the separate local MCP process.
    /// This file is world-scoped and contains no control or command channel.
    /// </summary>
    public sealed class AgentCoreSnapshotFileExporter
    {
        public const string SnapshotFileName = "agent-core-snapshot-v1.json";
        public const string DiscoveryFileName = "agent-core-discovery-v1.json";
        public const int SchemaVersion = 1;
        public const int PublishIntervalTicks = 300;
        public const int MaximumAgents = 8;
        public const int MaximumSnapshotBytes = 16 * 1024 * 1024;

        private long lastPublishedTick = long.MinValue;
        private string lastError;
        private bool hasPublished;
        private readonly string runId = Guid.NewGuid().ToString("N");
        private readonly string sessionId;
        private readonly string labRunId;
        private readonly string fixtureId;
        private readonly string bootstrapNonce;
        public string RunId { get { return runId; } }

        public AgentCoreSnapshotFileExporter() : this(string.Empty, string.Empty, string.Empty, string.Empty) { }

        public AgentCoreSnapshotFileExporter(string sessionId, string labRunId, string fixtureId, string bootstrapNonce)
        {
            this.sessionId = sessionId ?? string.Empty;
            this.labRunId = labRunId ?? string.Empty;
            this.fixtureId = fixtureId ?? string.Empty;
            this.bootstrapNonce = bootstrapNonce ?? string.Empty;
        }

        public void Update(IList<NavigationControllerSnapshot> controllers, long simulationTick)
        {
            if (lastPublishedTick != long.MinValue && simulationTick - lastPublishedTick < PublishIntervalTicks) return;
            lastPublishedTick = simulationTick;
            if (MyAPIGateway.Utilities == null) return;

            try
            {
                var playerObservation = CapturePlayerObservation(simulationTick);
                var payload = Serialize(controllers, playerObservation, simulationTick, runId);
                if (Encoding.UTF8.GetByteCount(payload) > MaximumSnapshotBytes)
                    throw new InvalidOperationException("Agent Core snapshot exceeded the 16 MiB export limit.");
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           SnapshotFileName, typeof(VoidwrightSession)))
                    writer.Write(payload);
                var discovery = SerializeDiscovery(controllers, simulationTick, runId, sessionId,
                    labRunId, fixtureId, bootstrapNonce);
                if (Encoding.UTF8.GetByteCount(discovery) > MaximumSnapshotBytes)
                    throw new InvalidOperationException("Agent Core discovery exceeded the 16 MiB export limit.");
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           DiscoveryFileName, typeof(MySessionComponentBase)))
                    writer.Write(discovery);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           DiscoveryFileName, typeof(MySessionComponentBase)))
                    if (!string.Equals(discovery, reader.ReadToEnd(), StringComparison.Ordinal))
                        throw new InvalidOperationException("Agent Core discovery readback differed from the published export.");
                WriteDiscoveryReceipt(discovery, simulationTick);
                if (!hasPublished)
                {
                    hasPublished = true;
                    MyLog.Default.WriteLineAndConsole("Voidwright MCP snapshot export active: world-scoped, read-only; file=" + SnapshotFileName);
                }
                else if (lastError != null)
                {
                    MyLog.Default.WriteLineAndConsole("Voidwright MCP snapshot export recovered.");
                }
                lastError = null;
            }
            catch (Exception exception)
            {
                var message = exception.GetType().Name + ": " + exception.Message;
                if (message == lastError) return;
                lastError = message;
                MyLog.Default.WriteLineAndConsole("Voidwright MCP snapshot export unavailable: " + message);
            }
        }

        private void WriteDiscoveryReceipt(string discovery, long tick)
        {
            if (!IsHex(labRunId, 32) || !IsHex(fixtureId, 32) || !IsHex(bootstrapNonce, 32) || !IsHex(sessionId, 32)) return;
            var bytes = Encoding.UTF8.GetBytes(discovery);
            string fingerprint;
            fingerprint = Sha256Digest.ComputeHex(bytes);
            var receipt = "VWDR2\nmod-ready|" + labRunId + "|" + fixtureId + "|" + bootstrapNonce + "|" +
                sessionId + "|" + runId + "|" + tick.ToString(CultureInfo.InvariantCulture) + "|" +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" + fingerprint + "|" +
                CountPhysicalGrids(discovery).ToString(CultureInfo.InvariantCulture) + "\n";
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                       "voidwright-discovery-" + labRunId + ".receipt", typeof(MySessionComponentBase)))
                writer.Write(receipt);
        }

        private static int CountPhysicalGrids(string json)
        {
            return System.Text.RegularExpressions.Regex.Matches(json,
                "\\\"entityId\\\":[0-9]+,\\\"name\\\":").Count;
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            var output = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) output.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return output.ToString();
        }

        private static PlayerCharacterObservationSnapshot CapturePlayerObservation(long simulationTick)
        {
            var observation = new PlayerCharacterObservationSnapshot
            {
                CapturedSimulationTick = simulationTick,
                Status = "players_api_unavailable"
            };
            var collection = MyAPIGateway.Players;
            if (collection == null) return observation;

            var players = new List<IMyPlayer>();
            collection.GetPlayers(players, player => player != null && !player.IsBot);
            players.RemoveAll(player => player == null || player.IsBot);
            observation.ConnectedHumanPlayerCount = players.Count;
            if (players.Count == 0)
            {
                observation.Status = "no_connected_humans";
                return observation;
            }
            if (players.Count != 1)
            {
                observation.Status = "ambiguous_multiple_humans";
                return observation;
            }

            var character = players[0].Character;
            if (character == null || character.EntityId <= 0)
            {
                observation.Status = "character_not_spawned";
                return observation;
            }

            var matrix = character.WorldMatrix;
            observation.Status = "observed";
            observation.CharacterObserved = true;
            observation.CharacterEntityId = character.EntityId;
            observation.Position = character.GetPosition();
            observation.Right = matrix.Right;
            observation.Up = matrix.Up;
            observation.Forward = matrix.Forward;
            return observation;
        }

        private static string Serialize(IList<NavigationControllerSnapshot> controllers,
            PlayerCharacterObservationSnapshot playerObservation, long simulationTick, string runId)
        {
            var agents = new List<AgentCoreSnapshot>();
            if (controllers != null)
                foreach (var controller in controllers)
                    if (controller != null && controller.Agent != null)
                        agents.Add(controller.Agent);
            agents.Sort((left, right) => left.CoreEntityId.CompareTo(right.CoreEntityId));

            var output = new StringBuilder();
            output.Append("{\"schemaVersion\":").Append(SchemaVersion)
                .Append(",\"capturedAtUtc\":");
            AppendString(output, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), 40);
            output.Append(",\"capturedSimulationTick\":").Append(simulationTick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"runId\":");
            AppendString(output, runId, 32);
            output.Append(",\"playerObservation\":");
            AppendPlayerObservation(output, playerObservation);
            output
                .Append(",\"agentsTruncated\":").Append(agents.Count > MaximumAgents ? "true" : "false")
                .Append(",\"agents\":[");

            var included = Math.Min(agents.Count, MaximumAgents);
            for (var index = 0; index < included; index++)
            {
                if (index != 0) output.Append(',');
                AppendAgent(output, agents[index]);
            }
            return output.Append("]}").ToString();
        }

        private static void AppendPlayerObservation(StringBuilder output, PlayerCharacterObservationSnapshot observation)
        {
            output.Append("{\"schemaVersion\":").Append(observation.SchemaVersion)
                .Append(",\"capturedSimulationTick\":").Append(observation.CapturedSimulationTick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"connectedHumanPlayerCount\":").Append(observation.ConnectedHumanPlayerCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"status\":");
            AppendString(output, observation.Status, 48);
            output.Append(",\"characterObserved\":").Append(observation.CharacterObserved ? "true" : "false");
            if (observation.CharacterObserved)
            {
                output.Append(",\"characterEntityId\":").Append(observation.CharacterEntityId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"position\":"); AppendVector(output, observation.Position);
                output.Append(",\"right\":"); AppendVector(output, observation.Right);
                output.Append(",\"up\":"); AppendVector(output, observation.Up);
                output.Append(",\"forward\":"); AppendVector(output, observation.Forward);
            }
            output.Append('}');
        }

        private static string SerializeDiscovery(IList<NavigationControllerSnapshot> controllers, long tick, string runId,
            string sessionId, string labRunId, string fixtureId, string bootstrapNonce)
        {
            var agents = new List<AgentCoreSnapshot>();
            if (controllers != null)
                foreach (var controller in controllers)
                    if (controller != null && controller.Agent != null) agents.Add(controller.Agent);
            agents.Sort((left, right) => left.CoreEntityId.CompareTo(right.CoreEntityId));
            var output = new StringBuilder();
            output.Append("{\"schemaVersion\":1,\"scope\":\"physical_grid_group\",\"mechanicalEdgesScope\":\"observed_IMyMechanicalConnectionBlock_only\",\"capturedAtUtc\":");
            AppendString(output, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), 40);
            output.Append(",\"capturedSimulationTick\":").Append(tick.ToString(CultureInfo.InvariantCulture)).Append(",\"runId\":");
            AppendString(output, runId, 32);
            output.Append(",\"sessionId\":"); AppendString(output, sessionId ?? string.Empty, 32);
            output.Append(",\"labRunId\":"); AppendString(output, labRunId ?? string.Empty, 32);
            output.Append(",\"fixtureId\":"); AppendString(output, fixtureId ?? string.Empty, 32);
            output.Append(",\"bootstrapNonce\":"); AppendString(output, bootstrapNonce ?? string.Empty, 32);
            output.Append(",\"agentsTruncated\":").Append(agents.Count > MaximumAgents ? "true" : "false").Append(",\"agents\":[");
            var count = Math.Min(agents.Count, MaximumAgents);
            for (var i = 0; i < count; i++)
            {
                if (i != 0) output.Append(',');
                AppendDiscoveryAgent(output, agents[i]);
            }
            return output.Append("]}").ToString();
        }

        private static void AppendDiscoveryAgent(StringBuilder output, AgentCoreSnapshot agent)
        {
            output.Append("{\"coreEntityId\":").Append(agent.CoreEntityId.ToString(CultureInfo.InvariantCulture)).Append(",\"coreName\":");
            AppendString(output, agent.CoreName, 256);
            output.Append(",\"anchorGridEntityId\":").Append(agent.RootGridEntityId.ToString(CultureInfo.InvariantCulture)).Append(",\"anchorGridName\":");
            AppendString(output, agent.RootGridName, 256);
            output.Append(",\"physicalGridCountObserved\":").Append(agent.PhysicalGridCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"physicalGridCountIncluded\":").Append(agent.PhysicalGrids.Count.ToString(CultureInfo.InvariantCulture))
                .Append(",\"physicalGridInventoryComplete\":").Append(agent.GridCoverageComplete ? "true" : "false")
                .Append(",\"typedBlockInventoryComplete\":").Append(agent.BlockCoverageComplete ? "true" : "false")
                .Append(",\"mechanicalConnectionInventoryComplete\":").Append(agent.BlockCoverageComplete ? "true" : "false")
                .Append(",\"blocksObserved\":").Append(agent.BlocksObserved.ToString(CultureInfo.InvariantCulture))
                .Append(",\"physicalGrids\":[");
            for (var gi = 0; gi < agent.PhysicalGrids.Count; gi++)
            {
                if (gi != 0) output.Append(',');
                var grid = agent.PhysicalGrids[gi];
                output.Append("{\"entityId\":").Append(grid.EntityId.ToString(CultureInfo.InvariantCulture)).Append(",\"name\":");
                AppendString(output, grid.Name, 256);
                output.Append(",\"position\":"); AppendVector(output, grid.Position);
                output.Append(",\"right\":"); AppendVector(output, grid.Right);
                output.Append(",\"up\":"); AppendVector(output, grid.Up);
                output.Append(",\"forward\":"); AppendVector(output, grid.Forward);
                output.Append(",\"blockCountObserved\":").Append(grid.BlockCount.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"blocks\":[");
                var first = true;
                foreach (var block in grid.Blocks)
                {
                    if (!first) output.Append(','); first = false;
                    AppendDiscoveryBlock(output, grid.EntityId, block);
                }
                output.Append("]}");
            }
            output.Append("]}");
        }

        private static void AppendDiscoveryBlock(StringBuilder output, long gridId, AgentBlockSnapshot block)
        {
            output.Append("{\"gridEntityId\":").Append(gridId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"entityId\":").Append(block.EntityId.ToString(CultureInfo.InvariantCulture)).Append(",\"definitionType\":");
            AppendString(output, block.DefinitionType, 128);
            output.Append(",\"subtypeId\":"); AppendString(output, block.SubtypeId, 128);
            output.Append(",\"typedKind\":"); AppendString(output, block.TypedKind ?? "other", 32);
            output.Append(",\"hasSuspensionState\":").Append(block.HasSuspensionState ? "true" : "false")
                .Append(",\"steeringEnabled\":").Append(block.SuspensionSteeringEnabled ? "true" : "false")
                .Append(",\"propulsionEnabled\":").Append(block.SuspensionPropulsionEnabled ? "true" : "false")
                .Append(",\"invertSteer\":").Append(block.SuspensionInvertSteer ? "true" : "false")
                .Append(",\"invertPropulsion\":").Append(block.SuspensionInvertPropulsion ? "true" : "false");
            output.Append(",\"name\":"); AppendString(output, block.Name, 256);
            output.Append(",\"position\":"); AppendVector(output, block.Position);
            output.Append(",\"min\":"); AppendVector(output, block.Min);
            output.Append(",\"max\":"); AppendVector(output, block.Max);
            output.Append(",\"forward\":"); AppendString(output, block.Forward, 32);
            output.Append(",\"up\":"); AppendString(output, block.Up, 32);
            output.Append(",\"worldCenter\":"); AppendVector(output, block.WorldCenter);
            output.Append(",\"ownerIdentityId\":").Append(block.OwnerIdentityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"shareMode\":\"unavailable\",\"actorAccess\":"); AppendString(output, block.AccessStatus, 64);
            output.Append(",\"isMechanicalConnection\":").Append(block.IsMechanicalConnection ? "true" : "false")
                .Append(",\"attached\":").Append(block.MechanicalAttached ? "true" : "false")
                .Append(",\"pendingAttachment\":").Append(block.MechanicalPendingAttachment ? "true" : "false")
                .Append(",\"topGridEntityId\":").Append(block.MechanicalTopGridEntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"topGridName\":"); AppendString(output, block.MechanicalTopGridName, 256);
            output.Append('}');
        }

        private static void AppendAgent(StringBuilder output, AgentCoreSnapshot agent)
        {
            output.Append("{\"schemaVersion\":").Append(agent.SchemaVersion)
                .Append(",\"identityScope\":");
            AppendString(output, agent.IdentityScope, 64);
            output.Append(",\"agentId\":"); AppendString(output, agent.AgentId, 128);
            output.Append(",\"capturedSimulationTick\":").Append(agent.CapturedSimulationTick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"coreEntityId\":").Append(agent.CoreEntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"coreName\":"); AppendString(output, agent.CoreName, 256);
            output.Append(",\"rootGridEntityId\":").Append(agent.RootGridEntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"rootGridName\":"); AppendString(output, agent.RootGridName, 256);
            output.Append(",\"corePosition\":"); AppendVector(output, agent.CorePosition);
            output.Append(",\"coreRight\":"); AppendVector(output, agent.CoreRight);
            output.Append(",\"coreUp\":"); AppendVector(output, agent.CoreUp);
            output.Append(",\"coreForward\":"); AppendVector(output, agent.CoreForward);
            output.Append(",\"physicalGridCount\":").Append(agent.PhysicalGridCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"blocksObserved\":").Append(agent.BlocksObserved.ToString(CultureInfo.InvariantCulture))
                .Append(",\"gridCoverageComplete\":").Append(agent.GridCoverageComplete ? "true" : "false")
                .Append(",\"blockCoverageComplete\":").Append(agent.BlockCoverageComplete ? "true" : "false")
                .Append(",\"structureFingerprint\":");
            AppendString(output, agent.StructureFingerprint.ToString("X16", CultureInfo.InvariantCulture), 16);
            output.Append(",\"physicalGrids\":[");
            for (var gridIndex = 0; gridIndex < agent.PhysicalGrids.Count; gridIndex++)
            {
                if (gridIndex != 0) output.Append(',');
                AppendGrid(output, agent.PhysicalGrids[gridIndex]);
            }
            output.Append("]}");
        }

        private static void AppendGrid(StringBuilder output, AgentGridSnapshot grid)
        {
            output.Append("{\"entityId\":").Append(grid.EntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"name\":"); AppendString(output, grid.Name, 256);
            output.Append(",\"gridSizeClass\":"); AppendString(output, grid.GridSizeClass, 64);
            output.Append(",\"gridSizeMeters\":"); AppendNumber(output, grid.GridSizeMeters);
            output.Append(",\"isStatic\":").Append(grid.IsStatic ? "true" : "false")
                .Append(",\"position\":"); AppendVector(output, grid.Position);
            output.Append(",\"right\":"); AppendVector(output, grid.Right);
            output.Append(",\"up\":"); AppendVector(output, grid.Up);
            output.Append(",\"forward\":"); AppendVector(output, grid.Forward);
            output.Append(",\"linearVelocity\":"); AppendVector(output, grid.LinearVelocity);
            output.Append(",\"naturalGravity\":"); AppendVector(output, grid.NaturalGravity);
            output.Append(",\"blockCount\":").Append(grid.BlockCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"blocks\":[");
            for (var blockIndex = 0; blockIndex < grid.Blocks.Count; blockIndex++)
            {
                if (blockIndex != 0) output.Append(',');
                AppendBlock(output, grid.Blocks[blockIndex]);
            }
            output.Append("]}");
        }

        private static void AppendBlock(StringBuilder output, AgentBlockSnapshot block)
        {
            output.Append("{\"entityId\":").Append(block.EntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"definitionType\":"); AppendString(output, block.DefinitionType, 128);
            output.Append(",\"subtypeId\":"); AppendString(output, block.SubtypeId, 128);
            output.Append(",\"name\":"); AppendString(output, block.Name, 256);
            output.Append(",\"position\":"); AppendVector(output, block.Position);
            output.Append(",\"min\":"); AppendVector(output, block.Min);
            output.Append(",\"max\":"); AppendVector(output, block.Max);
            output.Append(",\"forward\":"); AppendString(output, block.Forward, 32);
            output.Append(",\"up\":"); AppendString(output, block.Up, 32);
            output.Append(",\"buildLevelRatio\":"); AppendNumber(output, block.BuildLevelRatio);
            output.Append(",\"damageRatio\":"); AppendNumber(output, block.DamageRatio);
            output.Append(",\"hasEnabledState\":").Append(block.HasEnabledState ? "true" : "false")
                .Append(",\"enabled\":").Append(block.Enabled ? "true" : "false")
                .Append(",\"isFunctional\":").Append(block.IsFunctional ? "true" : "false")
                .Append(",\"isWorking\":").Append(block.IsWorking ? "true" : "false")
                .Append(",\"worldCenter\":"); AppendVector(output, block.WorldCenter);
            output.Append('}');
        }

        private static void AppendVector(StringBuilder output, VRageMath.Vector3D value)
        {
            output.Append('['); AppendNumber(output, value.X);
            output.Append(','); AppendNumber(output, value.Y);
            output.Append(','); AppendNumber(output, value.Z);
            output.Append(']');
        }

        private static void AppendVector(StringBuilder output, VRageMath.Vector3I value)
        {
            output.Append('[').Append(value.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Z.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        private static void AppendNumber(StringBuilder output, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("Snapshot contains non-finite numeric data.");
            output.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendNumber(StringBuilder output, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Snapshot contains non-finite numeric data.");
            output.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendString(StringBuilder output, string value, int maximumCharacters)
        {
            if (value == null) value = string.Empty;
            var count = Math.Min(value.Length, maximumCharacters);
            output.Append('"');
            for (var index = 0; index < count; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            output.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        else output.Append(character);
                        break;
                }
            }
            output.Append('"');
        }
    }
}
