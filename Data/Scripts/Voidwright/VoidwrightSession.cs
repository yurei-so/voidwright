using Sandbox.ModAPI;
using System.Collections.Generic;
using Voidwright.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace Voidwright
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public sealed class VoidwrightSession : MySessionComponentBase
    {
        private VehicleProbe probe;
        private RoutePreviewManager previews;
        private NavigationControllerRegistry navigationControllers;
        private readonly Dictionary<long, string> observedSignatures = new Dictionary<long, string>();
        private readonly Dictionary<long, string> controllerSignatures = new Dictionary<long, string>();
        private int updateCountdown = 100;
        private bool roleLogged;

        public override void LoadData()
        {
            probe = new VehicleProbe();
            previews = new RoutePreviewManager();
            navigationControllers = new NavigationControllerRegistry();
            if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                MyAPIGateway.Utilities.ShowMessage("Voidwright", "observation probe loaded; tag a Remote Control [Voidwright]");
            MyLog.Default.WriteLineAndConsole("Voidwright: observation probe loaded; control adapters are inactive.");
        }

        public override void UpdateBeforeSimulation()
        {
            updateCountdown--;
            if (updateCountdown > 0) return;
            updateCountdown = 100;
            if (probe == null)
                return;

            var dedicated = MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated;
            var multiplayerAvailable = MyAPIGateway.Multiplayer != null;
            var multiplayerServer = multiplayerAvailable && MyAPIGateway.Multiplayer.IsServer;
            if (!roleLogged)
            {
                roleLogged = true;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright probe role: dedicated=" + dedicated +
                    " multiplayerAvailable=" + multiplayerAvailable +
                    " multiplayerServer=" + multiplayerServer);
            }
            // Dedicated hosts may expose Multiplayer late or not at all under the
            // headless loader. Observation is read-only, so either trustworthy
            // server signal is sufficient; clients still remain excluded.
            if (!dedicated && !multiplayerServer)
                return;

            var current = new HashSet<long>();
            var snapshots = probe.ObserveOptedInVehicles();
            foreach (var snapshot in snapshots)
            {
                current.Add(snapshot.ControllerId);
                var signature = snapshot.Signature();
                string previous;
                if (observedSignatures.TryGetValue(snapshot.ControllerId, out previous) && previous == signature)
                    continue;
                observedSignatures[snapshot.ControllerId] = signature;
                MyLog.Default.WriteLineAndConsole("Voidwright probe: " + snapshot);
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=vehicle " + snapshot);
                if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                    MyAPIGateway.Utilities.ShowNotification("Voidwright observed " + snapshot.Mobility + " vehicle: " + snapshot.ControllerName, 4000, "White");
            }

            var removed = new List<long>();
            foreach (var controllerId in observedSignatures.Keys)
                if (!current.Contains(controllerId)) removed.Add(controllerId);
            foreach (var controllerId in removed)
            {
                observedSignatures.Remove(controllerId);
                MyLog.Default.WriteLineAndConsole("Voidwright probe: controller " + controllerId + " no longer opted in.");
            }
            if (previews != null) previews.Update(snapshots);
            ObserveNavigationControllers();
        }

        private void ObserveNavigationControllers()
        {
            if (navigationControllers == null) return;
            var active = new HashSet<long>();
            foreach (var controller in navigationControllers.Observe())
            {
                active.Add(controller.EntityId);
                var signature = controller.Signature();
                string previous;
                if (controllerSignatures.TryGetValue(controller.EntityId, out previous) && previous == signature)
                    continue;
                controllerSignatures[controller.EntityId] = signature;
                MyLog.Default.WriteLineAndConsole("Voidwright navigation controller: name='" + controller.Name +
                                                  "' id=" + controller.EntityId + " grid=" + controller.GridId +
                                                  " armed=" + controller.Settings.Armed +
                                                  " mode=" + controller.Settings.MobilityMode +
                                                  " destination=" +
                                                  (string.IsNullOrWhiteSpace(controller.Settings.DestinationGps) ? "unset" : "set") +
                                                  " vanillaAi=" + controller.Settings.VanillaAi +
                                                  " providers=" + controller.IntentProviders +
                                                  " winner=" + controller.WinningIntent +
                                                  " authority=none");
                if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                    MyAPIGateway.Utilities.ShowNotification(
                        "Voidwright controller: " + controller.Settings.MobilityMode +
                        (controller.Settings.Armed ? " armed (no authority yet)" : " disarmed"),
                        4000, "White");
            }
            var removed = new List<long>();
            foreach (var id in controllerSignatures.Keys)
                if (!active.Contains(id)) removed.Add(id);
            foreach (var id in removed) controllerSignatures.Remove(id);
        }

        protected override void UnloadData()
        {
            observedSignatures.Clear();
            controllerSignatures.Clear();
            if (previews != null) previews.ClearAll();
            previews = null;
            navigationControllers = null;
            probe = null;
            MyLog.Default.WriteLineAndConsole("Voidwright: session unloaded.");
        }
    }
}
