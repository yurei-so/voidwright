using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Voidwright.ServerPlugin
{
    internal static class MovementPreflightSnapshotStore
    {
        public static void Write(string quarantineDirectory, string runId, string operationId,
            DateTime createdAtUtc, string json)
        {
            if (string.IsNullOrEmpty(quarantineDirectory)) throw new ArgumentException("A quarantine directory is required.", "quarantineDirectory");
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("A run ID is required.", "runId");
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("An operation ID is required.", "operationId");
            if (json == null) throw new ArgumentNullException("json");

            var timestamp = createdAtUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
            var snapshotDirectory = Path.Combine(quarantineDirectory, "movement-preflight-snapshots");
            Directory.CreateDirectory(snapshotDirectory);
            var snapshotPath = Path.Combine(snapshotDirectory,
                "movement-preflight-" + runId + "-" + operationId + "-" + timestamp + ".json");
            using (var stream = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(json);

            var latestPath = Path.Combine(quarantineDirectory, "movement-preflight-" + runId + "-" + operationId + ".json");
            var temporaryPath = latestPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(latestPath)) File.Replace(temporaryPath, latestPath, null);
            else File.Move(temporaryPath, latestPath);
        }
    }
}
