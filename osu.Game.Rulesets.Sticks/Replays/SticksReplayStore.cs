#nullable enable

using System;
using System.IO;
using System.Linq;
using osu.Framework.Platform;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    internal sealed class SticksReplayStore
    {
        private const int magic = 0x53544B52;
        private const int version = 3;
        private const int version_with_triggers = 2;
        private const int version_without_buttons = 1;
        private const int bytes_per_frame_v1 = sizeof(double) + sizeof(float) * 4;
        private const int bytes_per_frame_v2 = bytes_per_frame_v1 + sizeof(bool) * 2;
        private const int bytes_per_frame_v3 = bytes_per_frame_v2 + sizeof(bool) * 2;
        private const int maximum_frame_count = 10_000_000;

        private readonly Storage storage;

        public SticksReplayStore(Storage storage)
        {
            this.storage = storage.GetStorageForDirectory("ruleset-data/sticks/replays");
        }

        public static void EnsureLocalIdentity(Score score)
        {
            if (string.IsNullOrEmpty(score.ScoreInfo.Hash))
                score.ScoreInfo.Hash = $"sticks-replay-{score.ScoreInfo.ID:N}";
        }

        public bool Save(Score score)
        {
            EnsureLocalIdentity(score);

            if (!score.Replay.Frames.OfType<SticksReplayFrame>().Any(isValid))
                return false;

            try
            {
                using Stream stream = storage.GetStream(filenameFor(score.ScoreInfo.ID), FileAccess.Write, FileMode.Create);
                using var writer = new BinaryWriter(stream);

                writer.Write(magic);
                writer.Write(version);

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

        public bool TryRestore(Score score, bool replaceExisting = false)
        {
            if (score.Replay.Frames.Count != 0 && !replaceExisting)
                return true;

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
                if (storedVersion is not version and not version_with_triggers and not version_without_buttons)
                    return false;

                int bytesPerFrame = storedVersion switch
                {
                    version => bytes_per_frame_v3,
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
                        storedVersion >= version && reader.ReadBoolean(),
                        storedVersion >= version && reader.ReadBoolean());

                    if (!isValid(frame) || frame.Time < previousTime)
                        return false;

                    previousTime = frame.Time;
                    restored[i] = frame;
                }

                if (replaceExisting)
                    score.Replay.Frames.Clear();

                score.Replay.Frames.AddRange(restored);
                score.Replay.HasReceivedAllFrames = true;
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

        private static string filenameFor(Guid scoreId) => $"{scoreId:N}.stkr";
    }
}
