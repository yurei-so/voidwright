using System;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    public sealed class VoidwrightDiscoveryReceipt
    {
        public const string Header = "VWDR2";
        public string Status, RunId, FixtureId, BootstrapNonce, SessionId, ExporterRunId, Fingerprint;
        public long Tick;
        public DateTime CapturedUtc;
        public int ObservedGridCount;

        public string Format()
        {
            if ((Status != "mod-ready" && Status != "host-ready") || !Hex(RunId, 32) ||
                !Hex(FixtureId, 32) || !Hex(BootstrapNonce, 32) || !Hex(SessionId, 32) ||
                !Hex(ExporterRunId, 32) || !Hex(Fingerprint, 64) || Tick < 0 ||
                ObservedGridCount < 0 || ObservedGridCount > 100)
                throw new ArgumentException("Invalid Voidwright discovery receipt.");
            return Header + "\n" + string.Join("|", Status, RunId, FixtureId, BootstrapNonce,
                SessionId, ExporterRunId, Tick.ToString(CultureInfo.InvariantCulture),
                CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), Fingerprint,
                ObservedGridCount.ToString(CultureInfo.InvariantCulture)) + "\n";
        }

        public static bool TryParse(string text, out VoidwrightDiscoveryReceipt receipt)
        {
            receipt = null;
            if (string.IsNullOrEmpty(text) || text.Length > 2048 || !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || lines[0] != Header || lines[2].Length != 0) return false;
            var f = lines[1].Split('|');
            long tick; int gridCount; DateTime captured;
            if (f.Length != 10 || (f[0] != "mod-ready" && f[0] != "host-ready") ||
                !Hex(f[1], 32) || !Hex(f[2], 32) || !Hex(f[3], 32) || !Hex(f[4], 32) ||
                !Hex(f[5], 32) || !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out tick) || tick < 0 ||
                !DateTime.TryParseExact(f[7], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured) ||
                captured.Kind != DateTimeKind.Utc || !Hex(f[8], 64) ||
                !int.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out gridCount) || gridCount < 0 || gridCount > 100) return false;
            receipt = new VoidwrightDiscoveryReceipt { Status = f[0], RunId = f[1], FixtureId = f[2],
                BootstrapNonce = f[3], SessionId = f[4], ExporterRunId = f[5], Tick = tick,
                CapturedUtc = captured, Fingerprint = f[8], ObservedGridCount = gridCount };
            return true;
        }

        public bool IsFreshAndBound(DateTime nowUtc, TimeSpan maximumAge, string status, string runId,
            string fixtureId, string nonce, string sessionId, string exporterRunId, long tick, string fingerprint)
        {
            var age = nowUtc.ToUniversalTime() - CapturedUtc.ToUniversalTime();
            return Status == status && RunId == runId && FixtureId == fixtureId && BootstrapNonce == nonce &&
                SessionId == sessionId && ExporterRunId == exporterRunId && Tick == tick && Fingerprint == fingerprint &&
                ObservedGridCount > 0 && age >= TimeSpan.Zero && age <= maximumAge;
        }

        internal static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    public sealed class VoidwrightStageTwoActivation
    {
        public const string Header = "VWCA1";
        public string Action = "preflight-only";
        public string RunId, FixtureId, OperationId, BootstrapNonce, SessionId, ExporterRunId;
        public string Fingerprint, SuspensionIds, ContextPath, ContextSha256, Challenge;
        public long Tick, CoreId, GridId, ControllerId, CameraId;
        public DateTime IssuedUtc, ExpiresUtc;

        public string Format()
        {
            if (!VoidwrightDiscoveryReceipt.Hex(RunId, 32) || !VoidwrightDiscoveryReceipt.Hex(FixtureId, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(OperationId, 32) || !VoidwrightDiscoveryReceipt.Hex(BootstrapNonce, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(SessionId, 32) || !VoidwrightDiscoveryReceipt.Hex(ExporterRunId, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(Fingerprint, 64) || !VoidwrightDiscoveryReceipt.Hex(ContextSha256, 64) ||
                !VoidwrightDiscoveryReceipt.Hex(Challenge, 32) || Tick < 0 || CoreId <= 0 || GridId <= 0 ||
                ControllerId <= 0 || CameraId <= 0 || string.IsNullOrEmpty(ContextPath) ||
                (Action != "preflight-only" && Action != "direct-maneuver") ||
                ContextPath.IndexOfAny(new[] { '\r', '\n', '|' }) >= 0 || !ValidIds(SuspensionIds) ||
                ExpiresUtc <= IssuedUtc || ExpiresUtc - IssuedUtc > TimeSpan.FromMinutes(2))
                throw new ArgumentException("Invalid stage-two Voidwright activation.");
            var path = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContextPath));
            var fields = new[] { RunId, FixtureId, OperationId, BootstrapNonce, SessionId, ExporterRunId,
                Tick.ToString(CultureInfo.InvariantCulture), Fingerprint, CoreId.ToString(CultureInfo.InvariantCulture),
                GridId.ToString(CultureInfo.InvariantCulture), ControllerId.ToString(CultureInfo.InvariantCulture),
                CameraId.ToString(CultureInfo.InvariantCulture), SuspensionIds, path, ContextSha256, Challenge,
                IssuedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ExpiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), Action };
            return Header + "\n" + string.Join("|", fields) + "\n";
        }

        public static bool TryParse(string text, DateTime nowUtc, out VoidwrightStageTwoActivation activation)
        {
            activation = null;
            if (string.IsNullOrEmpty(text) || text.Length > 8192 || !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || lines[0] != Header || lines[2].Length != 0) return false;
            var f = lines[1].Split('|');
            long tick, core, grid, controller, camera; DateTime issued, expires; string path;
            if (f.Length != 19 || (f[18] != "preflight-only" && f[18] != "direct-maneuver") ||
                !VoidwrightDiscoveryReceipt.Hex(f[0], 32) || !VoidwrightDiscoveryReceipt.Hex(f[1], 32) ||
                !VoidwrightDiscoveryReceipt.Hex(f[2], 32) || !VoidwrightDiscoveryReceipt.Hex(f[3], 32) ||
                !VoidwrightDiscoveryReceipt.Hex(f[4], 32) || !VoidwrightDiscoveryReceipt.Hex(f[5], 32) ||
                !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out tick) || tick < 0 ||
                !VoidwrightDiscoveryReceipt.Hex(f[7], 64) ||
                !long.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out core) || core <= 0 ||
                !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out grid) || grid <= 0 ||
                !long.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out controller) || controller <= 0 ||
                !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out camera) || camera <= 0 ||
                !ValidIds(f[12]) || !TryBase64(f[13], out path) || string.IsNullOrWhiteSpace(path) ||
                !VoidwrightDiscoveryReceipt.Hex(f[14], 64) || !VoidwrightDiscoveryReceipt.Hex(f[15], 32) ||
                !DateTime.TryParseExact(f[16], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out issued) ||
                !DateTime.TryParseExact(f[17], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expires) ||
                issued.Kind != DateTimeKind.Utc || expires.Kind != DateTimeKind.Utc || expires <= issued ||
                expires - issued > TimeSpan.FromMinutes(2) || nowUtc.ToUniversalTime() < issued || nowUtc.ToUniversalTime() > expires)
                return false;
            activation = new VoidwrightStageTwoActivation { RunId = f[0], FixtureId = f[1], OperationId = f[2],
                BootstrapNonce = f[3], SessionId = f[4], ExporterRunId = f[5], Tick = tick, Fingerprint = f[7],
                CoreId = core, GridId = grid, ControllerId = controller, CameraId = camera,
                SuspensionIds = f[12], ContextPath = path, ContextSha256 = f[14], Challenge = f[15],
                IssuedUtc = issued, ExpiresUtc = expires, Action = f[18] };
            return true;
        }

        public bool Matches(VoidwrightDiscoveryReceipt receipt, DateTime nowUtc, string requiredStatus)
        {
            return receipt != null && receipt.IsFreshAndBound(nowUtc,
                TimeSpan.FromSeconds(15), requiredStatus, RunId, FixtureId, BootstrapNonce, SessionId,
                ExporterRunId, Tick, Fingerprint);
        }

        public bool MatchesCurrentDiscovery(VoidwrightDiscoveryReceipt receipt, DateTime nowUtc,
            string requiredStatus)
        {
            if (receipt == null) return false;
            var age = nowUtc.ToUniversalTime() - receipt.CapturedUtc.ToUniversalTime();
            return receipt.Status == requiredStatus && receipt.RunId == RunId &&
                receipt.FixtureId == FixtureId && receipt.BootstrapNonce == BootstrapNonce &&
                receipt.SessionId == SessionId && receipt.ExporterRunId == ExporterRunId &&
                receipt.Tick >= Tick && (receipt.Tick != Tick || receipt.Fingerprint == Fingerprint) &&
                receipt.ObservedGridCount > 0 && age >= TimeSpan.Zero &&
                age <= TimeSpan.FromSeconds(15);
        }

        private static bool TryBase64(string value, out string decoded)
        {
            decoded = string.Empty;
            try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)); return true; }
            catch { return false; }
        }

        internal static bool ValidIds(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var ids = value.Split(',');
            var seen = new System.Collections.Generic.HashSet<long>();
            foreach (var item in ids)
            {
                long id;
                if (!long.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0 || !seen.Add(id)) return false;
            }
            return true;
        }
    }

    public sealed class VoidwrightStageTwoAcknowledgement
    {
        public const string Header = "VWAA1";
        public string Status, RunId, FixtureId, OperationId, BootstrapNonce, SessionId, ExporterRunId;
        public string Fingerprint, ContextSha256;
        public long Tick;
        public DateTime UpdatedUtc;

        public string Format()
        {
            if ((Status != "host-accepted" && Status != "mod-accepted" && Status != "rejected") ||
                !VoidwrightDiscoveryReceipt.Hex(RunId, 32) || !VoidwrightDiscoveryReceipt.Hex(FixtureId, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(OperationId, 32) || !VoidwrightDiscoveryReceipt.Hex(BootstrapNonce, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(SessionId, 32) || !VoidwrightDiscoveryReceipt.Hex(ExporterRunId, 32) ||
                !VoidwrightDiscoveryReceipt.Hex(Fingerprint, 64) || !VoidwrightDiscoveryReceipt.Hex(ContextSha256, 64) || Tick < 0)
                throw new ArgumentException("Invalid stage-two activation acknowledgement.");
            return Header + "\n" + string.Join("|", Status, RunId, FixtureId, OperationId, BootstrapNonce,
                SessionId, ExporterRunId, Tick.ToString(CultureInfo.InvariantCulture), Fingerprint, ContextSha256,
                UpdatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) + "\n";
        }
    }
}
