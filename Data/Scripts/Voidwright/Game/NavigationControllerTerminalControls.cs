using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Voidwright.Game
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public sealed class NavigationControllerTerminalControls : MySessionComponentBase
    {
        private static bool registered;

        public override void BeforeStart()
        {
            if (registered || MyAPIGateway.TerminalControls == null) return;
            registered = true;

            var separator = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSeparator, IMyUpgradeModule>("Voidwright_NavigationSeparator");
            separator.Visible = IsVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(separator);

            var armed = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlCheckbox, IMyUpgradeModule>("Voidwright_NavigationArmed");
            armed.Title = MyStringId.GetOrCompute("Navigation armed");
            armed.Tooltip = MyStringId.GetOrCompute(
                "Persist the operator's intent to grant control authority. This build records the setting but does not drive the vehicle.");
            armed.Visible = IsVisible;
            armed.SupportsMultipleBlocks = true;
            armed.Getter = block => NavigationControllerSettingsStore.Read(block).Armed;
            armed.Setter = NavigationControllerSettingsStore.SetArmed;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(armed);

            var mobility = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlCombobox, IMyUpgradeModule>("Voidwright_NavigationMobility");
            mobility.Title = MyStringId.GetOrCompute("Mobility mode");
            mobility.Tooltip = MyStringId.GetOrCompute(
                "Auto preserves detected LAND, AIR, or HYBRID evidence. Explicit modes select the future control adapter.");
            mobility.Visible = IsVisible;
            mobility.SupportsMultipleBlocks = true;
            mobility.ComboBoxContent = FillMobilityModes;
            mobility.Getter = block => (long)NavigationControllerSettingsStore.Read(block).MobilityMode;
            mobility.Setter = (block, value) => NavigationControllerSettingsStore.SetMobilityMode(
                block, (NavigationMobilityMode)value);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(mobility);

            var destination = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlTextbox, IMyUpgradeModule>("Voidwright_NavigationDestination");
            destination.Title = MyStringId.GetOrCompute("Destination GPS");
            destination.Tooltip = MyStringId.GetOrCompute(
                "Paste a GPS string here. The dedicated controller owns this goal; this build only persists it.");
            destination.Visible = IsVisible;
            destination.SupportsMultipleBlocks = false;
            destination.Getter = block => new StringBuilder(
                NavigationControllerSettingsStore.Read(block).DestinationGps ?? "");
            destination.Setter = (block, value) => NavigationControllerSettingsStore.SetDestinationGps(
                block, value == null ? "" : value.ToString());
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(destination);

            var vanillaAi = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlCombobox, IMyUpgradeModule>("Voidwright_VanillaAiPolicy");
            vanillaAi.Title = MyStringId.GetOrCompute("Vanilla AI authority");
            vanillaAi.Tooltip = MyStringId.GetOrCompute(
                "Observe Only never changes vanilla AI. Suspend When Armed records exclusive-authority intent for the guarded follower slice.");
            vanillaAi.Visible = IsVisible;
            vanillaAi.SupportsMultipleBlocks = true;
            vanillaAi.ComboBoxContent = FillVanillaAiPolicies;
            vanillaAi.Getter = block => (long)NavigationControllerSettingsStore.Read(block).VanillaAi;
            vanillaAi.Setter = (block, value) => NavigationControllerSettingsStore.SetVanillaAiPolicy(
                block, (VanillaAiPolicy)value);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(vanillaAi);
        }

        private static bool IsVisible(IMyTerminalBlock block)
            => NavigationControllerIdentity.IsNavigationController(block);

        private static void FillMobilityModes(List<MyTerminalControlComboBoxItem> items)
        {
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)NavigationMobilityMode.Auto,
                Value = MyStringId.GetOrCompute("Auto (detected)")
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)NavigationMobilityMode.Land,
                Value = MyStringId.GetOrCompute("LAND")
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)NavigationMobilityMode.Air,
                Value = MyStringId.GetOrCompute("AIR")
            });
        }

        private static void FillVanillaAiPolicies(List<MyTerminalControlComboBoxItem> items)
        {
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)VanillaAiPolicy.ObserveOnly,
                Value = MyStringId.GetOrCompute("Observe only")
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)VanillaAiPolicy.SuspendWhenArmed,
                Value = MyStringId.GetOrCompute("Suspend when armed")
            });
        }
    }
}
