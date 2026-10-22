using System;
using System.IO;
using System.Text;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using Voidwright.Core;

namespace Voidwright.ServerPlugin
{
    /// <summary>Plugin-owned WorldStorage transport for the opt-in disposable local test lane.</summary>
    internal static class LocalVehicleTestMailbox
    {
        private const string AuthorizationFile = "voidwright-local-test-authorization-v1.txt";
        private const string CommandFile = "voidwright-local-test-command-v1.txt";
        private const string ReceiptFile = "voidwright-local-test-receipt-v1.txt";

        public static bool TryReadAuthorization(out LocalVehicleTestAuthorization authorization)
            => LocalVehicleTestWire.TryParseAuthorization(Read(AuthorizationFile), out authorization);

        public static bool TryReadCommand(out LocalVehicleTestCommand command)
            => LocalVehicleTestWire.TryParseCommand(Read(CommandFile), out command);

        public static bool WriteReceipt(LocalVehicleTestReceipt receipt)
        {
            try
            {
                var text = LocalVehicleTestWire.FormatReceipt(receipt);
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ReceiptFile, typeof(MySessionComponentBase)))
                    writer.Write(text);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ReceiptFile, typeof(MySessionComponentBase)))
                    return string.Equals(reader.ReadToEnd(), text, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string Read(string file)
        {
            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(file, typeof(MySessionComponentBase)))
                {
                    var buffer = new char[4097];
                    var count = reader.Read(buffer, 0, buffer.Length);
                    if (count <= 0 || count > 4096) return string.Empty;
                    return new string(buffer).Substring(0, count);
                }
            }
            catch { return string.Empty; }
        }
    }
}
