using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCounterpointRhythmTest
    {
        [Test]
        public void IsolatedUnaccentedSliderDoesNotEstablishAManualPulse()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 2400, RepeatCount = 3 };
            Beatmap<HitObject> beatmap = map(600, slider);

            Assert.That(responses(beatmap, slider), Is.Empty);
        }

        [Test]
        public void ShortReleaseAgainstSlowerHeadCadenceIsUnsupported()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 93.75 };
            Beatmap<HitObject> beatmap = map(375, slider, 250, 625, 1375, 1750, 2125);

            Assert.That(evaluate(beatmap, slider), Is.Empty);
        }

        [Test]
        public void RepeatedLatePickupsDoNotEstablishAContinuousQuarterBeatPulse()
        {
            var slider = new SourceSlider { StartTime = 3000, Duration = 93.75 };
            Beatmap<HitObject> beatmap = map(375, slider,
                1500, 1781.25, 1875, 2250, 2531.25, 2625, 3281.25, 3375, 3750);

            Assert.That(evaluate(beatmap, slider), Is.Empty,
                "Recurring .75-to-0 pickups support the 375 ms pulse, not every 93.75 ms slot in between.");
        }

        [Test]
        public void AnActualFastManualPulseCanStillSupportItsCheckpoint()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 93.75 };
            Beatmap<HitObject> beatmap = map(375, slider, 718.75, 812.5, 906.25, 1187.5, 1281.25, 1375);

            Assert.That(evaluate(beatmap, slider).Single().Checkpoint.Time, Is.EqualTo(1093.75),
                "The evidence requirement concerns recurring attacks, not a blanket ban on a short interval.");
        }

        [Test]
        public void ShortPickupSliderCanCloseOnAnExistingSlowerPulse()
        {
            var slider = new SourceSlider { StartTime = 2000, Duration = 93.75 };
            Beatmap<HitObject> beatmap = map(375, slider, 968.75, 1343.75, 1718.75);

            Assert.That(evaluate(beatmap, slider).Single().Checkpoint.Time, Is.EqualTo(2093.75),
                "A short pickup's release can continue the demonstrated slower pulse even though its own head is between pulse attacks.");
        }

        [Test]
        public void RepeatingSliderUsesSurroundingManualCadenceInsteadOfFirstRepeat()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 450, RepeatCount = 2 };
            Beatmap<HitObject> beatmap = map(600, slider, -200, 400, 1600, 1900, 2200, 2500, 2800);

            Assert.That(responses(beatmap, slider).Single().Checkpoints.Select(point => point.Time), Is.EqualTo(new[] { 1300.0 }),
                "The middle repeat continues the 300 ms head rhythm; the first repeat and tail are between those attacks.");
        }

        [TestCase(0)]
        [TestCase(125)]
        [TestCase(347.25)]
        public void PulsePhaseComesFromHeadsRatherThanIntegerBeats(double shift)
        {
            var slider = new SourceSlider { StartTime = 1000 + shift, Duration = 450, RepeatCount = 2 };
            Beatmap<HitObject> beatmap = map(600, slider, new[] { -200.0, 400, 1600, 1900, 2200, 2500 }.Select(time => time + shift).ToArray());

            Assert.That(responses(beatmap, slider).Single().Checkpoints.Single().Time, Is.EqualTo(1300 + shift));
        }

        [Test]
        public void TailOnRecurringManualPulseRemainsAvailable()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 300 };
            Beatmap<HitObject> beatmap = map(600, slider, 400, 700, 1600, 1900);

            Assert.That(evaluate(beatmap, slider).Single().Checkpoint.Type, Is.EqualTo(SliderEventType.Tail));
        }

        [TestCase(HitSampleInfo.HIT_CLAP)]
        [TestCase(HitSampleInfo.HIT_FINISH)]
        public void ExplicitNodeAccentCanSupportAnOffPulseRelease(string sample)
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 93.75 };
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) });
            slider.NodeSamples.Add(new[] { new HitSampleInfo(sample) });
            Beatmap<HitObject> beatmap = map(375, slider, 250, 625, 1375, 1750);
            SticksCounterpointRhythm.SupportedCheckpoint checkpoint = evaluate(beatmap, slider).Single();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoint.Checkpoint.Time, Is.EqualTo(1093.75));
                Assert.That(checkpoint.ExplicitAccent, Is.True);
                Assert.That(checkpoint.Evidence, Is.InRange(4, 10));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeadAccentDoesNotLeakToAnUnaccentedTail(bool includeNodes)
        {
            var slider = new SourceSlider
            {
                StartTime = 1000,
                Duration = 93.75,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP) },
            };
            if (includeNodes)
            {
                slider.NodeSamples.Add(slider.Samples);
                slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) });
            }
            Beatmap<HitObject> beatmap = map(375, slider, 250, 625, 1375, 1750);

            Assert.That(evaluate(beatmap, slider), Is.Empty);
        }

        [Test]
        public void SpecificRepeatNodeRetainsItsAccentWithoutRewardingOtherNodes()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 900, RepeatCount = 2 };
            foreach (string sample in new[] { HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_FINISH, HitSampleInfo.HIT_NORMAL })
                slider.NodeSamples.Add(new[] { new HitSampleInfo(sample) });
            Beatmap<HitObject> beatmap = map(600, slider);

            Assert.That(evaluate(beatmap, slider).Select(point => point.Checkpoint.Time), Is.EqualTo(new[] { 1600.0 }));
        }

        [Test]
        public void OrdinaryTicksAndTailHaveEqualSupportWhenBothMatchThePulse()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 900 };
            Beatmap<HitObject> beatmap = map(600, slider, 100, 400, 700, 2200, 2500, 2800);
            beatmap.Difficulty.SliderTickRate = 2;
            SticksCounterpointRhythm.SupportedCheckpoint[] supported = evaluate(beatmap, slider);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(supported.Select(point => point.Checkpoint.Time), Is.EqualTo(new[] { 1300.0, 1600, 1900 }));
                Assert.That(supported.Select(point => point.Evidence).Distinct().Count(), Is.EqualTo(1), "Being a tail is not independent musical evidence.");
            });
        }

        [Test]
        public void StrongerLaterCheckpointWinsASingleHeadResponse()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 900 };
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) });
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP) });
            Beatmap<HitObject> beatmap = map(600, slider, 100, 400, 700, 2200, 2500, 2800);
            beatmap.Difficulty.SliderTickRate = 2;

            Assert.That(responses(beatmap, slider, maximumHeads: 1).Single().Checkpoints.Single().Time, Is.EqualTo(1900));
        }

        [Test]
        public void ResponsesRespectRecoveryWithoutInventingOrMovingCheckpoints()
        {
            var slider = new SourceSlider { StartTime = 1000.25, Duration = 1200 };
            Beatmap<HitObject> beatmap = map(600, slider, 100.25, 400.25, 700.25, 2500.25, 2800.25);
            beatmap.Difficulty.SliderTickRate = 2;
            SliderEventDescriptor[] actual = checkpoints(beatmap, slider);
            SticksCounterpointRhythm.Response[] selected = responses(beatmap, slider, minimumInterval: 500);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(selected.Max(response => response.Checkpoints.Length), Is.EqualTo(2));
                Assert.That(selected.SelectMany(response => response.Checkpoints).Select(point => point.Time).Distinct(), Is.SubsetOf(actual.Select(point => point.Time)));
                Assert.That(selected.SelectMany(response => response.Checkpoints.Zip(response.Checkpoints.Skip(1), (a, b) => b.Time - a.Time)), Has.All.GreaterThanOrEqualTo(500));
            });
        }

        [Test]
        public void NoHeadsAreSelectedWhenCallerAllowsNone()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 300 };
            Beatmap<HitObject> beatmap = map(600, slider, 400, 700, 1600, 1900);

            Assert.That(responses(beatmap, slider, maximumHeads: 0), Is.Empty);
        }

        [Test]
        public void NewTimingSectionCannotSupplyAnOtherwiseMissingPulse()
        {
            var slider = new SourceSlider { StartTime = 0, Duration = 300 };
            Beatmap<HitObject> beatmap = map(600, slider, 600, 900, 1200, 1500);
            beatmap.ControlPointInfo.Add(400, new TimingControlPoint { BeatLength = 600 });

            Assert.That(evaluate(beatmap, slider), Is.Empty);
        }

        [Test]
        public void RestPreventsBorrowingAPulseFromAnotherPhrase()
        {
            var slider = new SourceSlider { StartTime = 0, Duration = 300 };
            Beatmap<HitObject> beatmap = map(600, slider, 2400, 2700, 3000, 3300);

            Assert.That(evaluate(beatmap, slider), Is.Empty);
        }

        [Test]
        public void SustainedDurationDoesNotCountAsARest()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 2400 };
            Beatmap<HitObject> beatmap = map(600, slider, 400, 700, 3700, 4000, 4300);
            beatmap.Difficulty.SliderTickRate = 1;

            Assert.That(evaluate(beatmap, slider).Select(point => point.Checkpoint.Time), Does.Contain(3400));
        }

        [Test]
        public void IrregularHeadIntervalsDoNotInventARecurringPulse()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 100 };
            Beatmap<HitObject> beatmap = map(600, slider, 400, 550, 780, 1410, 1930);

            Assert.That(evaluate(beatmap, slider), Is.Empty);
        }

        [TestCase(173)]
        [TestCase(221)]
        public void MillisecondRoundedHeadTimesStillSupportTheirPulse(int bpm)
        {
            double beat = 60000.0 / bpm;
            var slider = new SourceSlider { StartTime = 1000, Duration = beat / 2 };
            Beatmap<HitObject> beatmap = map(beat, slider, Enumerable.Range(-4, 9).Where(i => i != 0 && i != 1)
                .Select(i => Math.Round(1000 + i * beat / 2)).ToArray());

            Assert.That(evaluate(beatmap, slider).Single().Checkpoint.Time, Is.EqualTo(slider.EndTime));
        }

        [Test]
        public void CancellationIsObservedForEvaluationAndSelection()
        {
            var slider = new SourceSlider { StartTime = 1000, Duration = 300 };
            Beatmap<HitObject> beatmap = map(600, slider, 400, 700, 1600, 1900);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            NUnitCompatibility.Multiple(() =>
            {
                NUnitCompatibility.Throws<OperationCanceledException>(() => SticksCounterpointRhythm.Evaluate(beatmap.HitObjects.ToArray(), beatmap.HitObjects.IndexOf(slider),
                    beatmap, checkpoints(beatmap, slider), cancellation.Token));
                NUnitCompatibility.Throws<OperationCanceledException>(() => SticksCounterpointRhythm.Select(Array.Empty<SticksCounterpointRhythm.SupportedCheckpoint>(), 200, 3, cancellation.Token));
            });
        }

        private static Beatmap<HitObject> map(double beat, SourceSlider slider, params double[] heads)
        {
            var beatmap = new Beatmap<HitObject> { BeatmapVersion = 14 };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beat });
            beatmap.HitObjects.AddRange(heads.Select(time => new SourceCircle { StartTime = time }));
            beatmap.HitObjects.Add(slider);
            beatmap.HitObjects = beatmap.HitObjects.OrderBy(note => note.StartTime).ToList();
            return beatmap;
        }

        private static SliderEventDescriptor[] checkpoints(Beatmap<HitObject> beatmap, SourceSlider slider)
            => SticksCounterpointCheckpoints.Generate(slider, beatmap).ToArray();

        private static SticksCounterpointRhythm.SupportedCheckpoint[] evaluate(Beatmap<HitObject> beatmap, SourceSlider slider)
            => SticksCounterpointRhythm.Evaluate(beatmap.HitObjects.ToArray(), beatmap.HitObjects.IndexOf(slider), beatmap, checkpoints(beatmap, slider));

        private static SticksCounterpointRhythm.Response[] responses(Beatmap<HitObject> beatmap, SourceSlider slider, double minimumInterval = 200, int maximumHeads = 3)
            => SticksCounterpointRhythm.Select(beatmap.HitObjects.ToArray(), beatmap.HitObjects.IndexOf(slider), beatmap, checkpoints(beatmap, slider), minimumInterval, maximumHeads);

        private sealed class SourceCircle : HitObject
        {
        }

        private sealed class SourceSlider : HitObject, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
