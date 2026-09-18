using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCounterpointCheckpointsTest
    {
        [Test]
        public void FractionalRepeatSpanMirrorsTickPosition()
        {
            var source = new SourceSlider { Duration = 1500, RepeatCount = 1 };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, map()).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Where(point => point.Type == SliderEventType.Tick).Select(point => point.Time), Is.EqualTo(new[] { 500.0, 1000.0 }));
                Assert.That(checkpoints.Select(point => point.Time), Is.Ordered);
                Assert.That(checkpoints.Where(point => point.Type == SliderEventType.Repeat).Select(point => point.Time), Is.EqualTo(new[] { 750.0 }));
                Assert.That(checkpoints.Single(point => point.Type == SliderEventType.Tail).Time, Is.EqualTo(1500));
                Assert.That(checkpoints.Select(point => point.Time), Does.Not.Contain(1250), "Repeating the first span's tick offset would invent a different rhythm.");
            });
        }

        [Test]
        public void DisabledTicksKeepRepeatsAndTheTrueTail()
        {
            var source = new SourceSlider { StartTime = 100, Duration = 2250, RepeatCount = 2, GenerateTicks = false };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, map()).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Select(point => point.Type), Is.EqualTo(new[] { SliderEventType.Repeat, SliderEventType.Repeat, SliderEventType.Tail }));
                Assert.That(checkpoints.Select(point => point.Time), Is.EqualTo(new[] { 850.0, 1600.0, 2350.0 }));
            });
        }

        [TestCase(7, true, new[] { 250.0, 500.0, 1000.0, 1250.0 })]
        [TestCase(8, true, new[] { 500.0, 1000.0 })]
        [TestCase(7, false, new[] { 250.0, 500.0, 1000.0, 1250.0 })]
        [TestCase(8, false, new[] { 500.0, 1000.0 })]
        public void HistoricalTickDensityUsesSvOnlyBeforeVersionEight(int version, bool legacyTiming, double[] expected)
        {
            Beatmap<HitObject> beatmap = map();
            beatmap.BeatmapVersion = version;
            if (legacyTiming)
            {
                beatmap.ControlPointInfo = new LegacyControlPointInfo();
                beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                beatmap.ControlPointInfo.Add(0, new DifficultyControlPoint { SliderVelocity = 2 });
            }
            var source = new SourceSlider
            {
                Duration = 1500,
                RepeatCount = 1,
                // The legacy timing value is authoritative even if a source multiplier
                // differs; non-legacy in-memory sources use their own multiplier.
                SliderVelocityMultiplier = legacyTiming ? 4 : 2,
            };
            Assert.That(SticksCounterpointCheckpoints.Generate(source, beatmap)
                .Where(point => point.Type == SliderEventType.Tick).Select(point => point.Time), Is.EqualTo(expected));
        }

        [Test]
        public void EndCheckpointUsesFullDurationWithoutLegacyTailLeniency()
        {
            var source = new SourceSlider { StartTime = 1000, Duration = 750 };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, map()).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Select(point => point.Type), Does.Not.Contain(SliderEventType.Head).And.Not.Contain(SliderEventType.LegacyLastTick));
                Assert.That(checkpoints.Select(point => point.Time), Does.Not.Contain(1714));
                Assert.That(checkpoints.Last().Type, Is.EqualTo(SliderEventType.Tail));
                Assert.That(checkpoints.Last().Time, Is.EqualTo(1750));
            });
        }

        [Test]
        public void ExcessiveTickDensityDoesNotMoveRepeatsOrTail()
        {
            Beatmap<HitObject> beatmap = map();
            beatmap.Difficulty.SliderTickRate = 64;
            var source = new SourceSlider { Duration = 1500, RepeatCount = 1 };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, beatmap).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Select(point => point.Type), Is.EqualTo(new[] { SliderEventType.Repeat, SliderEventType.Tail }));
                Assert.That(checkpoints.Select(point => point.Time), Is.EqualTo(new[] { 750.0, 1500.0 }));
            });
        }

        [Test]
        public void TickLimitIncludesAllSpans()
        {
            var source = new SourceSlider { Duration = 32000, RepeatCount = 15 };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, map()).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Count(point => point.Type == SliderEventType.Tick), Is.Zero, "Three ticks per span exceeds the total bound despite a small per-span count.");
                Assert.That(checkpoints.Count(point => point.Type == SliderEventType.Repeat), Is.EqualTo(15));
                Assert.That(checkpoints.Last().Time, Is.EqualTo(32000));
            });
        }

        [Test]
        public void ExactlyThirtyTwoTicksRemainAvailable()
        {
            var source = new SourceSlider { Duration = 20000, RepeatCount = 15 };
            SliderEventDescriptor[] checkpoints = SticksCounterpointCheckpoints.Generate(source, map()).ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(checkpoints.Count(point => point.Type == SliderEventType.Tick), Is.EqualTo(32));
                Assert.That(checkpoints.Count(point => point.Type == SliderEventType.Repeat), Is.EqualTo(15));
                Assert.That(checkpoints.Last().Time, Is.EqualTo(20000));
            });
        }

        [Test]
        public void MoreThanSixteenSpansAreRejectedWithoutRescalingTheirPeriod()
        {
            var source = new SourceSlider { Duration = 17000, RepeatCount = 16 };
            Assert.That(SticksCounterpointCheckpoints.Generate(source, map()), Is.Empty);
        }

        [Test]
        public void CancellationIsCheckedBeforeAnyCheckpoint()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            NUnitCompatibility.Throws<OperationCanceledException>(() => SticksCounterpointCheckpoints.Generate(
                new SourceSlider { Duration = 1500, RepeatCount = 1 }, map(), cancellation.Token).ToArray());
        }

        [Test]
        public void CancellationIsCheckedDuringEnumeration()
        {
            using var cancellation = new CancellationTokenSource();
            using IEnumerator<SliderEventDescriptor> checkpoints = SticksCounterpointCheckpoints.Generate(
                new SourceSlider { Duration = 1500, RepeatCount = 1 }, map(), cancellation.Token).GetEnumerator();
            Assert.That(checkpoints.MoveNext(), Is.True);
            cancellation.Cancel();
            NUnitCompatibility.Throws<OperationCanceledException>(() => checkpoints.MoveNext());
        }

        private static Beatmap<HitObject> map()
        {
            var beatmap = new Beatmap<HitObject> { BeatmapVersion = 14 };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.Difficulty.SliderTickRate = 1;
            return beatmap;
        }

        private sealed class SourceSlider : HitObject, IHasDuration, IHasRepeats, IHasGenerateTicks, IHasSliderVelocity
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
            public bool GenerateTicks { get; set; } = true;
            public BindableNumber<double> SliderVelocityMultiplierBindable { get; } = new BindableDouble(1);
            public double SliderVelocityMultiplier
            {
                get => SliderVelocityMultiplierBindable.Value;
                set => SliderVelocityMultiplierBindable.Value = value;
            }
        }
    }
}
