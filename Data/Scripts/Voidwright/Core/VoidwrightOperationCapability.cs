using System;
using System.Globalization;

namespace Voidwright.Core
{
    /// <summary>
    /// Short-lived host-minted movement capability carried through the
    /// run-bound VRage.Game WorldStorage handshake. It is readable only as an
    /// opaque token; every consumer must also validate the complete binding.
    /// </summary>
    public sealed class VoidwrightOperationCapability
    {
        public const string FileName = "voidwright-operation-capability-v1.txt";
        private const string Header = "VWOC1";
        public static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(3);

        public string RunId, FixtureId, OperationId, ContextSha256, Challenge, SessionId;
        public string DiscoveryRunId, DiscoveryFingerprint, Token;
        public long GridId, ControllerId, AgentCoreId, CameraId, DiscoveryTick;
        public DateTime IssuedUtc, ExpiresUtc;

        public string Format()
        {
            if (!Identity(RunId, 32) || !Identity(FixtureId, 32) || !Identity(OperationId, 32) ||
                !Identity(ContextSha256, 64) || !Identity(Challenge, 32) || !Identity(SessionId, 32) ||
                !Identity(DiscoveryRunId, 32) || !Identity(DiscoveryFingerprint, 64) || !IsToken(Token) ||
                GridId <= 0 || ControllerId <= 0 || AgentCoreId <= 0 || CameraId <= 0 || DiscoveryTick < 0 ||
                IssuedUtc.Kind != DateTimeKind.Utc || ExpiresUtc.Kind != DateTimeKind.Utc ||
                ExpiresUtc <= IssuedUtc || ExpiresUtc - IssuedUtc > MaximumLifetime)
                throw new ArgumentException("Invalid run-bound movement capability.");
            return Header + "\n" + string.Join("|", RunId, FixtureId, OperationId, ContextSha256,
                Challenge, SessionId, GridId.ToString(CultureInfo.InvariantCulture),
                ControllerId.ToString(CultureInfo.InvariantCulture), AgentCoreId.ToString(CultureInfo.InvariantCulture),
                CameraId.ToString(CultureInfo.InvariantCulture), DiscoveryRunId,
                DiscoveryTick.ToString(CultureInfo.InvariantCulture), DiscoveryFingerprint, Token,
                IssuedUtc.ToString("O", CultureInfo.InvariantCulture),
                ExpiresUtc.ToString("O", CultureInfo.InvariantCulture)) + "\n";
        }

        public static bool TryParse(string text, DateTime nowUtc, out VoidwrightOperationCapability capability)
        {
            capability = null;
            if (string.IsNullOrEmpty(text) || text.Length > 2048 ||
                !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || lines[0] != Header || lines[2].Length != 0) return false;
            var f = lines[1].Split('|');
            long gridId, controllerId, coreId, cameraId, discoveryTick;
            DateTime issued, expires;
            if (f.Length != 16 || !Identity(f[0], 32) || !Identity(f[1], 32) ||
                !Identity(f[2], 32) || !Identity(f[3], 64) || !Identity(f[4], 32) ||
                !Identity(f[5], 32) || !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out gridId) || gridId <= 0 ||
                !long.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out controllerId) || controllerId <= 0 ||
                !long.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out coreId) || coreId <= 0 ||
                !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out cameraId) || cameraId <= 0 ||
                !Identity(f[10], 32) || !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out discoveryTick) || discoveryTick < 0 ||
                !Identity(f[12], 64) || !IsToken(f[13]) ||
                !DateTime.TryParseExact(f[14], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out issued) || issued.Kind != DateTimeKind.Utc ||
                !DateTime.TryParseExact(f[15], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expires) || expires.Kind != DateTimeKind.Utc ||
                expires <= issued || expires - issued > MaximumLifetime)
                return false;
            var now = nowUtc.ToUniversalTime();
            if (now < issued || now >= expires) return false;
            capability = new VoidwrightOperationCapability
            {
                RunId = f[0], FixtureId = f[1], OperationId = f[2], ContextSha256 = f[3],
                Challenge = f[4], SessionId = f[5], GridId = gridId, ControllerId = controllerId,
                AgentCoreId = coreId, CameraId = cameraId, DiscoveryRunId = f[10],
                DiscoveryTick = discoveryTick, DiscoveryFingerprint = f[12], Token = f[13],
                IssuedUtc = issued, ExpiresUtc = expires
            };
            return true;
        }

        public bool Matches(DateTime nowUtc, string runId, string fixtureId, string operationId,
            string contextSha256, string challenge, string sessionId, long gridId,
            long controllerId, long agentCoreId, long cameraId, string discoveryRunId,
            long discoveryTick, string discoveryFingerprint, string expectedToken)
        {
            var now = nowUtc.ToUniversalTime();
            return now >= IssuedUtc && now < ExpiresUtc &&
                RunId == runId && FixtureId == fixtureId && OperationId == operationId &&
                ContextSha256 == contextSha256 && Challenge == challenge && SessionId == sessionId &&
                GridId == gridId && ControllerId == controllerId && AgentCoreId == agentCoreId &&
                CameraId == cameraId && DiscoveryRunId == discoveryRunId &&
                DiscoveryTick == discoveryTick && DiscoveryFingerprint == discoveryFingerprint &&
                IsToken(expectedToken) && string.Equals(Token, expectedToken, StringComparison.Ordinal);
        }

        public static bool IsToken(string token)
        {
            return Identity(token, 32);
        }

        private static bool Identity(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
