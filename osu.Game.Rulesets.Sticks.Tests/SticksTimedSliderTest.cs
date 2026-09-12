using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksTimedSliderTest
    {
        [Test]
        public void TestUnequalSpeedsKeepAnglesAtSourceTimes()
        {
            SticksSlider slider = create(new[] { 30f, 60f }, new[] { 200d, 800d });

            Assert.Multiple(() =>
            {
                Assert.That(slider.HasTimedSegments, Is.True);
                Assert.That(slider.SegmentDurationWeights, Is.EqualTo(new[] { 0.2d, 0.8d }));
                Assert.That(slider.SegmentDurationAt(0), Is.EqualTo(200));
                Assert.That(slider.SegmentDurationAt(1), Is.EqualTo(800));
                Assert.That(slider.AngleAt(900), Is.EqualTo(10));
                Assert.That(slider.AngleAt(1100), Is.EqualTo(25));
                Assert.That(slider.AngleAt(1200), Is.EqualTo(40));
                Assert.That(slider.AngleAt(1600), Is.EqualTo(70));
                Assert.That(slider.AngleAt(2200), Is.EqualTo(100));
            });
        }

        [Test]
        public void TestDwellsAndDirectionChangesKeepSourceCheckpoints()
        {
            SticksSlider slider = create(new[] { 10f, 20f, 0f, -40f, 20f }, new[] { 200d, 100d, 200d, 100d, 400d });
            for (int i = 0; i <= slider.SegmentCount; i++)
                slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 40 + i) });

            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 2000 });
            slider.ApplyDefaults(controlPoints, new BeatmapDifficulty { SliderTickRate = 1 });

            SticksHitObject[] anchors = slider.NestedHitObjects.OfType<SticksHitObject>()
                                              .Where(obj => obj is SticksSliderTick or SticksSliderRepeat)
                                              .OrderBy(obj => obj.StartTime)
                                              .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(slider.AngleAt(1400), Is.EqualTo(40));
                Assert.That(slider.AngleAt(1550), Is.EqualTo(20));
                Assert.That(anchors.Select(obj => obj.StartTime), Is.EqualTo(new[] { 1200d, 1300d, 1500d, 1600d }));
                Assert.That(anchors.Select(obj => obj.Angle), Is.EqualTo(new[] { 20f, 40f, 40f, 0f }));
                Assert.That(anchors.Select(obj => obj.GetType()), Is.EqualTo(new[]
                {
                    typeof(SticksSliderTick), typeof(SticksSliderTick), typeof(SticksSliderRepeat), typeof(SticksSliderRepeat),
                }));
                Assert.That(anchors.Select(obj => obj.Samples.Single().Volume), Is.EqualTo(new[] { 41, 42, 43, 44 }));
                Assert.That(anchors.OfType<SticksSliderRepeat>().Select(obj => obj.DirectionAfter), Is.EqualTo(new[] { -1, 1 }));
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderTail>().Single().StartTime, Is.EqualTo(2000));
            });
        }

        [Test]
        public void TestReversalPreviewSkipsSpeedChangesAndDwellEntries()
        {
            SticksSlider slider = create(new[] { 10f, 20f, 0f, -40f, 20f }, new[] { 200d, 100d, 200d, 100d, 400d });

            Assert.Multiple(() =>
            {
                Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentEndsWithReversal),
                    Is.EqualTo(new[] { false, false, true, true, false }));
                Assert.That(slider.UpcomingSegmentIndexAt(900), Is.EqualTo(-1));
                Assert.That(slider.UpcomingSegmentIndexAt(1100), Is.EqualTo(3));
                Assert.That(slider.UpcomingSegmentIndexAt(1400), Is.EqualTo(3));
                Assert.That(slider.UpcomingSegmentIndexAt(1550), Is.EqualTo(4));
                Assert.That(slider.UpcomingSegmentIndexAt(1700), Is.EqualTo(-1));
            });
        }

        [Test]
        public void TestLeadingDwellDoesNotCreateReversal()
        {
            SticksSlider slider = create(new[] { 0f, -30f, 0f, -20f }, new[] { 100d, 400d, 200d, 300d });

            Assert.Multiple(() =>
            {
                Assert.That(slider.InitialDirection, Is.EqualTo(-1));
                Assert.That(slider.AngleAt(1050), Is.EqualTo(10));
                Assert.That(Enumerable.Range(0, slider.SegmentCount).Any(slider.SegmentEndsWithReversal), Is.False);
                Assert.That(slider.UpcomingSegmentIndexAt(1050), Is.EqualTo(-1));
            });
        }

        [TestCase(90f)]
        [TestCase(0f)]
        public void TestFixedLanePreviewsContinuationWithoutFakeReversal(float firstArc)
        {
            SticksSlider slider = create(new[] { firstArc, 90f }, new[] { 500d, 500d });
            var drawable = new DrawableSticksSlider(slider);
            MethodInfo updatePreview = typeof(DrawableSticksSlider).GetMethod(
                "updateReversalPathPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var preview = (CircularProgress)typeof(DrawableSticksSlider).GetField(
                "reversalPathPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
            var outline = (CircularProgress)typeof(DrawableSticksSlider).GetField(
                "reversalPathPreviewOutline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;

            updatePreview.Invoke(drawable, new object[] { 1250d, true });

            Assert.Multiple(() =>
            {
                Assert.That(slider.UpcomingSegmentIndexAt(1250), Is.EqualTo(-1), "There is no reversal.");
                Assert.That(slider.UpcomingPathSegmentIndexAt(1250), Is.EqualTo(1));
                Assert.That(slider.UpcomingPathSegmentPreviewProgressAt(1250), Is.EqualTo(0.125).Within(0.00001));
                Assert.That(preview.Alpha, Is.GreaterThan(0), "The continuation is visible before its anchor.");
                Assert.That(preview.Progress, Is.EqualTo(0.03125).Within(0.00001));
                Assert.That(preview.Rotation, Is.EqualTo(100 + firstArc));
                Assert.That(outline.Alpha, Is.Zero);
                Assert.That(outline.Progress, Is.Zero);
            });
        }

        [Test]
        public void TestPathPreviewDoesNotSkipAheadToLaterReversal()
        {
            SticksSlider slider = create(new[] { 90f, 45f, -60f }, new[] { 300d, 300d, 400d });

            Assert.Multiple(() =>
            {
                Assert.That(slider.UpcomingSegmentIndexAt(1200), Is.EqualTo(2));
                Assert.That(slider.UpcomingPathSegmentIndexAt(1200), Is.EqualTo(1));
                Assert.That(slider.UpcomingPathSegmentPreviewProgressAt(1200), Is.GreaterThan(0));
                Assert.That(slider.SegmentEndsWithReversal(0), Is.False);
                Assert.That(slider.SegmentEndsWithReversal(1), Is.True);
                Assert.That(slider.UpcomingPathSegmentIndexAt(1400), Is.EqualTo(2));
                Assert.That(slider.UpcomingPathSegmentIndexAt(1700), Is.EqualTo(-1));
            });
        }

        [TestCase(700, 2300, 97)]
        [TestCase(1400, 1400, 1)]
        [TestCase(2300, 700, 97)]
        [TestCase(1000, 2000, 0)]
        public void TestAngleSamplesMatchTimedTracking(double start, double end, int count)
        {
            SticksSlider slider = create(new[] { 0.25f, 0f, -40f, 120f }, new[] { 200d, 300d, 100d, 400d });
            var samples = new float[count];
            slider.FillAngleSamples(start, end, samples);

            for (int i = 0; i < count; i++)
            {
                double time = count == 1 ? start : start + (end - start) * i / (count - 1);
                Assert.That(samples[i], Is.EqualTo(slider.AngleAt(time)), $"Sample at {time}");
            }
        }

        [Test]
        public void TestOverallDurationEditsScaleEveryAnchor()
        {
            SticksSlider slider = create(new[] { 0.25f, 0f, -40f }, new[] { 200d, 300d, 500d });
            slider.Duration = 2000;

            Assert.Multiple(() =>
            {
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(1400));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(2000));
                Assert.That(slider.SegmentEndTimeAt(2), Is.EqualTo(3000));
                Assert.That(slider.AngleAt(1400), Is.EqualTo(10.25f));
                Assert.That(slider.AngleAt(2500), Is.EqualTo(-9.75f));
            });

            slider.Duration = 1;
            Assert.Multiple(() =>
            {
                Assert.That(slider.AngleAt(1000.1), Is.EqualTo(10.125f).Within(0.00001));
                Assert.That(slider.AngleAt(1000.2), Is.EqualTo(10.25f).Within(0.00001));
                Assert.That(slider.AngleAt(1000.75), Is.EqualTo(-9.75f).Within(0.00001));
                Assert.That(slider.AngleAt(1001), Is.EqualTo(-29.75f).Within(0.00001));
            });
        }

        [Test]
        public void TestReplacingEndpointPreservesAllTimings()
        {
            SticksSlider slider = create(new[] { 30f, 0f, -60f }, new[] { 200d, 100d, 700d });
            slider.ReplaceFinalSegment(-90);

            Assert.Multiple(() =>
            {
                Assert.That(slider.HasTimedSegments, Is.True);
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 30f, 0f, -90f }));
                Assert.That(slider.SegmentDurationWeights, Is.EqualTo(new[] { 0.2d, 0.1d, 0.7d }));
                Assert.That(slider.AngleAt(1300), Is.EqualTo(40));
                Assert.That(slider.AngleAt(2000), Is.EqualTo(-50));
            });
        }

        [Test]
        public void TestAppendingAndRemovingRetainEarlierTimes()
        {
            SticksSlider slider = create(new[] { 30f, -60f, 0f }, new[] { 200d, 600d, 100d });
            slider.Duration = 900;
            slider.AppendSegmentAtConstantSpeed(30);

            Assert.Multiple(() =>
            {
                Assert.That(slider.HasTimedSegments, Is.True);
                Assert.That(slider.Duration, Is.EqualTo(1200).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(1200).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(1800).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(2), Is.EqualTo(1900).Within(0.000001));
                Assert.That(slider.SegmentDurationAt(3), Is.EqualTo(300).Within(0.000001));
                Assert.That(slider.RemoveFinalSegmentAtConstantSpeed(), Is.True);
                Assert.That(slider.Duration, Is.EqualTo(900).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(1200).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(1800).Within(0.000001));
            });
        }

        [Test]
        public void TestTimedContinuationUsesFinalMovingSpeed()
        {
            SticksSlider slider = create(new[] { 30f, -60f, 0f }, new[] { 200d, 600d, 100d });
            slider.Duration = 900;

            Assert.That(slider.ContinuationArcAt(2300), Is.EqualTo(40).Within(0.00001));
            Assert.That(slider.AppendTimedSegmentAtConstantSpeed(2300), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(slider.Duration, Is.EqualTo(1300));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(1200).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(1800).Within(0.000001));
                Assert.That(slider.SegmentEndTimeAt(2), Is.EqualTo(1900).Within(0.000001));
                Assert.That(slider.AngleAt(2300), Is.EqualTo(20).Within(0.00001));
            });
        }

        [Test]
        public void TestSegmentLimitRejectsMutationWithoutMovingExistingAnchors()
        {
            SticksSlider slider = create(Enumerable.Repeat(1f, SticksSlider.MAX_SEGMENT_COUNT).ToArray(),
                Enumerable.Repeat(1d, SticksSlider.MAX_SEGMENT_COUNT).ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(() => slider.AppendSegmentAtConstantSpeed(10), Throws.InvalidOperationException);
                Assert.That(slider.AppendTimedSegmentAtConstantSpeed(2200), Is.False);
                Assert.That(slider.Duration, Is.EqualTo(1000));
                Assert.That(slider.SegmentCount, Is.EqualTo(SticksSlider.MAX_SEGMENT_COUNT));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(1062.5));
            });
        }

        [Test]
        public void TestRemovingOnlyMovingSegmentKeepsValidPath()
        {
            SticksSlider slider = create(new[] { 0f, 10f }, new[] { 200d, 800d });

            Assert.Multiple(() =>
            {
                Assert.That(slider.RemoveFinalSegmentAtConstantSpeed(), Is.False);
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 0f, 10f }));
                Assert.That(slider.Duration, Is.EqualTo(1000));
            });
        }

        [TestCase("arc")]
        [TestCase("repeat")]
        [TestCase("custom")]
        public void TestLegacyEditsClearIndependentTiming(string edit)
        {
            SticksSlider slider = create(new[] { 30f, 60f }, new[] { 200d, 800d });
            switch (edit)
            {
                case "arc":
                    slider.ArcAngle = 40;
                    break;

                case "repeat":
                    slider.RepeatCount = 2;
                    break;

                default:
                    slider.SetCustomSegments(new[] { 90f, -180f, 45f });
                    break;
            }

            Assert.That(slider.HasTimedSegments, Is.False);
            Assert.That(slider.SegmentDurationWeights, Is.Null);
            double speed = slider.TotalAngularDistance / slider.Duration;
            for (int i = 0; i < slider.SegmentCount; i++)
                Assert.That(Math.Abs(slider.SegmentArcAngleAt(i)) / slider.SegmentDurationAt(i), Is.EqualTo(speed).Within(0.000001));
        }

        [TestCaseSource(nameof(invalidPaths))]
        public void TestMalformedPathsDoNotPartiallyMutate(float[] arcs, double[] durations)
        {
            SticksSlider slider = create(new[] { 30f, 60f }, new[] { 200d, 800d });

            Assert.Throws<ArgumentException>(() => slider.SetTimedSegments(arcs, durations));
            Assert.Multiple(() =>
            {
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 30f, 60f }));
                Assert.That(slider.SegmentDurationWeights, Is.EqualTo(new[] { 0.2d, 0.8d }));
            });
        }

        private static IEnumerable<TestCaseData> invalidPaths()
        {
            yield return new TestCaseData(Array.Empty<float>(), Array.Empty<double>());
            yield return new TestCaseData(new[] { 1f, 2f }, new[] { 1d });
            yield return new TestCaseData(Enumerable.Repeat(1f, 17).ToArray(), Enumerable.Repeat(1d, 17).ToArray());
            yield return new TestCaseData(new[] { 0f, 0f }, new[] { 1d, 1d });
            yield return new TestCaseData(new[] { float.NaN }, new[] { 1d });
            yield return new TestCaseData(new[] { float.PositiveInfinity }, new[] { 1d });
            yield return new TestCaseData(new[] { 1f }, new[] { 0d });
            yield return new TestCaseData(new[] { 1f }, new[] { -1d });
            yield return new TestCaseData(new[] { 1f }, new[] { double.NaN });
            yield return new TestCaseData(new[] { 1f }, new[] { double.PositiveInfinity });
            yield return new TestCaseData(new[] { 1f, 1f }, new[] { double.MaxValue, double.MaxValue });
            yield return new TestCaseData(new[] { 1f, 1f }, new[] { double.Epsilon, double.MaxValue });
        }

        [Test]
        public void TestJsonRoundTripKeepsTinyArcsAndDwells()
        {
            SticksSlider slider = create(new[] { 0.25f, 0f, -40f }, new[] { 200d, 300d, 500d });
            double[] weights = slider.SegmentDurationWeights.ToArray();

            for (int cycle = 0; cycle < 5; cycle++)
            {
                string json = JsonConvert.SerializeObject(slider);
                slider = JsonConvert.DeserializeObject<SticksSlider>(json);

                Assert.Multiple(() =>
                {
                    Assert.That(slider.HasTimedSegments, Is.True);
                    Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 0.25f, 0f, -40f }));
                    Assert.That(slider.SegmentDurationWeights, Is.EqualTo(weights));
                    Assert.That(slider.AngleAt(1200), Is.EqualTo(10.25));
                    Assert.That(slider.AngleAt(1400), Is.EqualTo(10.25));
                    Assert.That(slider.AngleAt(1750), Is.EqualTo(-9.75));
                });
            }
        }

        [Test]
        public void TestJsonTimingMayPrecedeSegmentsAndLegacyProperties()
        {
            const string json = "{\"segmentDurationWeights\":[0.2,0.3,0.5],\"segments\":[0.25,0,-40],\"StartTime\":1000,\"Duration\":1000,\"Angle\":10,\"ArcAngle\":90,\"RepeatCount\":0}";
            SticksSlider slider = JsonConvert.DeserializeObject<SticksSlider>(json);

            Assert.Multiple(() =>
            {
                Assert.That(slider.HasTimedSegments, Is.True);
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 0.25f, 0f, -40f }));
                Assert.That(slider.SegmentDurationWeights, Is.EqualTo(new[] { 0.2d, 0.3d, 0.5d }));
                Assert.That(slider.AngleAt(1750), Is.EqualTo(-9.75));
            });
        }

        [Test]
        public void TestLegacyJsonStillUsesConstantSpeed()
        {
            const string json = "{\"segments\":[90,-180,45],\"StartTime\":1000,\"Duration\":3500,\"Angle\":10}";
            SticksSlider slider = JsonConvert.DeserializeObject<SticksSlider>(json);

            Assert.Multiple(() =>
            {
                Assert.That(slider.HasTimedSegments, Is.False);
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(2000));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(4000));
                Assert.That(slider.AngleAt(4500), Is.EqualTo(-35));
            });
        }

        private static SticksSlider create(float[] arcs, double[] durations)
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 1000, Angle = 10, Side = StickSide.Left };
            slider.SetTimedSegments(arcs, durations);
            return slider;
        }
    }
}
