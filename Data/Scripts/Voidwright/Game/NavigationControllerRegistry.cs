using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

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

        public string Signature()
            => EntityId + ":" + GridId + ":" + Settings.Armed + ":" + Settings.MobilityMode + ":" +
               Settings.DestinationGps + ":" + Settings.VanillaAi + ":" + IntentProviders + ":" + WinningIntent;
    }

    public sealed class NavigationControllerRegistry
    {
        public List<NavigationControllerSnapshot> Observe()
        {
            var result = new List<NavigationControllerSnapshot>();
            if (MyAPIGateway.Entities == null) return result;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                foreach (var module in grid.GetFatBlocks<IMyUpgradeModule>())
                {
                    if (!NavigationControllerIdentity.IsNavigationController(module)) continue;
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
                        WinningIntent = winner
                    });
                }
            }
            return result;
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
