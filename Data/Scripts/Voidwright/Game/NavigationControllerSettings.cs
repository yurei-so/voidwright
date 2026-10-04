using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Voidwright.Game
{
    public enum NavigationMobilityMode : long { Auto = 0, Land = 1, Air = 2 }
    public enum VanillaAiPolicy : long { ObserveOnly = 0, SuspendWhenArmed = 1 }

    public struct NavigationControllerSettings
    {
        public bool Armed;
        public NavigationMobilityMode MobilityMode;
        public string DestinationGps;
        public VanillaAiPolicy VanillaAi;
    }

    public static class NavigationControllerIdentity
    {
        public const string SmallSubtype = "VoidwrightNavigationControllerSmall";
        public const string LargeSubtype = "VoidwrightNavigationControllerLarge";

        public static bool IsNavigationController(IMyCubeBlock block)
        {
            if (block == null) return false;
            var subtype = block.BlockDefinition.SubtypeName;
            return subtype == SmallSubtype || subtype == LargeSubtype;
        }
    }

    public static class NavigationControllerSettingsStore
    {
        private const string Section = "Voidwright.Navigation";
        private const string ArmedKey = "Armed";
        private const string MobilityKey = "Mobility";
        private const string DestinationKey = "DestinationGps";
        private const string VanillaAiKey = "VanillaAiPolicy";

        public static NavigationControllerSettings Read(IMyTerminalBlock block)
        {
            var settings = new NavigationControllerSettings
            {
                Armed = false,
                MobilityMode = NavigationMobilityMode.Auto,
                DestinationGps = "",
                VanillaAi = VanillaAiPolicy.ObserveOnly
            };
            if (block == null) return settings;
            var ini = new MyIni();
            MyIniParseResult result;
            if (!ini.TryParse(block.CustomData, out result)) return settings;
            settings.Armed = ini.Get(Section, ArmedKey).ToBoolean(false);
            var mobility = ini.Get(Section, MobilityKey).ToInt64((long)NavigationMobilityMode.Auto);
            if (mobility >= (long)NavigationMobilityMode.Auto && mobility <= (long)NavigationMobilityMode.Air)
                settings.MobilityMode = (NavigationMobilityMode)mobility;
            settings.DestinationGps = ini.Get(Section, DestinationKey).ToString("");
            var vanillaAi = ini.Get(Section, VanillaAiKey).ToInt64((long)VanillaAiPolicy.ObserveOnly);
            if (vanillaAi >= (long)VanillaAiPolicy.ObserveOnly && vanillaAi <= (long)VanillaAiPolicy.SuspendWhenArmed)
                settings.VanillaAi = (VanillaAiPolicy)vanillaAi;
            return settings;
        }

        public static void SetArmed(IMyTerminalBlock block, bool armed)
            => SetValue(block, ArmedKey, armed ? "true" : "false");

        public static void SetMobilityMode(IMyTerminalBlock block, NavigationMobilityMode mode)
            => SetValue(block, MobilityKey, ((long)mode).ToString());

        public static void SetDestinationGps(IMyTerminalBlock block, string destination)
            => SetValue(block, DestinationKey, destination ?? "");

        public static void SetVanillaAiPolicy(IMyTerminalBlock block, VanillaAiPolicy policy)
            => SetValue(block, VanillaAiKey, ((long)policy).ToString());

        private static void SetValue(IMyTerminalBlock block, string key, string value)
        {
            if (block == null) return;
            var ini = new MyIni();
            MyIniParseResult result;
            if (!ini.TryParse(block.CustomData, out result))
            {
                if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowNotification(
                        "Voidwright: Custom Data must contain valid INI text.", 3000, "Red");
                return;
            }
            ini.Set(Section, key, value);
            block.CustomData = ini.ToString();
        }
    }
}
