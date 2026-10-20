#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using osu.Framework.Platform;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    internal sealed class SticksReplayStore
    {
        private const int magic = 0x53544B52;
        private const int version = 5;
        private const int version_with_stick_buttons = 4;
        private const int version_with_shoulders = 3;
        private const int version_with_triggers = 2;
        private const int version_without_buttons = 1;
        private const int bytes_per_frame_v1 = sizeof(double) + sizeof(float) * 4;
        private const int bytes_per_frame_v2 = bytes_per_frame_v1 + sizeof(bool) * 2;
        private const int bytes_per_frame_v3 = bytes_per_frame_v2 + sizeof(bool) * 2;
        private const int bytes_per_frame_v4 = bytes_per_frame_v3 + sizeof(bool) * 2;
        private const int maximum_frame_count = 10_000_000;

        private readonly Storage storage;

        public SticksReplayStore(Storage storage)
            : this(storage, "ruleset-data/sticks/replays")
        {
        }

        private SticksReplayStore(Storage storage, string directory)
        {
            this.storage = storage.GetStorageForDirectory(directory);
        }

        /// <summary>
        /// Other players' downloaded recordings. Kept apart from local replays, whose orphan cleanup
        /// deletes any recording without a matching local score.
        /// </summary>
        public static SticksReplayStore Downloads(Storage storage) => new SticksReplayStore(storage, "ruleset-data/sticks/downloads");

        public bool Contains(Guid scoreId) => storage.Exists(filenameFor(scoreId));

        /// <summary>
        /// The raw current-version recording for upload, or null if it is missing, an older version, or longer than allowed.
        /// </summary>
        public byte[]? ReadForUpload(Guid scoreId, double maxDurationMilliseconds)
        {
            try
            {
                string filename = filenameFor(scoreId);
                if (!storage.Exists(filename)) return null;
                using Stream stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                if (stream.Length > maximum_frame_count * (long)bytes_per_frame_v4) return null;
                byte[] raw = new byte[stream.Length];
                stream.ReadExactly(raw);
                return TryGetDuration(raw, out double duration) && duration <= maxDurationMilliseconds ? raw : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Checks the header and frame layout of a raw current-version recording and measures it from first to last frame.
        /// </summary>
        public static bool TryGetDuration(ReadOnlySpan<byte> raw, out double milliseconds)
        {
            milliseconds = 0;
            const int header = sizeof(int) * 2 + sizeof(float);
            if (raw.Length < header + bytes_per_frame_v4 || (raw.Length - header) % bytes_per_frame_v4 != 0
                || BinaryPrimitives.ReadInt32LittleEndian(raw) != magic || BinaryPrimitives.ReadInt32LittleEndian(raw[4..]) != version)
                return false;

            double first = BinaryPrimitives.ReadDoubleLittleEndian(raw[header..]);
            double last = BinaryPrimitives.ReadDoubleLittleEndian(raw[^bytes_per_frame_v4..]);
            if (!double.IsFinite(first) || !double.IsFinite(last) || last < first) return false;
            milliseconds = last - first;
            return true;
        }

        /// <summary>
        /// Stores a raw recording that has already been checked, so playback can restore it by score.
        /// </summary>
        public bool SaveRaw(Guid scoreId, ReadOnlySpan<byte> raw)
        {
            if (!TryGetDuration(raw, out _)) return false;
            try
            {
                using Stream stream = storage.GetStream(filenameFor(scoreId), FileAccess.Write, FileMode.Create);
                stream.Write(raw);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void EnsureLocalIdentity(Score score)
        {
            if (string.IsNullOrEmpty(score.ScoreInfo.Hash))
                score.ScoreInfo.Hash = $"sticks-replay-{score.ScoreInfo.ID:N}";
        }

        public bool Save(Score score, float flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD)
        {
            EnsureLocalIdentity(score);

            if (!isValidThreshold(flickActivationThreshold) || !score.Replay.Frames.OfType<SticksReplayFrame>().Any(isValid))
                return false;

            try
            {
                using Stream stream = storage.GetStream(filenameFor(score.ScoreInfo.ID), FileAccess.Write, FileMode.Create);
                using var writer = new BinaryWriter(stream);

                writer.Write(magic);
                writer.Write(version);
                writer.Write(flickActivationThreshold);

                foreach (ReplayFrame replayFrame in score.Replay.Frames)
                {
                    if (replayFrame is not SticksReplayFrame frame || !isValid(frame))
                        continue;

                    writer.Write(frame.Time);
                    writer.Write(frame.LeftStick.X);
                    writer.Write(frame.LeftStick.Y);
                    writer.Write(frame.RightStick.X);
                    writer.Write(frame.RightStick.Y);
                    writer.Write(frame.LeftTrigger);
                    writer.Write(frame.RightTrigger);
                    writer.Write(frame.LeftShoulder);
                    writer.Write(frame.RightShoulder);
                    writer.Write(frame.LeftStickButton);
                    writer.Write(frame.RightStickButton);
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void DeleteOrphans(Func<Guid, bool> scoreExists)
        {
            foreach (string file in storage.GetFiles(string.Empty, "*.stkr"))
            {
                string name = Path.GetFileNameWithoutExtension(file);

                if (Guid.TryParseExact(name, "N", out Guid scoreId))
                {
                    try
                    {
                        if (scoreExists(scoreId))
                            continue;
                    }
                    catch
                    {
                        // A temporary database failure must never be treated as proof that the
                        // score was deleted. Leave the recording for a later cleanup pass.
                        continue;
                    }
                }

                try
                {
                    storage.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        public bool TryRestore(Score score, bool replaceExisting = false) =>
            (score.Replay.Frames.Count != 0 && !replaceExisting) || TryRestore(score, out _);

        // Load settings and frames together, replacing neither if the file is invalid.
        public bool TryRestore(Score score, out float flickActivationThreshold)
        {
            flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD;

            string filename = filenameFor(score.ScoreInfo.ID);
            if (!storage.Exists(filename))
                return false;

            try
            {
                using Stream stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                using var reader = new BinaryReader(stream);

                if (stream.Length < sizeof(int) * 2 || reader.ReadInt32() != magic)
                    return false;

                int storedVersion = reader.ReadInt32();
                if (storedVersion is not version and not version_with_stick_buttons and not version_with_shoulders and not version_with_triggers and not version_without_buttons)
                    return false;

                float storedThreshold = storedVersion >= version ? reader.ReadSingle() : SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD;
                if (!isValidThreshold(storedThreshold))
                    return false;

                int bytesPerFrame = storedVersion switch
                {
                    version or version_with_stick_buttons => bytes_per_frame_v4,
                    version_with_shoulders => bytes_per_frame_v3,
                    version_with_triggers => bytes_per_frame_v2,
                    _ => bytes_per_frame_v1,
                };

                long payloadLength = stream.Length - stream.Position;
                if (payloadLength < 0 || payloadLength % bytesPerFrame != 0)
                    return false;

                long frameCount = payloadLength / bytesPerFrame;
                if (frameCount <= 0 || frameCount > maximum_frame_count)
                    return false;

                var restored = new ReplayFrame[frameCount];
                double previousTime = double.NegativeInfinity;

                for (int i = 0; i < restored.Length; i++)
                {
                    var frame = new SticksReplayFrame(
                        reader.ReadDouble(),
                        new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        storedVersion >= version_with_triggers && reader.ReadBoolean(),
                        storedVersion >= version_with_triggers && reader.ReadBoolean(),
                        storedVersion >= version_with_shoulders && reader.ReadBoolean(),
                        storedVersion >= version_with_shoulders && reader.ReadBoolean(),
                        storedVersion >= version_with_stick_buttons && reader.ReadBoolean(),
                        storedVersion >= version_with_stick_buttons && reader.ReadBoolean());

                    if (!isValid(frame) || frame.Time < previousTime)
                        return false;

                    previousTime = frame.Time;
                    restored[i] = frame;
                }

                score.Replay.Frames.Clear();

                score.Replay.Frames.AddRange(restored);
                score.Replay.HasReceivedAllFrames = true;
                flickActivationThreshold = storedThreshold;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool isValid(SticksReplayFrame frame) =>
            double.IsFinite(frame.Time)
            && float.IsFinite(frame.LeftStick.X)
            && float.IsFinite(frame.LeftStick.Y)
            && float.IsFinite(frame.RightStick.X)
            && float.IsFinite(frame.RightStick.Y);

        private static bool isValidThreshold(float threshold) => float.IsFinite(threshold)
                                                                && threshold >= SticksInputTracker.MIN_ACTIVATION_THRESHOLD
                                                                && threshold <= SticksInputTracker.MAX_ACTIVATION_THRESHOLD;

        private static string filenameFor(Guid scoreId) => $"{scoreId:N}.stkr";
    }
}
