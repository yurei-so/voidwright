using System;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>Small authenticated client/server consent protocol; contains no player ID.</summary>
    public static class DrivingLabConsentWire
    {
        public const ushort MessageId = 0x6D3A;
        public const byte StartRequest = 1;
        public const byte StopRequest = 2;
        public const byte Result = 128;
        public const int MaximumResponseBytes = 256;

        public static byte[] CreateStartRequest()
        {
            return new[] { StartRequest };
        }

        public static byte[] CreateStopRequest()
        {
            return new[] { StopRequest };
        }

        public static bool TryReadRequest(byte[] payload, out byte operation)
        {
            operation = 0;
            if (payload == null || payload.Length != 1 ||
                (payload[0] != StartRequest && payload[0] != StopRequest)) return false;
            operation = payload[0];
            return true;
        }

        public static byte[] CreateResult(bool accepted, string message)
        {
            var text = Encoding.UTF8.GetBytes(message ?? string.Empty);
            if (text.Length > MaximumResponseBytes) Array.Resize(ref text, MaximumResponseBytes);
            var payload = new byte[text.Length + 2];
            payload[0] = Result;
            payload[1] = accepted ? (byte)1 : (byte)0;
            Buffer.BlockCopy(text, 0, payload, 2, text.Length);
            return payload;
        }

        public static bool TryReadResult(byte[] payload, out bool accepted, out string message)
        {
            accepted = false;
            message = string.Empty;
            if (payload == null || payload.Length < 2 || payload.Length > MaximumResponseBytes + 2 ||
                payload[0] != Result || payload[1] > 1) return false;
            accepted = payload[1] == 1;
            message = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
            return true;
        }
    }
}
