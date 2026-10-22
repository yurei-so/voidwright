using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using Voidwright.Core;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Voidwright.Game
{
    // The lab mod is server-only. Vanilla GPS replication reaches unmodified clients.
    public sealed class VirtualCameraMarkerManager
    {
        private sealed class ShownMarker
        {
            public IMyGps Gps;
            public string Signature;
        }

        private readonly Dictionary<long, Dictionary<string, ShownMarker>> recipients =
            new Dictionary<long, Dictionary<string, ShownMarker>>();
        private VirtualCameraMarkerBatch latest;
        private string currentRunId;
        private string lastServerLog;

        public void Broadcast(string exporterRunId)
        {
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer ||
                MyAPIGateway.Session == null || MyAPIGateway.Session.GPS == null ||
                MyAPIGateway.Players == null || MyAPIGateway.Utilities == null ||
                string.IsNullOrEmpty(exporterRunId)) return;
            if (currentRunId != exporterRunId)
            {
                Clear();
                currentRunId = exporterRunId;
            }
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(
                    VirtualCameraMarkerWire.FileName, typeof(VoidwrightSession)))
                {
                    string packet;
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                        VirtualCameraMarkerWire.FileName, typeof(VoidwrightSession)))
                    {
                        var buffer = new char[VirtualCameraMarkerWire.MaxBytes + 1];
                        var count = reader.ReadBlock(buffer, 0, buffer.Length);
                        packet = count <= VirtualCameraMarkerWire.MaxBytes
                            ? new string(buffer, 0, count) : null;
                    }
                    VirtualCameraMarkerBatch parsed;
                    if (packet != null && VirtualCameraMarkerWire.TryParse(packet, out parsed) &&
                        parsed.RunId == exporterRunId) latest = parsed;
                }
                SyncRecipients();
                if (latest != null && lastServerLog != latest.RunId + ":" + latest.Markers.Count)
                {
                    lastServerLog = latest.RunId + ":" + latest.Markers.Count;
                    MyLog.Default.WriteLineAndConsole("Voidwright camera markers: syncing " +
                        latest.Markers.Count + " display-only poses through vanilla GPS.");
                }
            }
            catch (Exception error)
            {
                var message = error.GetType().Name + ": " + error.Message;
                if (lastServerLog == message) return;
                lastServerLog = message;
                MyLog.Default.WriteLineAndConsole("Voidwright camera marker GPS unavailable: " + message);
            }
        }

        public void Clear()
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.GPS != null)
                foreach (var recipient in recipients)
                    foreach (var shown in recipient.Value.Values)
                        MyAPIGateway.Session.GPS.RemoveGps(recipient.Key, shown.Gps);
            recipients.Clear();
            latest = null;
            currentRunId = null;
            lastServerLog = null;
        }

        private void SyncRecipients()
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, player => player != null && !player.IsBot);
            var connected = new HashSet<long>();
            foreach (var player in players)
            {
                if (player.IdentityId <= 0) continue;
                connected.Add(player.IdentityId);
                Dictionary<string, ShownMarker> shown;
                if (!recipients.TryGetValue(player.IdentityId, out shown))
                {
                    shown = new Dictionary<string, ShownMarker>();
                    recipients.Add(player.IdentityId, shown);
                }
                SyncPlayer(player.IdentityId, shown);
            }
            var departed = new List<long>();
            foreach (var recipient in recipients)
                if (!connected.Contains(recipient.Key)) departed.Add(recipient.Key);
            foreach (var identityId in departed)
            {
                foreach (var shown in recipients[identityId].Values)
                    MyAPIGateway.Session.GPS.RemoveGps(identityId, shown.Gps);
                recipients.Remove(identityId);
            }
        }

        private void SyncPlayer(long identityId, Dictionary<string, ShownMarker> shown)
        {
            var wanted = new HashSet<string>();
            if (latest != null)
                foreach (var marker in latest.Markers)
                {
                    var active = VirtualCameraMarkerWire.Fresh(latest, marker, DateTime.UtcNow);
                    var label = marker.Id.Substring(0, 8);
                    var color = active ? new Color(72, 225, 255) : new Color(255, 170, 75);
                    var state = active ? "ACTIVE" : "STALE";
                    var origin = new Vector3D(marker.Position[0], marker.Position[1], marker.Position[2]);
                    var forward = new Vector3D(marker.Forward[0], marker.Forward[1], marker.Forward[2]);
                    var up = new Vector3D(marker.Up[0], marker.Up[1], marker.Up[2]);
                    var description = "Voidwright virtual camera " + marker.Id + "; " + state +
                        ". Display only; no physical camera or collision.";
                    SyncOne(identityId, shown, wanted, marker.Id + ":cam",
                        "VW CAM " + label + " " + state, description + " Position", origin, color);
                    SyncOne(identityId, shown, wanted, marker.Id + ":look",
                        "VW LOOK " + label, description + " Forward tip 3 m from position",
                        origin + forward * 3, color);
                    SyncOne(identityId, shown, wanted, marker.Id + ":up",
                        "VW UP " + label, description + " Up tip 2 m from position",
                        origin + up * 2, color);
                }
            var removed = new List<string>();
            foreach (var entry in shown)
                if (!wanted.Contains(entry.Key)) removed.Add(entry.Key);
            foreach (var key in removed)
            {
                MyAPIGateway.Session.GPS.RemoveGps(identityId, shown[key].Gps);
                shown.Remove(key);
            }
        }

        private static void SyncOne(long identityId, Dictionary<string, ShownMarker> shown,
            HashSet<string> wanted, string key, string name, string description,
            Vector3D position, Color color)
        {
            wanted.Add(key);
            var signature = name + "|" + description + "|" +
                position.X.ToString("R", CultureInfo.InvariantCulture) + "|" +
                position.Y.ToString("R", CultureInfo.InvariantCulture) + "|" +
                position.Z.ToString("R", CultureInfo.InvariantCulture);
            ShownMarker existing;
            if (!shown.TryGetValue(key, out existing))
            {
                var gps = MyAPIGateway.Session.GPS.Create(name, description, position, true, false);
                gps.GPSColor = color;
                MyAPIGateway.Session.GPS.AddGps(identityId, gps);
                shown.Add(key, new ShownMarker { Gps = gps, Signature = signature });
            }
            else if (existing.Signature != signature)
            {
                existing.Gps.Name = name;
                existing.Gps.Description = description;
                existing.Gps.Coords = position;
                existing.Gps.GPSColor = color;
                MyAPIGateway.Session.GPS.ModifyGps(identityId, existing.Gps);
                existing.Signature = signature;
            }
        }
    }
}
