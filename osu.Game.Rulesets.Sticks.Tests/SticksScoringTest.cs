// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksScoringTest
    {
        [TestCase(0, 79.5, 139.5, 199.5)]
        [TestCase(5, 49.5, 99.5, 149.5)]
        [TestCase(10, 19.5, 59.5, 99.5)]
        public void TestTimingWindowsMatchOsuStandard(double overallDifficulty, double great, double ok, double meh)
        {
            var windows = new SticksHitWindows();
            windows.SetDifficulty(overallDifficulty);

            Assert.Multiple(() =>
            {
                Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(great));
                Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(ok));
                Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(meh));
                Assert.That(windows.WindowFor(HitResult.Miss), Is.EqualTo(400));

                Assert.That(windows.ResultFor(great), Is.EqualTo(HitResult.Great));
                Assert.That(windows.ResultFor(great + 0.001), Is.EqualTo(HitResult.Ok));
                Assert.That(windows.ResultFor(ok + 0.001), Is.EqualTo(HitResult.Meh));
                Assert.That(windows.ResultFor(meh + 0.001), Is.EqualTo(HitResult.Miss));
                Assert.That(windows.ResultFor(400.001), Is.EqualTo(HitResult.None));
            });
        }

        [Test]
        public void TestTimingAndAngleHaveEqualAccuracyWeightAndOneCombo()
        {
            var processor = new SticksScoreProcessor(new SticksRuleset());
            var timing = result(new SticksFlick(), HitResult.Great);
            var angle = result(new SticksAngleComponent(), HitResult.Ok);

            processor.ApplyResult(timing);
            Assert.That(processor.Combo.Value, Is.EqualTo(1));

            processor.ApplyResult(angle);
            Assert.Multiple(() =>
            {
                Assert.That(processor.Combo.Value, Is.EqualTo(1));
                Assert.That(processor.HighestCombo.Value, Is.EqualTo(1));
                Assert.That(processor.Accuracy.Value, Is.EqualTo(2.0 / 3).Within(0.000001));
            });

            processor.RevertResult(angle);
            Assert.Multiple(() =>
            {
                Assert.That(processor.Combo.Value, Is.EqualTo(1));
                Assert.That(processor.HighestCombo.Value, Is.EqualTo(1));
                Assert.That(processor.Accuracy.Value, Is.EqualTo(1));
            });

            processor.RevertResult(timing);
            Assert.That(processor.Combo.Value, Is.Zero);
        }

        [Test]
        public void TestEitherComponentMissMakesWholeNoteMiss()
        {
            var processor = new SticksScoreProcessor(new SticksRuleset());
            (HitResult timing, HitResult angle) = SticksHitObject.ResolveComponentResults(HitResult.Great, HitResult.Miss);
            processor.ApplyResult(result(new SticksFlick(), timing));
            processor.ApplyResult(result(new SticksAngleComponent(), angle));

            Assert.Multiple(() =>
            {
                Assert.That(processor.Combo.Value, Is.Zero);
                Assert.That(processor.HighestCombo.Value, Is.Zero);
                Assert.That(processor.Accuracy.Value, Is.Zero);
            });
        }

        [TestCase(HitResult.Great, HitResult.Great, HitResult.Perfect)]
        [TestCase(HitResult.Great, HitResult.Ok, HitResult.Great)]
        [TestCase(HitResult.Ok, HitResult.Great, HitResult.Great)]
        [TestCase(HitResult.Meh, HitResult.Great, HitResult.Good)]
        [TestCase(HitResult.Ok, HitResult.Ok, HitResult.Ok)]
        [TestCase(HitResult.Meh, HitResult.Ok, HitResult.Meh)]
        [TestCase(HitResult.Great, HitResult.Miss, HitResult.Miss)]
        [TestCase(HitResult.Miss, HitResult.Great, HitResult.Miss)]
        public void TestCombinedJudgementPalette(HitResult timing, HitResult angle, HitResult expected) =>
            Assert.That(SticksJudgementDisplay.CombinedResult(timing, angle), Is.EqualTo(expected));

        [Test]
        public void TestSliderHasIndependentHeadComponentsAndIgnoreParent()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });

            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                ArcAngle = 90,
            };

            slider.ApplyDefaults(controlPoints, new BeatmapDifficulty
            {
                OverallDifficulty = 5,
                SliderTickRate = 1,
            });

            SticksSliderHead head = (SticksSliderHead)slider.NestedHitObjects[0];

            Assert.Multiple(() =>
            {
                Assert.That(slider.CreateJudgement().MaxResult, Is.EqualTo(HitResult.IgnoreHit));
                Assert.That(head.AccuracyComponent, Is.EqualTo(SticksAccuracyComponent.Timing));
                Assert.That(head.NestedHitObjects, Has.One.TypeOf<SticksAngleComponent>());
                Assert.That(((SticksAngleComponent)head.NestedHitObjects[0]).AccuracyComponent, Is.EqualTo(SticksAccuracyComponent.Angle));
                Assert.That(slider.NestedHitObjects, Has.One.TypeOf<SticksSliderTail>());
                Assert.That(slider.NestedHitObjects, Has.One.TypeOf<SticksSliderTick>());
            });
        }

        [Test]
        public void TestHoldUsesIndependentSliderStyleCheckpoints()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });

            var hold = new SticksHold
            {
                StartTime = 1000,
                Duration = 1600,
                Side = StickSide.Right,
                Angle = 135,
            };

            hold.ApplyDefaults(controlPoints, new BeatmapDifficulty
            {
                OverallDifficulty = 5,
                SliderTickRate = 1,
            });

            SticksHoldHead head = (SticksHoldHead)hold.NestedHitObjects[0];

            Assert.Multiple(() =>
            {
                Assert.That(hold.CreateJudgement().MaxResult, Is.EqualTo(HitResult.IgnoreHit));
                Assert.That(head.AccuracyComponent, Is.EqualTo(SticksAccuracyComponent.Timing));
                Assert.That(head.NestedHitObjects, Has.One.TypeOf<SticksAngleComponent>());
                Assert.That(hold.NestedHitObjects.OfType<SticksHoldTick>().Count(), Is.EqualTo(3));
                Assert.That(hold.NestedHitObjects, Has.One.TypeOf<SticksHoldTail>());
                Assert.That(((SticksHoldTail)hold.NestedHitObjects[hold.NestedHitObjects.Count - 1]).CreateJudgement().MinResult, Is.EqualTo(HitResult.IgnoreMiss));
            });
        }

        private static JudgementResult result(SticksHitObject hitObject, HitResult type) => new JudgementResult(hitObject, hitObject.CreateJudgement())
        {
            Type = type,
        };
    }
}
