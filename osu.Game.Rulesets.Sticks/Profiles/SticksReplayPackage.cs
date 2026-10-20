#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using osu.Game.Rulesets.Sticks.Replays;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    /// <summary>
    /// Reads and writes the leaderboard's replay formats. Uploads are gzip(.stkr v5); downloads are the portable
    /// ZKR1 container: ASCII "ZKR1" | uint32 LE metadata length | UTF-8 JSON metadata | gzip(.stkr v5).
    /// </summary>
    internal static class SticksReplayPackage
    {
        public const int MaxMetadataBytes = 16 * 1024;

        // Enforced even if the service advertises more.
        public const int LocalMaxRawBytes = 32 * 1024 * 1024;

        private static readonly JsonSerializerOptions json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static byte[] Compress(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
                gzip.Write(raw);
            return output.ToArray();
        }

        /// <summary>
        /// Validates a downloaded container against the play it was requested for and returns the raw recording.
        /// </summary>
        public static byte[] Decode(ReadOnlySpan<byte> package, Guid scoreId, string sourceMd5, int maxRawBytes)
        {
            if (package.Length < 8 || !package[..4].SequenceEqual("ZKR1"u8))
                throw new InvalidDataException("This is not a Sticks replay download");

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(package[4..]);
            if (length == 0 || length > MaxMetadataBytes || 8 + length >= package.Length)
                throw new InvalidDataException("The replay download is damaged");

            Metadata metadata;
            try { metadata = JsonSerializer.Deserialize<Metadata>(package.Slice(8, (int)length), json) ?? throw new InvalidDataException(); }
            catch (JsonException) { throw new InvalidDataException("The replay download is damaged"); }

            if (metadata.FormatVersion != 1 || metadata.Ruleset != "sticks" || metadata.Recording is not { Format: "stkr", Version: 5, Encoding: "gzip" })
                throw new InvalidDataException("This replay uses a format this version of Sticks can't read");
            if (metadata.Score == null || metadata.Score.ScoreId != scoreId.ToString("N")
                || !string.Equals(metadata.Score.SourceMd5, sourceMd5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The replay download doesn't match this play");
            // Playback reconverts the beatmap, so a different conversion generation would replay against different notes.
            if (metadata.Score.Generation != SticksProfileScore.CurrentGeneration)
                throw new InvalidDataException("This replay was recorded on a different version of Sticks");

            ReadOnlySpan<byte> compressed = package[(8 + (int)length)..];
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(compressed)), metadata.Recording.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The replay download is damaged");

            byte[] raw = inflate(compressed, Math.Min(maxRawBytes, LocalMaxRawBytes));
            if (!SticksReplayStore.TryGetDuration(raw, out _))
                throw new InvalidDataException("This replay uses a format this version of Sticks can't read");
            return raw;
        }

        private static byte[] inflate(ReadOnlySpan<byte> compressed, int limit)
        {
            try
            {
                using var gzip = new GZipStream(new MemoryStream(compressed.ToArray()), CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(buffer)) > 0)
                {
                    // Bounded while streaming; the gzip size field is not trusted.
                    if (output.Length + read > limit) throw new InvalidDataException("The replay is larger than allowed");
                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
            catch (InvalidDataException) { throw; }
            catch (IOException) { throw new InvalidDataException("The replay download is damaged"); }
        }

        private sealed record Metadata(int FormatVersion, string? Ruleset, Summary? Score, Recording? Recording);
        private sealed record Summary(string? ScoreId, string? SourceMd5, int Generation);
        private sealed record Recording(string? Format, int Version, string? Encoding, string? Sha256);
    }
}
