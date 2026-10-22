using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Voidwright.Core
{
    public sealed class BlockLifecycleObservedBlock
    {
        public long EntityId;
        public bool HasEntityId;
        public string DefinitionType;
        public string SubtypeId;
    }

    public sealed class BlockLifecycleDiagnosticSnapshot
    {
        public const string CurrentSchema = "vragecage.block-lifecycle-diagnostic.v1";
        public const int CurrentSchemaVersion = 1;

        public string Schema = CurrentSchema;
        public int SchemaVersion = CurrentSchemaVersion;
        public string Phase;
        public string RunId;
        public string SessionId;
        public string LabRunId;
        public string FixtureId;
        public string BootstrapNonce;
        public string SourceTreeSha256;
        public string InstrumentationLedgerSha256;
        public long CapturedSimulationTick;
        public DateTime CapturedUtc;
        public long TargetGridEntityId;
        public string TargetGridName;
        public bool TargetGridFound;
        public int BlockCount;
        public readonly List<BlockLifecycleObservedBlock> Blocks = new List<BlockLifecycleObservedBlock>();

        public string ToJson()
        {
            var output = new StringBuilder(2048 + Blocks.Count * 112);
            output.Append("{\"schema\":\"").Append(CurrentSchema)
                .Append("\",\"schemaVersion\":").Append(SchemaVersion)
                .Append(",\"phase\":\"").Append(Escape(Phase))
                .Append("\",\"runId\":\"").Append(Escape(RunId))
                .Append("\",\"sessionId\":\"").Append(Escape(SessionId))
                .Append("\",\"labRunId\":\"").Append(Escape(LabRunId))
                .Append("\",\"fixtureId\":\"").Append(Escape(FixtureId))
                .Append("\",\"bootstrapNonce\":\"").Append(Escape(BootstrapNonce))
                .Append("\",\"sourceTreeSha256\":\"").Append(Escape(SourceTreeSha256))
                .Append("\",\"instrumentationLedgerSha256\":\"").Append(Escape(InstrumentationLedgerSha256))
                .Append("\",\"capturedSimulationTick\":").Append(CapturedSimulationTick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"capturedUtc\":\"").Append(CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append("\",\"targetGridEntityId\":").Append(TargetGridEntityId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"targetGridName\":\"").Append(Escape(TargetGridName))
                .Append("\",\"targetGridFound\":").Append(TargetGridFound ? "true" : "false")
                .Append(",\"blockCount\":").Append(BlockCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"blocks\":[");
            for (var index = 0; index < Blocks.Count; index++)
            {
                if (index != 0) output.Append(',');
                var block = Blocks[index];
                output.Append("{\"entityId\":");
                if (block.HasEntityId) output.Append(block.EntityId.ToString(CultureInfo.InvariantCulture));
                else output.Append("null");
                output.Append(",\"definitionType\":\"").Append(Escape(block.DefinitionType))
                    .Append("\",\"subtypeId\":\"").Append(Escape(block.SubtypeId)).Append("\"}");
            }
            return output.Append("]}").ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var output = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
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
                        if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(character);
                        break;
                }
            }
            return output.ToString();
        }
    }

    public sealed class BlockLifecycleDiagnosticTarget
    {
        public const string ExpectedFixtureCameraSubtype = "SmallCameraBlock";
        public long GridEntityId;
        public string GridName;
        public long ControllerEntityId;
        public string ControllerType;
        public string ControllerSubtype;
        public long CoreEntityId;
        public string CoreSubtype;
        public long CameraEntityId;
        public string CameraSubtype;
        public string SourceTreeSha256;
        public string InstrumentationLedgerSha256;
    }

    /// <summary>Fail-closed binding checks for the read-only load/save diagnostic.</summary>
    public static class BlockLifecycleDiagnosticBindingPolicy
    {
        public static bool IsBound(BlockLifecycleDiagnosticSnapshot snapshot,
            BlockLifecycleDiagnosticTarget target, string runId, string sessionId,
            string labRunId, string fixtureId, string bootstrapNonce)
        {
            if (snapshot == null || target == null ||
                snapshot.Schema != BlockLifecycleDiagnosticSnapshot.CurrentSchema ||
                snapshot.SchemaVersion != BlockLifecycleDiagnosticSnapshot.CurrentSchemaVersion ||
                (snapshot.Phase != "after-entity-initialization" && snapshot.Phase != "before-first-world-save") ||
                !SameIdentity(snapshot.RunId, runId) || !SameIdentity(snapshot.SessionId, sessionId) ||
                !SameIdentity(snapshot.LabRunId, labRunId) || !SameIdentity(snapshot.FixtureId, fixtureId) ||
                !SameIdentity(snapshot.BootstrapNonce, bootstrapNonce) ||
                !SameHex(snapshot.SourceTreeSha256, target.SourceTreeSha256, 64) ||
                !SameHex(snapshot.InstrumentationLedgerSha256, target.InstrumentationLedgerSha256, 64) ||
                snapshot.TargetGridEntityId != target.GridEntityId ||
                (snapshot.TargetGridFound && !string.Equals(snapshot.TargetGridName, target.GridName, StringComparison.Ordinal)) ||
                (!snapshot.TargetGridFound && !string.IsNullOrEmpty(snapshot.TargetGridName)) ||
                snapshot.Blocks == null || snapshot.BlockCount != snapshot.Blocks.Count || snapshot.BlockCount < 0)
                return false;

            var ids = new HashSet<long>();
            foreach (var block in snapshot.Blocks)
                if (block == null || (block.HasEntityId && (block.EntityId <= 0 || !ids.Add(block.EntityId))) ||
                    (!block.HasEntityId && block.EntityId != 0) || string.IsNullOrWhiteSpace(block.DefinitionType) ||
                    string.IsNullOrWhiteSpace(block.SubtypeId)) return false;

            return snapshot.TargetGridFound || snapshot.BlockCount == 0;
        }

        public static bool ContainsExpectedBlocks(BlockLifecycleDiagnosticSnapshot snapshot,
            BlockLifecycleDiagnosticTarget target)
        {
            return snapshot != null && target != null && snapshot.TargetGridFound &&
                   target.ControllerEntityId > 0 && target.CoreEntityId > 0 && target.CameraEntityId > 0 &&
                   HasBlock(snapshot, target.ControllerEntityId, target.ControllerType, target.ControllerSubtype) &&
                   HasBlock(snapshot, target.CoreEntityId, "MyObjectBuilder_UpgradeModule", target.CoreSubtype) &&
                   HasBlock(snapshot, target.CameraEntityId, "MyObjectBuilder_CameraBlock", target.CameraSubtype);
        }

        private static bool HasBlock(BlockLifecycleDiagnosticSnapshot snapshot, long id, string type, string subtype)
        {
            return snapshot.Blocks.Any(block => block.HasEntityId && block.EntityId == id &&
                string.Equals(block.DefinitionType, type, StringComparison.Ordinal) &&
                string.Equals(block.SubtypeId, subtype, StringComparison.Ordinal));
        }

        private static bool SameIdentity(string left, string right)
            => IsHex(left, 32) && IsHex(right, 32) && string.Equals(left, right, StringComparison.Ordinal);

        private static bool SameHex(string left, string right, int length)
            => IsHex(left, length) && IsHex(right, length) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool IsHex(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length != length) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F'))) return false;
            return true;
        }
    }
}
