using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksInspectorStickEditsTest
    {
        [Test]
        public void TestBothCreatesAnExactIndependentCounterpart()
        {
            SticksFlick source = flick(StickSide.Left, 1000, 15);
            source.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL));
            source.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 75));
            var notes = new SticksHitObject[] { source };
            Assert.That(SticksInspectorStickEdits.CanUseBoth(notes, notes, out string error), Is.True, error);
            Assert.That(SticksInspectorStickEdits.TryPrepare(notes, notes, null, out SticksInspectorStickEditPlan plan, out error), Is.True, error);

            SticksHitObject counterpart = plan.Additions.Single();
            Assert.Multiple(() =>
            {
                Assert.That(counterpart, Is.TypeOf<SticksFlick>());
                Assert.That(counterpart.Side, Is.EqualTo(StickSide.Right));
                Assert.That(counterpart.StartTime, Is.EqualTo(source.StartTime));
                Assert.That(counterpart.Angle, Is.EqualTo(source.Angle));
                Assert.That(counterpart.CreatePlayableSamples().Single(sample => sample.Name == HitSampleInfo.HIT_CLAP).Volume, Is.EqualTo(75));
                Assert.That(plan.Removals, Is.Empty);
                Assert.That(plan.Updates, Is.Empty);
                Assert.That(plan.Selection, Is.EqualTo(new[] { source, counterpart }));
                Assert.That(SticksInspectorStickEdits.IsBoth(plan.Selection), Is.True);
                Assert.That(source.Side, Is.EqualTo(StickSide.Left));
                Assert.That(source.Samples.Select(sample => sample.Name), Is.EqualTo(new[] { HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_CLAP }),
                    "Preparation must not rewrite the source samples.");
            });
        }

        [Test]
        public void TestBothReusesExactUnselectedPartnerAndRejectsNearMatches()
        {
            SticksFlick left = flick(StickSide.Left, 1000, 15);
            SticksFlick right = flick(StickSide.Right, 1000, 375);
            var selected = new SticksHitObject[] { left };
            var all = new SticksHitObject[] { left, right };
            Assert.That(SticksInspectorStickEdits.TryPrepare(selected, all, null, out SticksInspectorStickEditPlan plan, out string error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Additions, Is.Empty);
                Assert.That(plan.Selection, Is.EqualTo(all));
                Assert.That(SticksInspectorStickEdits.IsBoth(plan.Selection), Is.True);
            });

            right.Angle = 16;
            Assert.That(SticksInspectorStickEdits.CanUseBoth(selected, all, out _), Is.False, "A nearby existing head occupies the requested hand but is not an exact partner.");
            right.Angle = left.Angle;
            right.StartTime += 0.25;
            Assert.That(SticksInspectorStickEdits.TryPrepare(selected, all, null, out plan, out _), Is.False);
            Assert.That(plan.Additions, Is.Empty);
        }

        [Test]
        public void TestSingleStickCollapsesOnlySelectedExactPair()
        {
            SticksFlick left = flick(StickSide.Left, 1000, 15);
            SticksFlick right = flick(StickSide.Right, 1000, 15);
            var pair = new SticksHitObject[] { right, left };
            Assert.That(SticksInspectorStickEdits.TryPrepare(pair, pair, StickSide.Left, out SticksInspectorStickEditPlan plan, out string error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Removals, Is.EqualTo(new[] { right }));
                Assert.That(plan.Selection, Is.EqualTo(new[] { left }));
                Assert.That(plan.Updates, Is.Empty);
                Assert.That(plan.Additions, Is.Empty);
                Assert.That(right.Side, Is.EqualTo(StickSide.Right));
            });

            right.Angle = 40;
            Assert.That(SticksInspectorStickEdits.IsBoth(pair), Is.False);
            Assert.That(SticksInspectorStickEdits.TryPrepare(pair, pair, StickSide.Left, out _, out _), Is.False,
                "Changing unrelated simultaneous notes to one hand must not delete either as though it were an exact pair.");
        }

        [Test]
        public void TestMixedSelectionUpdatesOnlyTheChangedHand()
        {
            SticksFlick left = flick(StickSide.Left, 1000, 15);
            SticksFlick right = flick(StickSide.Right, 2000, 40);
            var notes = new SticksHitObject[] { left, right };
            Assert.That(SticksInspectorStickEdits.GetSelectionSide(notes), Is.Null);
            Assert.That(SticksInspectorStickEdits.IsBoth(notes), Is.False);
            Assert.That(SticksInspectorStickEdits.TryPrepare(notes, notes, StickSide.Right, out SticksInspectorStickEditPlan plan, out string error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Updates.Single().Target, Is.SameAs(left));
                Assert.That(plan.Additions, Is.Empty);
                Assert.That(plan.Removals, Is.Empty);
                Assert.That(left.Side, Is.EqualTo(StickSide.Left));
            });
            plan.Updates.Single().Apply();
            Assert.That(SticksInspectorStickEdits.GetSelectionSide(plan.Selection), Is.EqualTo(StickSide.Right));
        }

        [Test]
        public void TestTimedSliderCopyPreservesWeightsAndIndependentNodeSamples()
        {
            var source = new SticksSlider { StartTime = 1000, Duration = 1234.5678, Angle = 12, Side = StickSide.Left };
            source.SetTimedSegments(new[] { 90f, 0f, -35f }, new[] { 0.2d, 0.3d, 0.5d });
            source.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 80));
            source.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 65) });
            source.EnsureLegacyEditorMarker();
            var notes = new SticksHitObject[] { source };

            Assert.That(SticksInspectorStickEdits.TryPrepare(notes, notes, null, out SticksInspectorStickEditPlan plan, out string error), Is.True, error);
            var copy = (SticksSlider)plan.Additions.Single();
            Assert.Multiple(() =>
            {
                Assert.That(copy.Duration, Is.EqualTo(source.Duration));
                Assert.That(copy.SegmentArcAngles, Is.EqualTo(source.SegmentArcAngles));
                Assert.That(copy.SegmentDurationWeights, Is.EqualTo(source.SegmentDurationWeights));
                Assert.That(Enumerable.Range(0, copy.SegmentCount).Select(copy.SegmentDurationAt),
                    Is.EqualTo(Enumerable.Range(0, source.SegmentCount).Select(source.SegmentDurationAt)));
                Assert.That(copy.CreatePlayableSamples().Select(sample => sample.Volume),
                    Is.EqualTo(source.CreatePlayableSamples().Select(sample => sample.Volume)));
                Assert.That(copy.NodeSamples.Single(), Is.Not.SameAs(source.NodeSamples.Single()));
                Assert.That(copy.NodeSamples.Single().Single(), Is.Not.SameAs(source.NodeSamples.Single().Single()));
                Assert.That(copy.NodeSamples.Single().Single().Name, Is.EqualTo(HitSampleInfo.HIT_WHISTLE));
                Assert.That(copy.NodeSamples.Single().Single().Volume, Is.EqualTo(65));
                Assert.That(SticksInspectorStickEdits.IsBoth(plan.Selection), Is.True);
            });
        }

        [Test]
        public void TestBothChecksSustainedOccupancyAndKeepsClicksSeparate()
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 1200, Angle = 0, ArcAngle = 90, Side = StickSide.Left };
            SticksFlick right = flick(StickSide.Right, 1500, 90);
            var all = new SticksHitObject[] { slider, right };
            Assert.That(SticksInspectorStickEdits.CanUseBoth(new SticksHitObject[] { slider }, all, out _), Is.False);
            Assert.That(SticksInspectorStickEdits.CanUseBoth(new SticksHitObject[] { right }, all, out _), Is.False);

            right.StartTime = slider.EndTime;
            Assert.That(SticksInspectorStickEdits.CanUseBoth(new SticksHitObject[] { slider }, all, out string error), Is.True, error);

            var click = new SticksClick { StartTime = 1500, Side = StickSide.Right };
            Assert.That(SticksInspectorStickEdits.CanUseBoth(new SticksHitObject[] { click }, new SticksHitObject[] { slider, right, click }, out error), Is.True, error);
            Assert.That(SticksInspectorStickEdits.TryPrepare(new SticksHitObject[] { click }, new SticksHitObject[] { slider, right, click }, null,
                out SticksInspectorStickEditPlan plan, out error), Is.True, error);
            Assert.That(plan.Additions.Single(), Is.TypeOf<SticksClick>());
        }

        [Test]
        public void TestManySelectedNotesCanCheckBothWithoutCreatingCopies()
        {
            SticksHitObject[] notes = Enumerable.Range(0, 2048).Select(index => (SticksHitObject)flick(StickSide.Left, 1000 + index * 100, index % 360)).ToArray();
            Assert.That(SticksInspectorStickEdits.CanUseBoth(notes, notes, out string error), Is.True, error);
            Assert.That(notes.All(note => note.Side == StickSide.Left && note.Samples.Count == 0), Is.True);
        }

        private static SticksFlick flick(StickSide side, double time, float angle) => new SticksFlick { Side = side, StartTime = time, Angle = angle };
    }
}
