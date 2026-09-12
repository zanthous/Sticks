using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Database;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksClickTest
    {
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void EachButtonRequiresFreshPress(bool shoulder, bool trigger, bool stick)
        {
            var input = new SticksClickInput();
            Assert.That(input.Update(shoulder, trigger, stick, false), Is.False, "A pre-held button must not hit.");
            Assert.That(input.Update(false, false, false, false), Is.False);
            Assert.That(input.Update(shoulder, trigger, stick, false), Is.True);
            Assert.That(input.Update(shoulder, trigger, stick, false), Is.False, "Holding must not repeat.");
        }

        [Test]
        public void StrumAcceptsOnlyStickButtonAndKeepsIndependentEdges()
        {
            var input = new SticksClickInput();
            input.Update(false, false, false, true);
            Assert.That(input.Update(true, false, false, true), Is.False);
            Assert.That(input.Update(true, true, false, true), Is.False);
            Assert.That(input.Update(true, true, true, true), Is.True);
            Assert.That(input.Update(true, true, true, false), Is.False, "Changing mode must not create a press.");
            input.Update(true, true, false, false);
            Assert.That(input.Update(true, true, true, false), Is.True, "Held L1/L2 must not swallow L3.");
        }

        [TestCase(0, HitResult.Perfect, 300)]
        [TestCase(75, HitResult.Good, 100)]
        [TestCase(-75, HitResult.Good, 100)]
        [TestCase(125, HitResult.Ok, 50)]
        [TestCase(160, HitResult.Miss, 0)]
        public void TimingGradesHaveFullNoteWeight(double offset, HitResult expected, int points)
        {
            var click = new SticksClick { StartTime = 1000, Angle = 237 };
            var beatmap = map(click);
            var processor = new SticksScoreProcessor(new SticksRuleset());
            processor.ApplyBeatmap(beatmap);
            HitResult grade = click.HitWindows.ResultFor(offset);
            Assert.That(grade, Is.EqualTo(expected));
            Assert.That(click.NestedHitObjects, Is.Empty, "Click notes must have no aim judgement.");
            var result = new JudgementResult(click, click.Judgement) { Type = grade };
            processor.ApplyResult(result);
            Assert.That(processor.GetBaseScoreForResult(grade), Is.EqualTo(points));
            Assert.That(processor.Accuracy.Value, Is.EqualTo(points / 300.0).Within(0.000001));
            Assert.That(processor.Combo.Value, Is.EqualTo(points > 0 ? 1 : 0));
            processor.RevertResult(result);
            Assert.That(processor.Combo.Value, Is.Zero);
        }

        [Test]
        public void MixedMapPerfectScoreNormalisesToOneMillion()
        {
            var beatmap = map(new SticksClick { StartTime = 1000 },
                new SticksFlick { StartTime = 1500 },
                new SticksHold { StartTime = 2000, Duration = 1000 },
                new SticksClick { StartTime = 2500 });
            var processor = new SticksScoreProcessor(new SticksRuleset());
            processor.ApplyBeatmap(beatmap);
            foreach (HitObject hitObject in beatmap.HitObjects.SelectMany(flatten).OrderBy(h => h.GetEndTime()))
                processor.ApplyResult(new JudgementResult(hitObject, hitObject.Judgement) { Type = hitObject.Judgement.MaxResult });
            Assert.That(processor.Accuracy.Value, Is.EqualTo(1));
            Assert.That(processor.TotalScore.Value, Is.EqualTo(1_000_000));
            var score = new ScoreInfo();
            processor.PopulateScore(score);
            Assert.That(StandardisedScoreMigrationTools.ComputeAccuracy(score, processor), Is.EqualTo(1));
        }

        [TestCase(StickSide.Left)]
        [TestCase(StickSide.Right)]
        public void ClickRoundTripsThroughAuthoredCarrier(StickSide side)
        {
            var click = new SticksClick { StartTime = 1234.5, Side = side, Angle = 219 };
            var carrier = SticksAuthoredBeatmapCodec.CreateLegacyProxy(click);
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(carrier, out SticksHitObject restored), Is.True);
            Assert.That(restored, Is.TypeOf<SticksClick>());
            Assert.That(restored.Side, Is.EqualTo(side));
            Assert.That(restored.StartTime, Is.EqualTo(click.StartTime));
            click.EnsureLegacyEditorMarker();
            click.Side = side == StickSide.Left ? StickSide.Right : StickSide.Left;
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(click, out restored), Is.True);
            Assert.That(restored.Side, Is.EqualTo(click.Side));
        }

        [Test]
        public void ReplayStoresBothStickButtons()
        {
            using var storage = new TemporaryNativeStorage("sticks-click-replay");
            var store = new SticksReplayStore(storage);
            var score = new Score();
            score.Replay.Frames.Add(new SticksReplayFrame(0, Vector2.Zero, Vector2.Zero));
            score.Replay.Frames.Add(new SticksReplayFrame(1000, Vector2.UnitX, -Vector2.UnitX,
                leftTrigger: true, rightShoulder: true, leftStickButton: true, rightStickButton: true));
            Assert.That(store.Save(score), Is.True);
            var restored = new Score { ScoreInfo = score.ScoreInfo };
            Assert.That(store.TryRestore(restored), Is.True);
            for (int i = 0; i < score.Replay.Frames.Count; i++)
                Assert.That(restored.Replay.Frames[i].IsEquivalentTo(score.Replay.Frames[i]), Is.True);
            var provider = new SticksReplayInputProvider();
            provider.Update(Vector2.Zero, Vector2.Zero, leftStickButton: true, rightStickButton: true);
            Assert.That(provider.SnapshotWithAllButtons().LeftStickButton, Is.True);
            provider.Deactivate();
            Assert.That(provider.SnapshotWithAllButtons().RightStickButton, Is.False);
        }

        [Test]
        public void AutoplayClicksDoNotInterruptSameHandHold()
        {
            var beatmap = map(new SticksHold { StartTime = 1000, Duration = 2000, Angle = 90 },
                new SticksClick { StartTime = 1500 },
                new SticksClick { StartTime = 1500, Side = StickSide.Right },
                new SticksClick { StartTime = 1700 });
            var frames = new SticksAutoGenerator(beatmap).Generate().Frames.Cast<SticksReplayFrame>().ToArray();
            foreach (var frame in frames.Where(frame => frame.Time >= 1499 && frame.Time <= 1701))
                Assert.That(frame.LeftStick.Y, Is.EqualTo(1).Within(0.00001));
            Assert.That(frames.Single(frame => frame.Time == 1500).LeftStickButton, Is.True);
            Assert.That(frames.Single(frame => frame.Time == 1500).RightStickButton, Is.True);
            Assert.That(frames.Single(frame => frame.Time == 1501).LeftStickButton, Is.False);
            Assert.That(frames.Single(frame => frame.Time == 1700).LeftStickButton, Is.True);
        }

        [Test]
        public void ClickDifficultyIgnoresAngleAndCircleSize()
        {
            var clicks = Enumerable.Range(0, 20).Select(i => new SticksClick { StartTime = 1000 + i * 250 }).ToArray();
            var beatmap = map(clicks);
            var first = SticksDifficultyModel.CalculateOrdered(clicks, 1, 5);
            for (int i = 0; i < clicks.Length; i++)
            {
                clicks[i].Angle = i * 37;
                clicks[i].ApplyDefaults(beatmap.ControlPointInfo, new BeatmapDifficulty { CircleSize = 10, OverallDifficulty = 5 });
            }
            Assert.That(SticksDifficultyModel.CalculateOrdered(clicks, 1, 5), Is.EqualTo(first));
        }

        private static Beatmap<SticksHitObject> map(params SticksHitObject[] objects)
        {
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5, CircleSize = 4, SliderTickRate = 1 };
            var beatmap = new Beatmap<SticksHitObject> { BeatmapInfo = new BeatmapInfo(new SticksRuleset().RulesetInfo, difficulty) };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.AddRange(objects);
            foreach (var hitObject in objects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
            return beatmap;
        }

        private static IEnumerable<HitObject> flatten(HitObject hitObject)
        {
            yield return hitObject;
            foreach (var nested in hitObject.NestedHitObjects.SelectMany(flatten))
                yield return nested;
        }
    }
}
