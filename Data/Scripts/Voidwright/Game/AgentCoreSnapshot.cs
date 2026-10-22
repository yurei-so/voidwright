using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Voidwright.Game
{
    /// <summary>
    /// Versioned, read-only description of the physical construct enrolled by an Agent Core.
    /// Kept as plain data so a later MCP bridge can expose it without owning game discovery.
    /// </summary>
    public sealed class AgentCoreSnapshot
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumPhysicalGrids = 64;
        public const int MaximumBlockEntries = 4096;
        public const int MaximumTerminalBlockRows = 80;

        public int SchemaVersion = CurrentSchemaVersion;
        public string IdentityScope = "world-local";
        public long CapturedSimulationTick;
        public long CoreEntityId;
        public string CoreName;
        public long RootGridEntityId;
        public string RootGridName;
        public Vector3D CorePosition;
        public Vector3D CoreRight;
        public Vector3D CoreUp;
        public Vector3D CoreForward;
        public int PhysicalGridCount;
        public int BlocksObserved;
        public bool GridCoverageComplete;
        public bool BlockCoverageComplete;
        public ulong StructureFingerprint;
        public readonly List<AgentGridSnapshot> PhysicalGrids = new List<AgentGridSnapshot>();

        public string AgentId => "voidwright-core:" + CoreEntityId.ToString(CultureInfo.InvariantCulture);

        public bool IsComplete => GridCoverageComplete && BlockCoverageComplete;

        public string StructureSignature()
            => RootGridEntityId.ToString(CultureInfo.InvariantCulture) + ":" +
               PhysicalGridCount.ToString(CultureInfo.InvariantCulture) + ":" +
               BlocksObserved.ToString(CultureInfo.InvariantCulture) + ":" +
               StructureFingerprint.ToString("X16", CultureInfo.InvariantCulture) + ":" + IsComplete;

        public void AppendCustomInfo(StringBuilder builder)
        {
            if (builder == null) return;
            builder.AppendLine();
            builder.AppendLine("VOIDWRIGHT AGENT BODY (observation only)");
            builder.AppendLine("Schema: " + SchemaVersion + "  Captured tick: " + CapturedSimulationTick);
            builder.AppendLine("Agent: " + AgentId + " (" + IdentityScope + ")  Core entity: " + CoreEntityId);
            builder.AppendLine("Core: " + Safe(CoreName) + "  Root grid: " + Safe(RootGridName) + " (" + RootGridEntityId + ")");
            builder.AppendLine("Core world position: " + Vector(CorePosition));
            builder.AppendLine("Core world right/up/forward: " + Vector(CoreRight) + " / " + Vector(CoreUp) + " / " + Vector(CoreForward));
            builder.AppendLine("Physical grids: " + PhysicalGridCount + " (listed " + PhysicalGrids.Count + ")  Blocks observed: " + BlocksObserved);
            builder.AppendLine("Coverage: " + (IsComplete ? "complete" : "TRUNCATED - query via the future agent data interface"));

            var rows = 0;
            foreach (var grid in PhysicalGrids)
            {
                builder.AppendLine("GRID " + grid.Name + " id=" + grid.EntityId + " size=" + grid.GridSizeClass +
                                   " cell=" + Number(grid.GridSizeMeters) + "m" +
                                   " static=" + grid.IsStatic + " blocks=" + grid.BlockCount);
                builder.AppendLine("  position=" + Vector(grid.Position) + " right/up/forward=" + Vector(grid.Right) +
                                   "/" + Vector(grid.Up) + "/" + Vector(grid.Forward));
                builder.AppendLine("  velocity=" + Vector(grid.LinearVelocity) + " m/s gravity=" + Vector(grid.NaturalGravity) + " m/s^2");

                foreach (var block in grid.Blocks)
                {
                    if (rows >= MaximumTerminalBlockRows)
                    {
                        builder.AppendLine("... terminal view capped at " + MaximumTerminalBlockRows + " block rows; snapshot coverage=" +
                                           (IsComplete ? "complete" : "truncated") + ".");
                        return;
                    }
                    builder.Append("  BLOCK ").Append(block.DefinitionType).Append('/').Append(block.SubtypeId)
                        .Append(" id=").Append(block.EntityId)
                        .Append(" name='").Append(Safe(block.Name)).Append("' pos=").Append(block.Position)
                        .Append(" bounds=").Append(block.Min).Append("..").Append(block.Max)
                        .Append(" facing=").Append(block.Forward).Append('/').Append(block.Up)
                        .Append(" build=").Append(Number(block.BuildLevelRatio))
                        .Append(" damage=").Append(Number(block.DamageRatio));
                    if (block.HasEnabledState)
                        builder.Append(" enabled=").Append(block.Enabled);
                    builder.Append(" functional=").Append(block.IsFunctional)
                        .Append(" working=").Append(block.IsWorking)
                        .AppendLine();
                    rows++;
                }
            }
        }

        private static string Safe(string value) => string.IsNullOrEmpty(value) ? "(unnamed)" : value;

        private static string Vector(Vector3D value)
            => Number(value.X) + "," + Number(value.Y) + "," + Number(value.Z);

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public sealed class AgentGridSnapshot
    {
        public long EntityId;
        public string Name;
        public string GridSizeClass;
        public float GridSizeMeters;
        public bool IsStatic;
        public Vector3D Position;
        public Vector3D Right;
        public Vector3D Up;
        public Vector3D Forward;
        public Vector3D LinearVelocity;
        public Vector3D NaturalGravity;
        public int BlockCount;
        public readonly List<AgentBlockSnapshot> Blocks = new List<AgentBlockSnapshot>();
    }

    public sealed class AgentBlockSnapshot
    {
        public long EntityId;
        public string DefinitionType;
        public string SubtypeId;
        public string Name;
        public Vector3I Position;
        public Vector3I Min;
        public Vector3I Max;
        public string Forward;
        public string Up;
        public float BuildLevelRatio;
        public float DamageRatio;
        public bool HasEnabledState;
        public bool Enabled;
        public bool IsFunctional;
        public bool IsWorking;
        public Vector3D WorldCenter;
        public string TypedKind;
        public long OwnerIdentityId;
        public string AccessStatus = "not_evaluated_no_authenticated_actor";
        public bool HasSuspensionState;
        public bool SuspensionSteeringEnabled;
        public bool SuspensionPropulsionEnabled;
        public bool SuspensionInvertSteer;
        public bool SuspensionInvertPropulsion;
        public long MechanicalTopGridEntityId;
        public string MechanicalTopGridName;
        public bool IsMechanicalConnection;
        public bool MechanicalAttached;
        public bool MechanicalPendingAttachment;
    }

    /// <summary>
    /// One deliberately anonymous, transient player-character pose for local lab debugging.
    /// Pose data is emitted only when exactly one non-bot player is connected, avoiding
    /// incidental tracking of other players in a shared test session.
    /// </summary>
    public sealed class PlayerCharacterObservationSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public long CapturedSimulationTick;
        public int ConnectedHumanPlayerCount;
        public string Status = "unavailable";
        public bool CharacterObserved;
        public long CharacterEntityId;
        public Vector3D Position;
        public Vector3D Right;
        public Vector3D Up;
        public Vector3D Forward;
    }

    public static class AgentCoreSnapshotBuilder
    {
        public static AgentCoreSnapshot Capture(IMyUpgradeModule core, long simulationTick)
        {
            if (core == null || core.CubeGrid == null) return null;

            var root = core.CubeGrid;
            var grids = new List<IMyCubeGrid>();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Physical, grids);
            if (grids.Count == 0) grids.Add(root);
            grids.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));

            var matrix = core.WorldMatrix;
            var snapshot = new AgentCoreSnapshot
            {
                CapturedSimulationTick = simulationTick,
                CoreEntityId = core.EntityId,
                CoreName = core.CustomName,
                RootGridEntityId = root.EntityId,
                RootGridName = root.CustomName,
                CorePosition = core.GetPosition(),
                CoreRight = matrix.Right,
                CoreUp = matrix.Up,
                CoreForward = matrix.Forward,
                PhysicalGridCount = grids.Count,
                GridCoverageComplete = grids.Count <= AgentCoreSnapshot.MaximumPhysicalGrids,
                BlockCoverageComplete = grids.Count <= AgentCoreSnapshot.MaximumPhysicalGrids
            };

            var gridLimit = Math.Min(grids.Count, AgentCoreSnapshot.MaximumPhysicalGrids);
            var includedBlockEntries = 0;
            var fingerprint = 14695981039346656037UL;
            for (var gridIndex = 0; gridIndex < gridLimit; gridIndex++)
            {
                var grid = grids[gridIndex];
                if (grid == null || grid.MarkedForClose)
                {
                    snapshot.GridCoverageComplete = false;
                    snapshot.BlockCoverageComplete = false;
                    continue;
                }
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, null);
                blocks.Sort(CompareBlocks);
                var gridMatrix = grid.WorldMatrix;
                var gridSnapshot = new AgentGridSnapshot
                {
                    EntityId = grid.EntityId,
                    Name = grid.CustomName,
                    GridSizeClass = grid.GridSizeEnum.ToString(),
                    GridSizeMeters = grid.GridSize,
                    IsStatic = grid.IsStatic,
                    Position = grid.GetPosition(),
                    Right = gridMatrix.Right,
                    Up = gridMatrix.Up,
                    Forward = gridMatrix.Forward,
                    LinearVelocity = grid.LinearVelocity,
                    NaturalGravity = grid.NaturalGravity,
                    BlockCount = blocks.Count
                };
                Fingerprint(ref fingerprint, gridSnapshot.EntityId.ToString(CultureInfo.InvariantCulture));
                snapshot.BlocksObserved += blocks.Count;

                foreach (var slim in blocks)
                {
                    if (includedBlockEntries >= AgentCoreSnapshot.MaximumBlockEntries)
                    {
                        snapshot.BlockCoverageComplete = false;
                        break;
                    }
                    var block = Describe(slim);
                    gridSnapshot.Blocks.Add(block);
                    Fingerprint(ref fingerprint, block.EntityId.ToString(CultureInfo.InvariantCulture));
                    Fingerprint(ref fingerprint, block.DefinitionType);
                    Fingerprint(ref fingerprint, block.SubtypeId);
                    Fingerprint(ref fingerprint, block.Name);
                    Fingerprint(ref fingerprint, block.Position.X.ToString(CultureInfo.InvariantCulture));
                    Fingerprint(ref fingerprint, block.Position.Y.ToString(CultureInfo.InvariantCulture));
                    Fingerprint(ref fingerprint, block.Position.Z.ToString(CultureInfo.InvariantCulture));
                    Fingerprint(ref fingerprint, block.Forward);
                    Fingerprint(ref fingerprint, block.Up);
                    includedBlockEntries++;
                }
                snapshot.PhysicalGrids.Add(gridSnapshot);
            }
            if (!snapshot.GridCoverageComplete) snapshot.BlockCoverageComplete = false;
            snapshot.StructureFingerprint = fingerprint;
            return snapshot;
        }

        private static void Fingerprint(ref ulong hash, string value)
        {
            if (value == null) value = "";
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }
            hash ^= 0xFF;
            hash *= 1099511628211UL;
        }

        private static int CompareBlocks(IMySlimBlock left, IMySlimBlock right)
        {
            var compare = left.Position.X.CompareTo(right.Position.X);
            if (compare != 0) return compare;
            compare = left.Position.Y.CompareTo(right.Position.Y);
            return compare != 0 ? compare : left.Position.Z.CompareTo(right.Position.Z);
        }

        private static AgentBlockSnapshot Describe(IMySlimBlock slim)
        {
            var fat = slim.FatBlock;
            var functional = fat as Sandbox.ModAPI.IMyFunctionalBlock;
            var terminal = fat as IMyTerminalBlock;
            var definition = slim.BlockDefinition.Id;
            var result = new AgentBlockSnapshot
            {
                EntityId = fat == null ? 0 : fat.EntityId,
                DefinitionType = definition.TypeId.ToString(),
                SubtypeId = definition.SubtypeName,
                Name = terminal == null ? (fat == null ? "" : fat.DisplayNameText) : terminal.CustomName,
                Position = slim.Position,
                Min = slim.Min,
                Max = slim.Max,
                Forward = slim.Orientation.Forward.ToString(),
                Up = slim.Orientation.Up.ToString(),
                BuildLevelRatio = slim.BuildLevelRatio,
                DamageRatio = slim.DamageRatio,
                HasEnabledState = functional != null,
                Enabled = functional != null && functional.Enabled,
                IsFunctional = fat != null && fat.IsFunctional,
                IsWorking = fat != null && fat.IsWorking
            };
            Vector3D worldCenter;
            slim.ComputeWorldCenter(out worldCenter);
            result.WorldCenter = worldCenter;
            if (terminal != null) result.OwnerIdentityId = terminal.OwnerId;
            if (fat is Sandbox.ModAPI.IMyShipController) result.TypedKind = "controller";
            else if (fat is Sandbox.ModAPI.IMyCameraBlock) result.TypedKind = "camera";
            else if (fat is Sandbox.ModAPI.IMySensorBlock) result.TypedKind = "sensor";
            else if (fat is Sandbox.ModAPI.IMyMotorSuspension)
            {
                var suspension = (Sandbox.ModAPI.IMyMotorSuspension)fat;
                result.TypedKind = "suspension";
                result.HasSuspensionState = true;
                result.SuspensionSteeringEnabled = suspension.Steering;
                result.SuspensionPropulsionEnabled = suspension.Propulsion;
                result.SuspensionInvertSteer = suspension.InvertSteer;
                result.SuspensionInvertPropulsion = suspension.InvertPropulsion;
            }
            var mechanical = fat as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
            if (mechanical != null)
            {
                result.IsMechanicalConnection = true;
                result.MechanicalAttached = mechanical.IsAttached;
                result.MechanicalPendingAttachment = mechanical.PendingAttachment;
                var topGrid = mechanical.TopGrid;
                if (topGrid != null)
                {
                    result.MechanicalTopGridEntityId = topGrid.EntityId;
                    result.MechanicalTopGridName = topGrid.CustomName;
                }
            }
            return result;
        }
    }
}
