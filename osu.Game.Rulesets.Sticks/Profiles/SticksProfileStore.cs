#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    internal sealed class SticksProfileStore
    {
        private const uint magic = 0x50544B53;
        private const int version = 1;
        private const int header_size = 12;
        public const int PayloadSize = 166;
        public const int RecordSize = PayloadSize + 16;
        private const string filename = "scores.bin";
        private static readonly ConcurrentDictionary<string, object> locks = new ConcurrentDictionary<string, object>();
        private readonly Storage storage;
        private readonly object sync;

        public SticksProfileStore(Storage root)
        {
            storage = root.GetStorageForDirectory("ruleset-data/sticks/profile");
            sync = locks.GetOrAdd(storage.GetFullPath(filename), _ => new object());
        }

        public SticksProfileScore[] Read()
        {
            lock (sync) return read();
        }

        private SticksProfileScore[] read()
        {
            if (!storage.Exists(filename)) return Array.Empty<SticksProfileScore>();
            using Stream stream = storage.GetStream(filename, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            checkHeader(reader);
            var scores = new Dictionary<Guid, SticksProfileScore>();
            while (stream.Length - stream.Position >= RecordSize)
            {
                byte[] payload = reader.ReadBytes(PayloadSize);
                byte[] checksum = reader.ReadBytes(16);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload).AsSpan(0, 16), checksum)) continue;
                try
                {
                    SticksProfileScore score = Decode(payload);
                    if (score.IsValid) scores[score.ScoreId] = score;
                }
                catch (IOException) { }
            }
            return scores.Values.ToArray();
        }

        public bool Append(SticksProfileScore score)
        {
            if (!score.IsValid) return false;
            lock (sync)
            {
                if (read().Any(s => s.ScoreId == score.ScoreId)) return false;
                using Stream stream = storage.GetStream(filename, FileAccess.ReadWrite, FileMode.OpenOrCreate);
                using var writer = new BinaryWriter(stream);
                if (stream.Length == 0)
                {
                    writer.Write(magic);
                    writer.Write(version);
                    writer.Write(RecordSize);
                }
                else
                {
                    using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                    checkHeader(reader);
                    // Discard only an incomplete final append left by an interrupted write.
                    stream.SetLength(header_size + (stream.Length - header_size) / RecordSize * RecordSize);
                    stream.Position = stream.Length;
                }
                byte[] payload = Encode(score);
                writer.Write(payload);
                writer.Write(SHA256.HashData(payload), 0, 16);
                writer.Flush();
                if (stream is FileStream file) file.Flush(true);
                return true;
            }
        }

        private static void checkHeader(BinaryReader reader)
        {
            if (reader.BaseStream.Length < header_size || reader.ReadUInt32() != magic
                || reader.ReadInt32() != version || reader.ReadInt32() != RecordSize)
                throw new InvalidDataException("The Sticks profile has an unsupported or damaged header; it was not modified.");
        }

        internal static byte[] Encode(SticksProfileScore score)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(score.ScoreId.ToByteArray());
            writer.Write(score.UserId);
            writer.Write(score.BeatmapId);
            writer.Write(score.BeatmapSetId);
            writer.Write(Convert.FromHexString(score.SourceMd5));
            writer.Write(score.PlayedAt);
            writer.Write(score.Generation);
            writer.Write(score.DifficultyVersion);
            writer.Write(score.Mods);
            writer.Write(score.Rate);
            writer.Write(score.PP);
            writer.Write(score.Accuracy);
            writer.Write(score.MaxCombo);
            writer.Write(score.TotalScore);
            writer.Write(score.ActivationThreshold);
            writer.Write(score.Ranked);
            writer.Write(score.MatchesPublishedMap);
            foreach (int count in score.Judgements) writer.Write(count);
            return stream.ToArray();
        }

        internal static SticksProfileScore Decode(byte[] payload)
        {
            using var reader = new BinaryReader(new MemoryStream(payload));
            return new SticksProfileScore
            {
                ScoreId = new Guid(reader.ReadBytes(16)), UserId = reader.ReadInt32(),
                BeatmapId = reader.ReadInt32(), BeatmapSetId = reader.ReadInt32(),
                SourceMd5 = Convert.ToHexString(reader.ReadBytes(16)).ToLowerInvariant(),
                PlayedAt = reader.ReadInt64(), Generation = reader.ReadInt32(), DifficultyVersion = reader.ReadInt32(),
                Mods = reader.ReadUInt32(), Rate = reader.ReadSingle(), PP = reader.ReadDouble(), Accuracy = reader.ReadDouble(),
                MaxCombo = reader.ReadInt32(), TotalScore = reader.ReadInt64(), ActivationThreshold = reader.ReadSingle(),
                Ranked = reader.ReadBoolean(), MatchesPublishedMap = reader.ReadBoolean(),
                Judgements = Enumerable.Range(0, SticksProfileScore.JudgementCount).Select(_ => reader.ReadInt32()).ToArray(),
            };
        }
    }
}
