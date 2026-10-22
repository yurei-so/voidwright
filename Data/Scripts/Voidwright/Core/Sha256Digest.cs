using System;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>
    /// Small SHA-256 implementation for the Space Engineers mod sandbox, which
    /// blocks System.Security.Cryptography APIs used by the host-side verifier.
    /// </summary>
    public static class Sha256Digest
    {
        private static readonly uint[] Constants = new uint[]
        {
            0x428a2f98U,0x71374491U,0xb5c0fbcfU,0xe9b5dba5U,0x3956c25bU,0x59f111f1U,0x923f82a4U,0xab1c5ed5U,
            0xd807aa98U,0x12835b01U,0x243185beU,0x550c7dc3U,0x72be5d74U,0x80deb1feU,0x9bdc06a7U,0xc19bf174U,
            0xe49b69c1U,0xefbe4786U,0x0fc19dc6U,0x240ca1ccU,0x2de92c6fU,0x4a7484aaU,0x5cb0a9dcU,0x76f988daU,
            0x983e5152U,0xa831c66dU,0xb00327c8U,0xbf597fc7U,0xc6e00bf3U,0xd5a79147U,0x06ca6351U,0x14292967U,
            0x27b70a85U,0x2e1b2138U,0x4d2c6dfcU,0x53380d13U,0x650a7354U,0x766a0abbU,0x81c2c92eU,0x92722c85U,
            0xa2bfe8a1U,0xa81a664bU,0xc24b8b70U,0xc76c51a3U,0xd192e819U,0xd6990624U,0xf40e3585U,0x106aa070U,
            0x19a4c116U,0x1e376c08U,0x2748774cU,0x34b0bcb5U,0x391c0cb3U,0x4ed8aa4aU,0x5b9cca4fU,0x682e6ff3U,
            0x748f82eeU,0x78a5636fU,0x84c87814U,0x8cc70208U,0x90befffaU,0xa4506cebU,0xbef9a3f7U,0xc67178f2U
        };

        public static string ComputeHex(byte[] input)
        {
            if (input == null) throw new ArgumentNullException("input");
            var paddedLength = ((input.Length + 9 + 63) / 64) * 64;
            var padded = new byte[paddedLength];
            Array.Copy(input, padded, input.Length);
            padded[input.Length] = 0x80;
            var bitLength = (ulong)input.Length * 8UL;
            for (var i = 0; i < 8; i++) padded[paddedLength - 1 - i] = (byte)(bitLength >> (i * 8));

            var state = new uint[] { 0x6a09e667U,0xbb67ae85U,0x3c6ef372U,0xa54ff53aU,
                                     0x510e527fU,0x9b05688cU,0x1f83d9abU,0x5be0cd19U };
            var words = new uint[64];
            for (var offset = 0; offset < padded.Length; offset += 64)
            {
                for (var i = 0; i < 16; i++)
                {
                    var index = offset + i * 4;
                    words[i] = ((uint)padded[index] << 24) | ((uint)padded[index + 1] << 16) |
                               ((uint)padded[index + 2] << 8) | padded[index + 3];
                }
                for (var i = 16; i < 64; i++)
                {
                    var x = words[i - 15]; var y = words[i - 2];
                    var small0 = RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
                    var small1 = RotateRight(y, 17) ^ RotateRight(y, 19) ^ (y >> 10);
                    words[i] = unchecked(words[i - 16] + small0 + words[i - 7] + small1);
                }

                var a = state[0]; var b = state[1]; var c = state[2]; var d = state[3];
                var e = state[4]; var f = state[5]; var g = state[6]; var h = state[7];
                for (var i = 0; i < 64; i++)
                {
                    var big1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                    var choice = (e & f) ^ (~e & g);
                    var temp1 = unchecked(h + big1 + choice + Constants[i] + words[i]);
                    var big0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                    var majority = (a & b) ^ (a & c) ^ (b & c);
                    var temp2 = unchecked(big0 + majority);
                    h = g; g = f; f = e; e = unchecked(d + temp1);
                    d = c; c = b; b = a; a = unchecked(temp1 + temp2);
                }
                state[0] = unchecked(state[0] + a); state[1] = unchecked(state[1] + b);
                state[2] = unchecked(state[2] + c); state[3] = unchecked(state[3] + d);
                state[4] = unchecked(state[4] + e); state[5] = unchecked(state[5] + f);
                state[6] = unchecked(state[6] + g); state[7] = unchecked(state[7] + h);
            }

            var output = new StringBuilder(64);
            for (var i = 0; i < state.Length; i++) output.Append(state[i].ToString("x8"));
            return output.ToString();
        }

        public static string ComputeHex(string value)
        {
            return ComputeHex(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static uint RotateRight(uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }
    }
}
