// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksEditorCarrierBeatmapTest
    {
        [Test]
        public void TestManagerBeatmapInfoAssignmentDoesNotReplaceCarrierRuleset()
        {
            var sticksRuleset = new SticksRuleset();
            var source = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(sticksRuleset.RulesetInfo)
                {
                    DifficultyName = "Authored Sticks",
                },
            };

            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new SticksFlick { StartTime = 1000, Side = StickSide.Left, Angle = 45 });
            source.HitObjects.Add(new SticksHold { StartTime = 1500, Duration = 750, Side = StickSide.Right, Angle = 225 });
            source.HitObjects.Add(new SticksSlider { StartTime = 2500, Duration = 1000, Side = StickSide.Left, Angle = 90, ArcAngle = 180, RepeatCount = 1 });

            var standardRuleset = new RulesetInfo
            {
                OnlineID = 0,
                ShortName = "osu",
                Name = "osu!",
            };

            IBeatmap carrier = SticksEditorCarrierBeatmap.Create(source, standardRuleset);

            // BeatmapManager.Save() performs this assignment immediately before encoding.
            carrier.BeatmapInfo = source.BeatmapInfo;

            Assert.Multiple(() =>
            {
                Assert.That(source.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("sticks"));
                Assert.That(source.BeatmapInfo.Ruleset.OnlineID, Is.EqualTo(-1));
                Assert.That(carrier.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("osu"));
                Assert.That(carrier.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
            });

            using var writer = new StringWriter();
            new LegacyBeatmapEncoder(carrier, null, null).Encode(writer);
            string encoded = writer.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(encoded, Does.Contain("Mode: 0"));
                Assert.That(encoded, Does.Contain("sticks-v1~f~l~45.wav"));
                Assert.That(encoded, Does.Contain("sticks-v1~h~r~225~750.wav"));
                Assert.That(encoded, Does.Contain("sticks-v1~s~l~90~1000~180~1.wav"));
            });

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(encoded));
            using var reader = new LineBufferedReader(stream);
            IBeatmap decodedCarrier = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
            SticksHitObject[] roundTripped = new SticksBeatmapConverter(decodedCarrier, sticksRuleset)
                                                   .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(decodedCarrier.HitObjects[0], Is.Not.InstanceOf<IHasDuration>());
                Assert.That(((IHasDuration)decodedCarrier.HitObjects[1]).EndTime, Is.EqualTo(2250));
                Assert.That(((IHasDuration)decodedCarrier.HitObjects[2]).EndTime, Is.EqualTo(3500));
                Assert.That(roundTripped, Has.Length.EqualTo(3));
                Assert.That(roundTripped[0], Is.TypeOf<SticksFlick>());
                Assert.That(roundTripped[1], Is.TypeOf<SticksHold>());
                Assert.That(roundTripped[2], Is.TypeOf<SticksSlider>());
                Assert.That(((SticksSlider)roundTripped[2]).RepeatCount, Is.EqualTo(1));
            });
        }
    }
}
