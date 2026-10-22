using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Voidwright.Core;

namespace Voidwright.Game
{
    /// <summary>Bounded read-only capture of one exact grid at load and first save.</summary>
    public sealed class BlockLifecycleDiagnosticExporter
    {
        public const string LoadFileName = "vragecage-block-load-v1.json";
        public const string PreSaveFileName = "vragecage-block-presave-v1.json";
        public const string TargetFileName = "vragecage-block-lifecycle-target-v1.json";
        public const int MaximumBlocks = 4096;

        private readonly BlockLifecycleDiagnosticTarget target;
        private readonly string runId;
        private readonly string sessionId;
        private readonly string labRunId;
        private readonly string fixtureId;
        private readonly string bootstrapNonce;

        public BlockLifecycleDiagnosticExporter(BlockLifecycleDiagnosticTarget target,
            string runId, string sessionId, string labRunId, string fixtureId, string bootstrapNonce)
        {
            this.target = target;
            this.runId = runId ?? string.Empty;
            this.sessionId = sessionId ?? string.Empty;
            this.labRunId = labRunId ?? string.Empty;
            this.fixtureId = fixtureId ?? string.Empty;
            this.bootstrapNonce = bootstrapNonce ?? string.Empty;
        }

        public bool Capture(string phase, long simulationTick)
        {
            if (phase != "after-entity-initialization" && phase != "before-first-world-save") return false;
            if (target == null || MyAPIGateway.Utilities == null || MyAPIGateway.Entities == null) return false;
            try
            {
                var snapshot = new BlockLifecycleDiagnosticSnapshot
                {
                    Phase = phase,
                    RunId = runId,
                    SessionId = sessionId,
                    LabRunId = labRunId,
                    FixtureId = fixtureId,
                    BootstrapNonce = bootstrapNonce,
                    SourceTreeSha256 = target.SourceTreeSha256,
                    InstrumentationLedgerSha256 = target.InstrumentationLedgerSha256,
                    CapturedSimulationTick = simulationTick,
                    CapturedUtc = DateTime.UtcNow,
                    TargetGridEntityId = target.GridEntityId,
                    TargetGridName = string.Empty
                };

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
                IMyCubeGrid selected = null;
                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.MarkedForClose || grid.EntityId != target.GridEntityId) continue;
                    if (selected != null)
                        throw new InvalidOperationException("target grid entity id resolved more than once");
                    selected = grid;
                }

                if (selected != null)
                {
                    snapshot.TargetGridFound = true;
                    snapshot.TargetGridName = selected.CustomName ?? string.Empty;
                    var blocks = new List<IMySlimBlock>();
                    selected.GetBlocks(blocks, null);
                    if (blocks.Count > MaximumBlocks)
                        throw new InvalidOperationException("target grid exceeded the diagnostic block limit");
                    foreach (var slim in blocks)
                    {
                        if (slim == null || slim.BlockDefinition == null)
                            throw new InvalidOperationException("target grid contained a block without a definition");
                        var fat = slim.FatBlock;
                        snapshot.Blocks.Add(new BlockLifecycleObservedBlock
                        {
                            EntityId = fat == null ? 0 : fat.EntityId,
                            HasEntityId = fat != null && fat.EntityId > 0,
                            DefinitionType = slim.BlockDefinition.Id.TypeId.ToString(),
                            SubtypeId = slim.BlockDefinition.Id.SubtypeName
                        });
                    }
                    snapshot.BlockCount = snapshot.Blocks.Count;
                }

                if (!BlockLifecycleDiagnosticBindingPolicy.IsBound(snapshot, target, runId,
                        sessionId, labRunId, fixtureId, bootstrapNonce))
                    throw new InvalidOperationException("diagnostic target or run binding failed");

                var payload = snapshot.ToJson();
                var filename = phase == "after-entity-initialization" ? LoadFileName : PreSaveFileName;
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(VoidwrightSession)))
                    writer.Write(payload);
                var expected = BlockLifecycleDiagnosticBindingPolicy.ContainsExpectedBlocks(snapshot, target);
                MyLog.Default.WriteLineAndConsole("Voidwright block lifecycle diagnostic: phase=" + phase +
                    " targetGrid=" + snapshot.TargetGridEntityId + " found=" + snapshot.TargetGridFound +
                    " blocks=" + snapshot.BlockCount + " expectedBlocks=" + expected +
                    " run=" + runId + " session=" + sessionId + " fixture=" + fixtureId);
                return true;
            }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright block lifecycle diagnostic unavailable: phase=" + phase +
                    " error=" + exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }

    }
}
