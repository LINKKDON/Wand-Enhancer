using System;
using System.IO;
using System.Text;
using AsarSharp.Utils;

namespace WandEnhancer.Core.Patching.Shared
{
    /// <summary>
    /// Locates and validates the Electron fuse wire in a packaged Electron binary.
    /// Layout, from @electron/fuses: a 32-byte sentinel, then a version byte and a
    /// wire-length byte, then one state byte per fuse. The embedded ASAR integrity
    /// validation fuse is the fifth byte (index 4).
    /// </summary>
    internal static class ElectronFuseWire
    {
        public const string Sentinel = "dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX";
        public const int SentinelLength = 32;
        public const int HeaderLength = 2;
        public const int AsarIntegrityFuseIndex = 4;
        public const int WireLength = 9;
        public const int StateFromSentinel = SentinelLength + HeaderLength + AsarIntegrityFuseIndex;
        public const int MatchLength = SentinelLength + HeaderLength + WireLength;
        public const byte StateDisabled = 0x30;
        public const byte StateEnabled = 0x31;
        public const byte StateRemoved = 0x72;

        // INHERIT is what @electron/fuses writes for a fuse the app never
        // explicitly configured; it then runs at that fuse's default.
        public const byte StateInherit = 0x90;

        private const int ScanChunkSize = 4 * 1024 * 1024;
        private const byte FuseWireVersion = 1;

        /// <summary>Checks a block read from the sentinel onwards.</summary>
        public static bool BlockLooksValid(byte[] block, int offset)
        {
            if (block == null || offset < 0 || offset + MatchLength > block.Length)
            {
                return false;
            }

            for (int i = 0; i < SentinelLength; i++)
            {
                if (block[offset + i] != (byte)Sentinel[i])
                {
                    return false;
                }
            }

            if (block[offset + SentinelLength] != FuseWireVersion)
            {
                return false;
            }

            int length = block[offset + SentinelLength + 1];
            if (length < AsarIntegrityFuseIndex + 1 || length > WireLength)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                byte state = block[offset + SentinelLength + HeaderLength + i];
                if (state != StateDisabled && state != StateEnabled && state != StateRemoved && state != StateInherit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>RVA of the ASAR integrity fuse state byte, or -1 when there is no fuse block.</summary>
        public static long FindStateRva(string exePath)
        {
            using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long stateOffset = FindStateFileOffset(stream);
                return stateOffset < 0 ? -1 : PeImage.FileOffsetToRva(stream, stateOffset);
            }
        }

        /// <summary>File offset of the ASAR integrity fuse state byte, or -1 when there is no fuse block.</summary>
        public static long FindStateFileOffset(string exePath)
        {
            using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return FindStateFileOffset(stream);
            }
        }

        private static long FindStateFileOffset(Stream stream)
        {
            byte[] sentinel = Encoding.ASCII.GetBytes(Sentinel);
            byte[] chunk = new byte[ScanChunkSize + sentinel.Length - 1];
            long position = 0;

            while (true)
            {
                stream.Position = position;
                int read = stream.ReadFull(chunk, 0, chunk.Length);
                if (read < sentinel.Length)
                {
                    return -1;
                }

                int index = IndexOf(chunk, read, sentinel, 0);
                while (index >= 0)
                {
                    long sentinelPos = position + index;
                    if (TryReadBlock(stream, sentinelPos, out byte[] block) && BlockLooksValid(block, 0))
                    {
                        return sentinelPos + StateFromSentinel;
                    }

                    index = IndexOf(chunk, read, sentinel, index + 1);
                }

                if (read < chunk.Length)
                {
                    return -1;
                }

                position += ScanChunkSize;
            }
        }

        private static bool TryReadBlock(Stream stream, long sentinelPos, out byte[] block)
        {
            block = new byte[MatchLength];
            stream.Position = sentinelPos;
            return stream.ReadFull(block, 0, block.Length) == block.Length;
        }

        private static int IndexOf(byte[] haystack, int haystackLength, byte[] needle, int from)
        {
            for (int i = from; i + needle.Length <= haystackLength; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
