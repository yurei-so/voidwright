using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Voidwright.Core;

namespace Voidwright.Game
{
    public sealed class NavigationControllerSnapshot
    {
        public long EntityId;
        public string Name;
        public long GridId;
        public NavigationControllerSettings Settings;
        public string IntentProviders;
        public string WinningIntent;
        public AgentCoreSnapshot Agent;

        public string Signature()
            => EntityId + ":" + GridId + ":" + Settings.Armed + ":" + Settings.MobilityMode + ":" +
               Settings.DestinationGps + ":" + Settings.VanillaAi + ":" + IntentProviders + ":" + WinningIntent + ":" +
               (Agent == null ? "no-agent-snapshot" : Agent.StructureSignature());
    }

    public sealed class NavigationControllerRegistry
    {
        private readonly Dictionary<long, AgentCoreSnapshot> latestSnapshots = new Dictionary<long, AgentCoreSnapshot>();
        private readonly Dictionary<long, IMyTerminalBlock> infoBlocks = new Dictionary<long, IMyTerminalBlock>();

        public AgentCoreSnapshot GetLatestSnapshot(long coreEntityId)
        {
            AgentCoreSnapshot snapshot;
            return latestSnapshots.TryGetValue(coreEntityId, out snapshot) ? snapshot : null;
        }

        /// <summary>
        /// Performs an on-demand, read-only orthographic wireframe query against the latest
        /// captured body snapshot. A future MCP adapter can expose this without game discovery.
        /// </summary>
        public AgentWireframeRenderResult QueryWireframe(
            long coreEntityId,
            IList<AgentWireframeSelection> selections)
        {
            var snapshot = GetLatestSnapshot(coreEntityId);
            if (snapshot == null) return null;
            return AgentWireframeRenderer.Render(AgentWireframeSnapshotFactory.Create(snapshot), selections);
        }

        public List<NavigationControllerSnapshot> Observe(long simulationTick)
        {
            var result = new List<NavigationControllerSnapshot>();
            if (MyAPIGateway.Entities == null) return result;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
            var activeCoreIds = new HashSet<long>();
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                foreach (var module in grid.GetFatBlocks<IMyUpgradeModule>())
                {
                    if (!NavigationControllerIdentity.IsNavigationController(module)) continue;
                    activeCoreIds.Add(module.EntityId);
                    if (!infoBlocks.ContainsKey(module.EntityId))
                    {
                        module.AppendingCustomInfo += AppendAgentCustomInfo;
                        infoBlocks[module.EntityId] = module;
                    }
                    var agentSnapshot = AgentCoreSnapshotBuilder.Capture(module, simulationTick);
                    if (agentSnapshot != null)
                    {
                        latestSnapshots[module.EntityId] = agentSnapshot;
                        module.SetDetailedInfoDirty();
                    }
                    string providers;
                    string winner;
                    InspectIntentProviders(grid, out providers, out winner);
                    result.Add(new NavigationControllerSnapshot
                    {
                        EntityId = module.EntityId,
                        Name = module.CustomName,
                        GridId = grid.EntityId,
                        Settings = NavigationControllerSettingsStore.Read(module),
                        IntentProviders = providers,
                        WinningIntent = winner,
                        Agent = agentSnapshot
                    });
                }
            }
            RemoveMissingCores(activeCoreIds);
            return result;
        }

        public void Clear()
        {
            foreach (var block in infoBlocks.Values)
                if (block != null) block.AppendingCustomInfo -= AppendAgentCustomInfo;
            infoBlocks.Clear();
            latestSnapshots.Clear();
        }

        private void AppendAgentCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            if (block == null || builder == null) return;
            AgentCoreSnapshot snapshot;
            if (latestSnapshots.TryGetValue(block.EntityId, out snapshot))
                snapshot.AppendCustomInfo(builder);
            else
                builder.AppendLine("\nVoidwright agent enrollment is waiting for an authoritative snapshot.");
        }

        private void RemoveMissingCores(HashSet<long> activeCoreIds)
        {
            var removed = new List<long>();
            foreach (var coreId in infoBlocks.Keys)
                if (!activeCoreIds.Contains(coreId)) removed.Add(coreId);
            foreach (var coreId in removed)
            {
                infoBlocks[coreId].AppendingCustomInfo -= AppendAgentCustomInfo;
                infoBlocks.Remove(coreId);
                latestSnapshots.Remove(coreId);
            }
        }

        private static void InspectIntentProviders(IMyCubeGrid root, out string providers, out string winner)
        {
            var grids = new List<IMyCubeGrid>();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Physical, grids);
            if (grids.Count == 0) grids.Add(root);

            var basicCount = 0;
            var recorderCount = 0;
            var offensiveCount = 0;
            var defensiveCount = 0;
            winner = "none";
            var priority = 0;

            foreach (var grid in grids)
            {
                foreach (var defensive in grid.GetFatBlocks<IMyDefensiveCombatBlock>())
                {
                    defensiveCount++;
                    if (defensive.Enabled && defensive.IsFleeing && priority < 400)
                    {
                        priority = 400;
                        winner = "defensive:flee";
                    }
                }
                foreach (var offensive in grid.GetFatBlocks<IMyOffensiveCombatBlock>())
                {
                    offensiveCount++;
                    if (offensive.Enabled && priority < 300)
                    {
                        priority = 300;
                        winner = "offensive:configured";
                    }
                }
                foreach (var recorder in grid.GetFatBlocks<IMyPathRecorderBlock>())
                {
                    recorderCount++;
                    IMyPathRecorderComponent component;
                    if (recorder.Enabled && recorder.GetComponent(out component) &&
                        component != null && component.IsPlaying && priority < 200)
                    {
                        priority = 200;
                        winner = "recorder:playing";
                    }
                }
                foreach (var basic in grid.GetFatBlocks<IMyBasicMissionBlock>())
                {
                    basicCount++;
                    IMyBasicMissionComponent mission;
                    if (basic.Enabled && basic.TryGetSelectedMission(out mission) &&
                        mission != null && mission.IsSelected && priority < 100)
                    {
                        priority = 100;
                        winner = "basic:" + mission.MissionName;
                    }
                }
            }
            providers = "basic=" + basicCount + ",recorder=" + recorderCount +
                        ",offensive=" + offensiveCount + ",defensive=" + defensiveCount;
        }
    }
}
