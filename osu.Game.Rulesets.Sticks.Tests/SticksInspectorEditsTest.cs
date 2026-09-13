using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksInspectorEditsTest
    {
        [Test]
        public void TestPrepareIsAtomicAndAngleEditsPreserveLegacyTimingAndSamples()
        {
            SticksSlider left = slider(StickSide.Left);
            SticksSlider right = slider(StickSide.Right);
            var pair = new SticksHitObject[] { left, right };
            double[] originalDurations = Enumerable.Range(0, left.SegmentCount).Select(left.SegmentDurationAt).ToArray();
            HitSampleInfo[] originalSamples = left.Samples.ToArray();
            IList<HitSampleInfo>[] originalNodeSamples = left.NodeSamples.ToArray();

            Assert.That(SticksInspectorEdits.CanEditSegments(pair), Is.True);
            Assert.That(SticksInspectorEdits.TryPrepare(pair, new SticksInspectorEdit
            {
                StartTime = 1500,
                Angle = 720,
                SegmentAngles = new Dictionary<int, double> { [1] = -20 },
            }, 4000, out SticksInspectorEditPlan[] plans, out string error), Is.True, error);

            Assert.Multiple(() =>
            {
                Assert.That(left.StartTime, Is.EqualTo(1000), "Preparation must not mutate either selected note.");
                Assert.That(right.SegmentArcAngles, Is.EqualTo(new[] { 30f, -60f }));
                Assert.That(left.HasTimedSegments, Is.False);
                Assert.That(plans.Select(plan => plan.Target), Is.EqualTo(pair));
                Assert.That(plans.Select(plan => plan.EndTime), Is.EqualTo(new[] { 2700d, 2700d }));
            });

            foreach (SticksInspectorEditPlan plan in plans)
                plan.Apply();

            Assert.Multiple(() =>
            {
                Assert.That(pair.Select(note => note.StartTime), Is.EqualTo(new[] { 1500d, 1500d }));
                Assert.That(pair.Select(note => note.Angle), Is.EqualTo(new[] { 720f, 720f }), "Angles are not arbitrarily capped or normalised.");
                Assert.That(left.SegmentArcAngles, Is.EqualTo(new[] { 30f, -20f }));
                Assert.That(right.SegmentArcAngles, Is.EqualTo(left.SegmentArcAngles));
                Assert.That(Enumerable.Range(0, left.SegmentCount).Select(left.SegmentDurationAt), Is.EqualTo(originalDurations).Within(0.000001));
                Assert.That(left.Samples, Is.EqualTo(originalSamples));
                Assert.That(left.NodeSamples, Is.EqualTo(originalNodeSamples));
            });
        }

        [Test]
        public void TestGlobalDurationScalesSpansAndSingleSpanDurationKeepsOtherSpans()
        {
            SticksSlider target = slider();
            target.SetTimedSegments(new[] { 30f, -60f }, new[] { 200d, 1000d });
            var selected = new SticksHitObject[] { target };

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { Duration = 2400 }, 10000,
                out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
            plans.Single().Apply();
            Assert.Multiple(() =>
            {
                Assert.That(target.SegmentDurationAt(0), Is.EqualTo(400).Within(0.000001));
                Assert.That(target.SegmentDurationAt(1), Is.EqualTo(2000).Within(0.000001));
            });

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit
            {
                SegmentAngles = new Dictionary<int, double> { [1] = 0 },
                SegmentDurations = new Dictionary<int, double> { [1] = 800 },
            }, 10000, out plans, out error), Is.True, error);
            plans.Single().Apply();
            Assert.Multiple(() =>
            {
                Assert.That(target.Duration, Is.EqualTo(1200));
                Assert.That(target.SegmentDurationAt(0), Is.EqualTo(400).Within(0.000001));
                Assert.That(target.SegmentDurationAt(1), Is.EqualTo(800).Within(0.000001));
                Assert.That(target.SegmentArcAngles, Is.EqualTo(new[] { 30f, 0f }));
                Assert.That(target.AngleAt(2100), Is.EqualTo(40));
            });
        }

        [Test]
        public void TestStartOnlyMovesIntactAndEndTimeUsesTheNewStart()
        {
            SticksSlider target = slider();
            var selected = new SticksHitObject[] { target };
            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { StartTime = 2000 }, null,
                out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
            plans.Single().Apply();
            Assert.That(target.EndTime, Is.EqualTo(3200));

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { StartTime = 2500, EndTime = 4000 }, 4000,
                out plans, out error), Is.True, error);
            plans.Single().Apply();
            Assert.Multiple(() =>
            {
                Assert.That(target.Duration, Is.EqualTo(1500));
                Assert.That(target.EndTime, Is.EqualTo(4000));
                Assert.That(target.SegmentDurationAt(0), Is.EqualTo(500));
                Assert.That(target.HasTimedSegments, Is.False, "Moving and scaling a legacy path does not need to replace its geometry.");
            });
        }

        [Test]
        public void TestInvalidLaterTargetProducesNoPlansOrPartialChanges()
        {
            var flick = new SticksFlick { StartTime = 1000, Side = StickSide.Left, Angle = 10 };
            SticksSlider sustained = slider(StickSide.Right);
            var selected = new SticksHitObject[] { flick, sustained };

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { StartTime = 9500, Angle = 40 }, 10000,
                out SticksInspectorEditPlan[] plans, out string error), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(plans, Is.Empty);
                Assert.That(error, Does.Contain("audio"));
                Assert.That(flick.StartTime, Is.EqualTo(1000));
                Assert.That(flick.Angle, Is.EqualTo(10));
                Assert.That(sustained.StartTime, Is.EqualTo(1000));
            });
        }

        [Test]
        public void TestInvalidNumbersAndConflictingDurationEditsAreRejected()
        {
            SticksSlider target = slider();
            var invalidEdits = new[]
            {
                new SticksInspectorEdit { StartTime = -1 },
                new SticksInspectorEdit { Angle = double.NaN },
                new SticksInspectorEdit { Angle = double.MaxValue },
                new SticksInspectorEdit { Angle = float.MaxValue },
                new SticksInspectorEdit { EndTime = double.PositiveInfinity },
                new SticksInspectorEdit { EndTime = 1000 },
                new SticksInspectorEdit { Duration = 0 },
                new SticksInspectorEdit { EndTime = 2300, Duration = 1300 },
                new SticksInspectorEdit { Duration = 2000, SegmentDurations = new Dictionary<int, double> { [0] = 300 } },
                new SticksInspectorEdit { SegmentDurations = new Dictionary<int, double> { [0] = double.Epsilon } },
                new SticksInspectorEdit { SegmentDurations = new Dictionary<int, double> { [1] = -100 } },
                new SticksInspectorEdit { SegmentAngles = new Dictionary<int, double> { [2] = 40 } },
                new SticksInspectorEdit { SegmentAngles = new Dictionary<int, double> { [0] = float.MaxValue, [1] = float.MaxValue } },
                new SticksInspectorEdit { SegmentAngles = new Dictionary<int, double> { [0] = 2e38 } },
            };

            foreach (SticksInspectorEdit edit in invalidEdits)
            {
                Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { target }, edit, null,
                    out SticksInspectorEditPlan[] plans, out string error), Is.False, $"Accepted invalid edit: {edit}");
                Assert.That(plans, Is.Empty);
                Assert.That(error, Is.Not.Empty);
            }

            Assert.Multiple(() =>
            {
                Assert.That(target.Duration, Is.EqualTo(1200));
                Assert.That(target.SegmentArcAngles, Is.EqualTo(new[] { 30f, -60f }));
                Assert.That(target.HasTimedSegments, Is.False);
            });
        }

        [Test]
        public void TestSharedSegmentFieldsRequireMatchingPathsAndTiming()
        {
            SticksSlider first = slider(StickSide.Left);
            SticksSlider second = slider(StickSide.Right);
            second.Duration = 1400;
            var selected = new SticksHitObject[] { first, second };
            Assert.That(SticksInspectorEdits.CanEditSegments(selected), Is.False);
            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit
            {
                SegmentAngles = new Dictionary<int, double> { [1] = -20 },
            }, null, out _, out _), Is.False);
            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { Angle = 90 }, null, out _, out _), Is.True);

            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { first, new SticksFlick() },
                new SticksInspectorEdit { Duration = 1000 }, null, out _, out _), Is.False);
        }

        [Test]
        public void TestTinyDurationRemainsValidAfterSaving()
        {
            SticksSlider target = slider();
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { target }, new SticksInspectorEdit { Duration = 0.0001 },
                null, out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
            plans.Single().Apply();
            Assert.That(target.HasTimedSegments, Is.True, "A legacy marker would round this duration to zero.");
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(target), out SticksHitObject decoded), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(((SticksSlider)decoded).Duration, Is.EqualTo(0.0001));
                Assert.That(((SticksSlider)decoded).SegmentArcAngles, Is.EqualTo(target.SegmentArcAngles));
            });
        }

        [Test]
        public void TestCheckpointBudgetRejectsHugeFiniteWorkBeforeMutating()
        {
            SticksSlider target = slider();
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 100 });
            var selected = new SticksHitObject[] { target };

            foreach (SticksInspectorEdit edit in new[]
                     {
                         new SticksInspectorEdit { SegmentAngles = new Dictionary<int, double> { [0] = 1e9 } },
                         new SticksInspectorEdit { Duration = 1e12 },
                     })
            {
                Assert.That(SticksInspectorEdits.TryPrepare(selected, edit, null, out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
                Assert.That(SticksInspectorEdits.ValidateCheckpointBudget(plans, controlPoints, 1, out error), Is.False);
                Assert.That(error, Does.Contain("4096 checkpoints"));
            }

            Assert.Multiple(() =>
            {
                Assert.That(target.Duration, Is.EqualTo(1200));
                Assert.That(target.SegmentArcAngles, Is.EqualTo(new[] { 30f, -60f }));
            });

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit
            {
                SegmentAngles = new Dictionary<int, double> { [0] = 1080, [1] = -720 },
            }, null, out SticksInspectorEditPlan[] ordinaryPlans, out string ordinaryError), Is.True, ordinaryError);
            Assert.That(SticksInspectorEdits.ValidateCheckpointBudget(ordinaryPlans, controlPoints, 1, out ordinaryError), Is.True, ordinaryError);
            ordinaryPlans.Single().Apply();
            target.ApplyDefaults(controlPoints, new BeatmapDifficulty { SliderTickRate = 1 });
            Assert.That(target.NestedHitObjects.Count, Is.EqualTo(16), "The budget includes two endpoints, a seam, three full-turn checkpoints and ten ticks.");
        }

        [Test]
        public void TestCheckpointBudgetUsesActualTickCountAtTheBoundary()
        {
            var target = new SticksSlider { StartTime = 1000, Duration = 1000, ArcAngle = 0 };
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 100 });
            var selected = new SticksHitObject[] { target };

            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { Duration = 409500 }, null,
                out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
            Assert.That(SticksInspectorEdits.ValidateCheckpointBudget(plans, controlPoints, 1, out error), Is.True, error);
            Assert.That(SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit { Duration = 409600 }, null,
                out plans, out error), Is.True, error);
            Assert.That(SticksInspectorEdits.ValidateCheckpointBudget(plans, controlPoints, 1, out error), Is.False);
        }

        [Test]
        public void TestOnlyNewSameInputCollisionsAreRejected()
        {
            var left = new SticksFlick { StartTime = 1000, Side = StickSide.Left };
            var right = new SticksFlick { StartTime = 1000, Side = StickSide.Right };
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { left, right },
                new SticksInspectorEdit { Side = StickSide.Left }, null, out _, out _), Is.False);

            right.Side = StickSide.Left;
            right.StartTime = 2000;
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { left, right },
                new SticksInspectorEdit { StartTime = 1000 }, null, out _, out _), Is.False);

            right.StartTime = 1000;
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { left, right },
                new SticksInspectorEdit { StartTime = 1500 }, null, out _, out _), Is.True, "Existing duplicates can still be edited.");
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { left, new SticksClick { StartTime = 2000, Side = StickSide.Left } },
                new SticksInspectorEdit { StartTime = 1000 }, null, out _, out _), Is.True, "A click uses a different input from a directional head.");

            var nearby = new SticksHitObject[]
            {
                new SticksFlick { StartTime = 1000, Side = StickSide.Left },
                new SticksFlick { StartTime = 1000.4, Side = StickSide.Left },
                new SticksFlick { StartTime = 1000.8, Side = StickSide.Left },
            };
            Assert.That(SticksInspectorEdits.TryPrepare(nearby, new SticksInspectorEdit { StartTime = 2000 }, null, out _, out _),
                Is.False, "Checking only adjacent original pairs would overlook a newly colliding first/last pair.");
        }

        [Test]
        public void TestUnchangedEditsKeepRepresentationAndLargeExistingStacks()
        {
            SticksSlider target = slider();
            target.Angle = -720;
            Assert.That(SticksInspectorEdits.TryPrepare(new SticksHitObject[] { target }, new SticksInspectorEdit(), null,
                out SticksInspectorEditPlan[] plans, out string error), Is.True, error);
            plans.Single().Apply();
            Assert.Multiple(() =>
            {
                Assert.That(target.HasTimedSegments, Is.False);
                Assert.That(target.Angle, Is.EqualTo(-720));
            });

            SticksHitObject[] selection = Enumerable.Range(0, 4096)
                                                    .Select(_ => (SticksHitObject)new SticksFlick { StartTime = 1000, Side = StickSide.Left })
                                                    .ToArray();
            Assert.That(SticksInspectorEdits.TryPrepare(selection, new SticksInspectorEdit { StartTime = 1500 }, null,
                out plans, out error), Is.True, error);
            Assert.That(plans.Length, Is.EqualTo(selection.Length));
        }

        private static SticksSlider slider(StickSide side = StickSide.Left)
        {
            var result = new SticksSlider { StartTime = 1000, Duration = 1200, Angle = 10, Side = side };
            result.SetCustomSegments(new[] { 30f, -60f });
            result.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 80));
            result.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 70) });
            result.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 60) });
            return result;
        }
    }
}
