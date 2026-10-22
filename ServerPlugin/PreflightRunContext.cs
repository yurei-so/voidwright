using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>Verified immutable VRageCage run and source provenance passed by its launcher.</summary>
    public sealed class PreflightRunContext
    {
        private readonly Dictionary<string, string> fields;
        public readonly string RunId;
        public readonly string FixtureId;
        public readonly string Instance;
        public readonly string OperationId;
        public readonly string Action;
        public readonly string ContextSha256;
        public readonly long ControllerId;
        public readonly long DepthCameraId;
        public readonly long GridId;
        public readonly long AgentCoreId;
        public readonly string DiscoveryRunId;
        public readonly long DiscoveryTick;
        public readonly string DiscoveryFingerprint;
        public readonly string DirectManeuverSegments;
        public DirectManeuverPlan DirectManeuverPlan { get; private set; }
        public readonly double TargetX;
        public readonly double TargetY;
        public readonly double TargetZ;
        public readonly double MaximumSpeed;
        public readonly int TimeoutTicks;
        public readonly double ArrivalRadius;
        public readonly int StartDelayTicks;
        public readonly bool Reverse;
        public readonly string SourceTreeSha256;
        public readonly string FixtureSectorSha256;
        public readonly string ModsTreeSha256;

        private PreflightRunContext(Dictionary<string, string> values, string contextSha256)
        {
            fields = values;
            RunId = Get(values, "run_id");
            FixtureId = Get(values, "fixture_id");
            Instance = Get(values, "instance");
            OperationId = Get(values, "operation_id");
            Action = Get(values, "action");
            ContextSha256 = contextSha256;
            SourceTreeSha256 = Get(values, "source_tree_sha256");
            FixtureSectorSha256 = Get(values, "fixture_sector_sha256");
            ModsTreeSha256 = Get(values, "mods_tree_sha256");
            ControllerId = Long(Get(values, "controller_id"));
            DepthCameraId = Long(Get(values, "depth_camera_id"));
            GridId = Long(Get(values, "grid_id"));
            AgentCoreId = Long(Get(values, "agent_core_id"));
            DiscoveryRunId = Get(values, "discovery_run_id");
            DiscoveryTick = Long(Get(values, "discovery_tick"));
            DiscoveryFingerprint = Get(values, "discovery_fingerprint");
            DirectManeuverSegments = Get(values, "maneuver_segments");
            TargetX = Number(Get(values, "target_x"));
            TargetY = Number(Get(values, "target_y"));
            TargetZ = Number(Get(values, "target_z"));
            MaximumSpeed = Number(Get(values, "max_speed"));
            TimeoutTicks = (int)Long(Get(values, "timeout_ticks"));
            ArrivalRadius = Number(Get(values, "arrival_radius"));
            StartDelayTicks = (int)Long(Get(values, "start_delay_ticks"));
            Reverse = Get(values, "reverse") == "true";
        }

        public static bool TryLoad(string path, string expectedSha256, string expectedRunId,
            string expectedFixtureId, string expectedOperationId, string expectedAction,
            out PreflightRunContext context, out string reason)
        {
            context = null;
            reason = "preflight run context is unavailable";
            try
            {
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(path))
                    return false;
                var fullContextPath = Path.GetFullPath(path);
                if (Path.GetFileName(fullContextPath) != "preflight-context-v1.txt" ||
                    Path.GetFileName(Path.GetDirectoryName(fullContextPath)) != expectedRunId ||
                    new FileInfo(fullContextPath).Length > 32768)
                {
                    reason = "preflight context path or size is invalid";
                    return false;
                }
                var bytes = File.ReadAllBytes(path);
                var actualSha = Sha256(bytes);
                if (!Hex64(expectedSha256) || !string.Equals(actualSha, expectedSha256, StringComparison.Ordinal))
                {
                    reason = "preflight context digest mismatch";
                    return false;
                }
                var text = Encoding.UTF8.GetString(bytes);
                var values = ParseFields(text);
                var required = new[] { "schema", "run_id", "fixture_id", "instance", "operation_id", "action",
                    "controller_id", "depth_camera_id", "grid_id", "agent_core_id", "discovery_run_id",
                    "discovery_tick", "discovery_fingerprint", "maneuver_segments",
                    "target_x", "target_y", "target_z", "max_speed",
                    "timeout_ticks", "arrival_radius", "start_delay_ticks", "reverse", "source_tree_sha256",
                    "fixture_sector_path", "fixture_sector_sha256", "mods_path", "mods_tree_sha256", "contract_path", "contract_sha256",
                    "import_receipt_path", "import_receipt_sha256", "active_run_path", "active_run_sha256",
                    "run_receipt_path", "run_receipt_sha256", "command_path", "command_sha256",
                    "edit_ledger_path", "edit_ledger_sha256", "created_at_utc" };
                if (values.Count != required.Length)
                {
                    reason = "preflight context has an incomplete or unexpected field set";
                    return false;
                }
                foreach (var key in required)
                    if (!values.ContainsKey(key))
                    {
                        reason = "preflight context is missing " + key;
                        return false;
                    }
                if (Get(values, "schema") != "vragecage.preflight-context.v1" ||
                    !Hex32(Get(values, "run_id")) || !Hex32(Get(values, "fixture_id")) ||
                    !Hex32(Get(values, "operation_id")) || !Hex64(Get(values, "source_tree_sha256")) ||
                    !Hex64(Get(values, "fixture_sector_sha256")) || !Hex64(Get(values, "mods_tree_sha256")) ||
                    !string.Equals(Get(values, "run_id"), expectedRunId, StringComparison.Ordinal) ||
                    !string.Equals(Get(values, "fixture_id"), expectedFixtureId, StringComparison.Ordinal) ||
                    !string.Equals(Get(values, "operation_id"), expectedOperationId, StringComparison.Ordinal) ||
                    !string.Equals(Get(values, "action"), expectedAction, StringComparison.Ordinal))
                {
                    reason = "preflight context run, fixture, operation, action, or source identity mismatch";
                    return false;
                }
                var instanceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fullContextPath)), ".."));
                var expectedSector = Path.GetFullPath(Path.Combine(instanceRoot, "World", "SANDBOX_0_0_0_.sbs"));
                var expectedMods = Path.GetFullPath(Path.Combine(instanceRoot, "Mods"));
                if (!string.Equals(Path.GetFullPath(Get(values, "fixture_sector_path")), expectedSector, StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFullPath(Get(values, "mods_path")), expectedMods, StringComparison.Ordinal))
                {
                    reason = "preflight context fixture or mod source path is outside the activated instance";
                    return false;
                }
                foreach (var pair in new[] { new[] { "contract_path", "contract_sha256" },
                             new[] { "import_receipt_path", "import_receipt_sha256" },
                             new[] { "active_run_path", "active_run_sha256" },
                             new[] { "run_receipt_path", "run_receipt_sha256" },
                             new[] { "command_path", "command_sha256" } })
                    if (string.IsNullOrWhiteSpace(Get(values, pair[0])) || !Hex64(Get(values, pair[1])))
                    {
                        reason = "preflight context lacks a required immutable source artifact: " + pair[0];
                        return false;
                    }
                var parsed = new PreflightRunContext(values, actualSha);
                if (!Finite(parsed.TargetX) || !Finite(parsed.TargetY) || !Finite(parsed.TargetZ) ||
                    !Finite(parsed.MaximumSpeed) || !Finite(parsed.ArrivalRadius) || parsed.TimeoutTicks < 0 ||
                    parsed.StartDelayTicks < 0 || (Get(values, "reverse") != "true" && Get(values, "reverse") != "false"))
                {
                    reason = "preflight context contains invalid command values";
                    return false;
                }
                if (parsed.Action == "direct-maneuver")
                {
                    if (parsed.GridId <= 0 || parsed.AgentCoreId <= 0 || parsed.DepthCameraId <= 0 ||
                        !Hex32(parsed.DiscoveryRunId) || parsed.DiscoveryTick < 0 || !Hex64(parsed.DiscoveryFingerprint) ||
                        !DirectManeuverPlan.TryParseCompactSegments(parsed.DirectManeuverSegments,
                            parsed.RunId, parsed.FixtureId, parsed.OperationId, parsed.GridId,
                            parsed.ControllerId, parsed.AgentCoreId, out var maneuver) ||
                        parsed.TimeoutTicks != SumTicks(maneuver) + DirectManeuverPlan.MaximumStopConfirmationTicks)
                    {
                        reason = "preflight context contains an invalid or mismatched direct maneuver plan";
                        return false;
                    }
                    parsed.SetManeuver(maneuver);
                }
                else if (parsed.Action == "preflight-only")
                {
                    if (parsed.GridId <= 0 || parsed.ControllerId <= 0 || parsed.DepthCameraId <= 0 ||
                        parsed.AgentCoreId <= 0 || !Hex32(parsed.DiscoveryRunId) || parsed.DiscoveryTick < 0 ||
                        !Hex64(parsed.DiscoveryFingerprint) ||
                        !string.IsNullOrEmpty(parsed.DirectManeuverSegments) || parsed.MaximumSpeed < 0.1 ||
                        parsed.MaximumSpeed > 0.8 || parsed.TimeoutTicks < 60 || parsed.TimeoutTicks > 7200)
                    {
                        reason = "preflight-only context lacks exact sensor/vehicle bindings or bounded observation limits";
                        return false;
                    }
                }
                else if (parsed.GridId != 0 || !string.IsNullOrEmpty(parsed.DirectManeuverSegments))
                {
                    reason = "non-maneuver preflight context contains unsupported direct maneuver fields";
                    return false;
                }
                if (!parsed.VerifyArtifactHashes(out reason)) return false;
                context = parsed;
                reason = "run context and immutable source artifacts verified";
                return true;
            }
            catch (Exception error)
            {
                reason = "preflight context validation failed: " + error.GetType().Name;
                return false;
            }
        }

        private void SetManeuver(DirectManeuverPlan plan)
        {
            // The parsed context is immutable after its artifact digest has been checked.
            DirectManeuverPlan = plan;
        }

        private static int SumTicks(DirectManeuverPlan plan)
        {
            var total = 0;
            foreach (var segment in plan.Segments) total += segment.TimeoutTicks;
            return total;
        }

        private bool VerifyArtifactHashes(out string reason)
        {
            var artifactPairs = new[]
            {
                new[] { "contract_path", "contract_sha256" },
                new[] { "import_receipt_path", "import_receipt_sha256" },
                new[] { "active_run_path", "active_run_sha256" },
                new[] { "run_receipt_path", "run_receipt_sha256" },
                new[] { "command_path", "command_sha256" },
                new[] { "edit_ledger_path", "edit_ledger_sha256" }
            };
            foreach (var pair in artifactPairs)
            {
                var path = Get(fields, pair[0]);
                var expected = Get(fields, pair[1]);
                if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(expected)) continue;
                if (string.IsNullOrEmpty(path) || !Hex64(expected) || !File.Exists(path))
                {
                    reason = "bound source artifact is missing: " + pair[0];
                    return false;
                }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "bound source artifact is a symbolic link: " + pair[0];
                    return false;
                }
                if (!string.Equals(Sha256(File.ReadAllBytes(path)), expected, StringComparison.Ordinal))
                {
                    reason = "bound source artifact digest mismatch: " + pair[0];
                    return false;
                }
            }
            var modsPath = Get(fields, "mods_path");
            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath) ||
                (File.GetAttributes(modsPath) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "staged Voidwright mod tree is missing or redirected";
                return false;
            }
            string modsHash;
            if (!TryTreeSha256(modsPath, out modsHash, out reason)) return false;
            if (!string.Equals(modsHash, Get(fields, "mods_tree_sha256"), StringComparison.Ordinal))
            {
                reason = "staged Voidwright mod tree digest mismatch";
                return false;
            }
            reason = "all bound source artifact digests match";
            return true;
        }

        private static bool TryTreeSha256(string root, out string result, out string reason)
        {
            result = string.Empty;
            try
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var files = new List<string>();
                var pending = new Stack<string>();
                pending.Push(fullRoot);
                long totalBytes = 0;
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    foreach (var entry in Directory.GetFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            reason = "staged mod tree contains a symbolic link";
                            return false;
                        }
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else
                        {
                            totalBytes += new FileInfo(entry).Length;
                            if (files.Count >= 100000 || totalBytes > 4L * 1024 * 1024 * 1024)
                            {
                                reason = "staged mod tree exceeds the bounded preflight hash limit";
                                return false;
                            }
                            files.Add(entry);
                        }
                    }
                }
                files.Sort((left, right) => StringComparer.Ordinal.Compare(left, right));
                if (files.Count == 0)
                {
                    reason = "staged mod tree is empty";
                    return false;
                }
                using (var sha = SHA256.Create())
                {
                    var separator = new byte[] { 0 };
                    var buffer = new byte[65536];
                    foreach (var file in files)
                    {
                        var relative = file.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                        var name = Encoding.UTF8.GetBytes(relative);
                        sha.TransformBlock(name, 0, name.Length, name, 0);
                        sha.TransformBlock(separator, 0, 1, separator, 0);
                        using (var stream = File.OpenRead(file))
                        {
                            int count;
                            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                                sha.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        sha.TransformBlock(separator, 0, 1, separator, 0);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    result = BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
                }
                reason = "staged mod tree digest verified";
                return true;
            }
            catch (Exception error)
            {
                reason = "staged mod tree verification failed: " + error.GetType().Name;
                return false;
            }
        }

        private static Dictionary<string, string> ParseFields(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text) || !text.EndsWith("\n", StringComparison.Ordinal))
                throw new FormatException("context must end with a line feed");
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length - 1; index++)
            {
                var line = lines[index];
                var separator = line.IndexOf('=');
                if (separator <= 0 || line.IndexOf('\r') >= 0 || line.IndexOf('\t') >= 0)
                    throw new FormatException("invalid context field");
                var key = line.Substring(0, separator);
                var value = line.Substring(separator + 1);
                if (result.ContainsKey(key)) throw new FormatException("duplicate context field");
                result.Add(key, value);
            }
            return result;
        }

        private static string Get(IDictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static long Long(string value)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static double Number(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : double.NaN;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Hex32(string value) { return Hex(value, 32); }
        private static bool Hex64(string value) { return Hex(value, 64); }
        private static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

}
