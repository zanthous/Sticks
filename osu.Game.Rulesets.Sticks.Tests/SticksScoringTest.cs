// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.StateChanges;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Replays;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

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

        [Test]
        public void TestRecordedFlickReplaysWithIdenticalJudgementAndScore()
        {
            const double note_time = 1000;
            const float target_angle = 20;
            const float played_angle = 32;

            Vector2 direction = new Vector2(
                System.MathF.Cos(played_angle * System.MathF.PI / 180),
                System.MathF.Sin(played_angle * System.MathF.PI / 180));
            var frames = new List<SticksReplayFrame>
            {
                new SticksReplayFrame(900, Vector2.Zero, Vector2.Zero),
                new SticksReplayFrame(990, direction * 0.75f, Vector2.Zero),
                new SticksReplayFrame(1008, direction, Vector2.Zero),
                new SticksReplayFrame(1040, direction, Vector2.Zero),
            };

            var originalInput = new SticksInputTracker();
            foreach (SticksReplayFrame frame in frames)
                originalInput.Update(StickSide.Left, frame.LeftStick, frame.Time);

            var replay = new Replay { Frames = frames.Cast<ReplayFrame>().ToList() };
            var provider = new SticksReplayInputProvider();
            var replayHandler = new SticksFramedReplayInputHandler(replay, provider);
            var replayedInput = new SticksInputTracker();

            foreach (double time in new[] { 900d, 990, note_time, 1008, 1040 })
            {
                Assert.That(replayHandler.SetFrameFromTime(time), Is.EqualTo(time));
                replayHandler.CollectPendingInputs(new List<IInput>());
                (Vector2 left, _) = provider.Snapshot();
                replayedInput.Update(StickSide.Left, left, time);

                if (time == note_time)
                    Assert.That(replayedInput.SequenceFor(StickSide.Left), Is.Zero,
                        "Playback must not interpolate across the activation threshold before the recorded edge sample.");
            }

            SticksInputTracker.FlickEvent originalFlick = originalInput.LastFlickFor(StickSide.Left);
            SticksInputTracker.FlickEvent replayedFlick = replayedInput.LastFlickFor(StickSide.Left);

            SticksFlick hitObject = createFlick(note_time, StickSide.Left, target_angle);
            (HitResult originalTiming, HitResult originalAngle) = resultsFor(hitObject, originalFlick);
            (HitResult replayTiming, HitResult replayAngle) = resultsFor(hitObject, replayedFlick);
            SticksScoreProcessor originalScore = score(hitObject, originalTiming, originalAngle);
            SticksScoreProcessor replayScore = score(hitObject, replayTiming, replayAngle);

            Assert.Multiple(() =>
            {
                Assert.That(replayedFlick.Sequence, Is.EqualTo(originalFlick.Sequence));
                Assert.That(replayedFlick.Time, Is.EqualTo(originalFlick.Time));
                Assert.That(replayedFlick.Angle, Is.EqualTo(originalFlick.Angle).Within(0.0001));
                Assert.That((replayTiming, replayAngle), Is.EqualTo((originalTiming, originalAngle)));
                Assert.That(replayScore.TotalScore.Value, Is.EqualTo(originalScore.TotalScore.Value));
                Assert.That(replayScore.Accuracy.Value, Is.EqualTo(originalScore.Accuracy.Value));
                Assert.That(replayScore.Combo.Value, Is.EqualTo(originalScore.Combo.Value));
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
        public void TestJudgementFeedbackPairsInterleavedNotesIndependently()
        {
            SticksFlick first = createFlick(1000, StickSide.Left, 0);
            SticksFlick second = createFlick(1000, StickSide.Right, 180);
            var firstAngle = (SticksAngleComponent)first.NestedHitObjects.Single();
            var secondAngle = (SticksAngleComponent)second.NestedHitObjects.Single();
            var display = new SticksJudgementDisplay();

            // This is the ordering that a simultaneous chord is allowed to produce. A single
            // pending timing slot would overwrite the first note before either angle arrived.
            display.Process(result(first, HitResult.Great));
            display.Process(result(second, HitResult.Ok));
            display.Process(result(firstAngle, HitResult.Ok));

            Assert.That(display.LastResult, Is.EqualTo(HitResult.Great));

            display.Process(result(secondAngle, HitResult.Great));
            Assert.That(display.LastResult, Is.EqualTo(HitResult.Great));
        }

        [Test]
        public void TestJudgementFeedbackClearsPendingPairOnRevert()
        {
            SticksFlick flick = createFlick(1000, StickSide.Left, 0);
            var angle = (SticksAngleComponent)flick.NestedHitObjects.Single();
            var display = new SticksJudgementDisplay();
            JudgementResult timingResult = result(flick, HitResult.Great);

            display.Process(timingResult);
            display.Revert(timingResult);
            display.Process(result(angle, HitResult.Great));

            Assert.That(display.LastResult, Is.Null);
        }

        [Test]
        public void TestJudgementFeedbackUsesThinBottomBar()
        {
            var display = new SticksJudgementDisplay();

            Assert.Multiple(() =>
            {
                Assert.That(display.Size.X, Is.EqualTo(SticksPlayfield.SIZE));
                Assert.That(display.Size.Y, Is.EqualTo(SticksJudgementDisplay.BAR_HEIGHT));
                Assert.That(SticksJudgementDisplay.DISPLAY_DURATION, Is.EqualTo(420));
                Assert.That(SticksJudgementDisplay.FADE_DURATION, Is.EqualTo(100));
                Assert.That(display.Position.Y + display.Size.Y, Is.EqualTo(SticksPlayfield.SIZE));
            });
        }

        [Test]
        public void TestJudgementFeedbackShowsSuccessfulActionCheckpoints()
        {
            var display = new SticksJudgementDisplay();
            (SticksHitObject Object, HitResult Result)[] checkpoints =
            {
                (new SticksSliderTail(), HitResult.SliderTailHit),
                (new SticksHoldTail(), HitResult.SliderTailHit),
                (new SticksSliderRepeat(), HitResult.LargeTickHit),
                (new SticksSliderExtension(), HitResult.LargeTickHit),
            };

            foreach ((SticksHitObject hitObject, HitResult hitResult) in checkpoints)
            {
                display.ResetDisplay();
                display.Process(result(hitObject, hitResult));
                Assert.That(display.LastResult, Is.EqualTo(hitResult), hitObject.GetType().Name);
            }
        }

        [Test]
        public void TestJudgementFeedbackDoesNotShowTrackingTicks()
        {
            var display = new SticksJudgementDisplay();

            display.Process(result(new SticksSliderTick(), HitResult.LargeTickHit));
            display.Process(result(new SticksHoldTick(), HitResult.LargeTickHit));

            Assert.That(display.LastResult, Is.Null);
        }

        [Test]
        public void TestJudgementFeedbackDoesNotDrawMisses()
        {
            SticksFlick flick = createFlick(1000, StickSide.Left, 0);
            var angle = (SticksAngleComponent)flick.NestedHitObjects.Single();
            var display = new SticksJudgementDisplay();

            display.Process(result(flick, HitResult.Great));
            display.Process(result(angle, HitResult.Miss));
            display.Process(result(new SticksSliderTail(), HitResult.IgnoreMiss));
            display.Process(result(new SticksSliderRepeat(), HitResult.LargeTickMiss));

            Assert.Multiple(() =>
            {
                Assert.That(display.LastResult, Is.Null);
                Assert.That(display.Alpha, Is.Zero);
            });
        }

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

        private static (HitResult Timing, HitResult Angle) resultsFor(SticksFlick hitObject, SticksInputTracker.FlickEvent flick)
        {
            HitResult timing = hitObject.HitWindows.ResultFor(flick.Time - hitObject.StartTime);
            HitResult angle = hitObject.ResultForCurrentAngleError(SticksHitObject.DeltaAngle(flick.Angle, hitObject.Angle));
            return SticksHitObject.ResolveComponentResults(timing, angle);
        }

        private static SticksScoreProcessor score(SticksFlick hitObject, HitResult timing, HitResult angle)
        {
            var processor = new SticksScoreProcessor(new SticksRuleset());
            processor.ApplyResult(result(hitObject, timing));
            processor.ApplyResult(result((SticksAngleComponent)hitObject.NestedHitObjects.Single(), angle));
            return processor;
        }

        private static SticksFlick createFlick(double startTime, StickSide side, float angle)
        {
            var flick = new SticksFlick
            {
                StartTime = startTime,
                Side = side,
                Angle = angle,
            };

            flick.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return flick;
        }
    }
}
