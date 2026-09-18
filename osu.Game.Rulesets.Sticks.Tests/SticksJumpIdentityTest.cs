using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksJumpIdentityTest
    {
        [TestCase(125)]
        [TestCase(250)]
        [TestCase(260)]
        public void TestFastDriftingTwoClusterJumpsKeepEveryFlick(double interval)
        {
            // Alternating large jumps must not become a pair of almost stationary
            // sliders merely because each cluster drifts slightly between visits.
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, interval, Enumerable.Range(0, 8).Select(index =>
                position(index % 2 == 0 ? 5 + index * 0.75f : 185 - index * 0.6f)).ToArray());

            assertPreservedHeads(source, convert(source));
        }

        [Test]
        public void TestVariableSquareJumpsKeepTheirSourceDirections()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 200, new[]
            {
                new Vector2(70, 60), new Vector2(445, 70), new Vector2(410, 315), new Vector2(105, 285),
                new Vector2(420, 55), new Vector2(68, 316), new Vector2(330, 30), new Vector2(200, 335),
            });

            assertPreservedHeads(source, convert(source));
        }

        [Test]
        public void TestLargeRadialJumpsAreRecognisedEvenWhenTheirAnglesAreIdentical()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 250, Enumerable.Range(0, 8).Select(index =>
                SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(index % 2 == 0 ? 55 : 205, 0)).ToArray());

            assertPreservedHeads(source, convert(source));
        }

        [TestCase(-70, -55)]
        [TestCase(0, 0)]
        [TestCase(70, 55)]
        public void TestJumpRecognitionDoesNotDependOnBeingCentredOnThePlayfield(float x, float y)
        {
            Beatmap<HitObject> source = createBeatmap();
            var translation = new Vector2(x, y);
            addPhrase(source, 0, 250, Enumerable.Range(0, 8).Select(index =>
                (index % 2 == 0 ? new Vector2(130, 130) : new Vector2(360, 270))
                + new Vector2(index * 2, -index) + translation).ToArray());

            assertPreservedHeads(source, convert(source));
        }

        [Test]
        public void TestSeparatedJumpSectionsKeepTheirHeadsAndIndividualHitsounds()
        {
            Beatmap<HitObject> source = createBeatmap();
            for (int phrase = 0; phrase < 3; phrase++)
            {
                addPhrase(source, phrase * 4000, 250, Enumerable.Range(0, 8).Select(index =>
                    position(phrase * 27 + (index % 2 == 0 ? index : 180 - index))).ToArray());
            }

            assertPreservedHeads(source, convert(source));
        }

        [Test]
        public void TestParityKeepsProtectedJumpTimingTypesAndHands()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 250, Enumerable.Range(0, 8).Select(index =>
                position(index % 2 == 0 ? index * 2 : 180 - index)).ToArray());

            SticksHitObject[] baseline = convert(source);
            SticksHitObject[] parity = convert(source, SticksConversionMode.ParityDuet);

            assertPreservedHeads(source, baseline);
            assertPreservedHeads(source, parity, checkAngles: false);
            Assert.That(parity.Select(note => (note.StartTime, note.Side)),
                Is.EqualTo(baseline.Select(note => (note.StartTime, note.Side))));
        }

        [Test]
        public void TestSlowMovingInterleavedVoicesStillProduceTwoSliders()
        {
            Beatmap<HitObject> source = createBeatmap(600);
            // Keep the phrase clear of the inherited bar-start hold pass.
            source.HitObjects.Add(new PositionedDuration { StartTime = 300, Duration = 10, Position = position(270) });
            addPhrase(source, 600, 300, new[] { 0f, 180, 30, 150, 60, 120 }.Select(angle => position(angle)).ToArray());

            SticksSlider[] voices = convert(source).OfType<SticksSlider>().Where(note => note.StartTime >= 600).ToArray();

            Assert.That(voices, Has.Length.EqualTo(2));
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(voices.Select(note => note.StartTime), Is.EqualTo(new[] { 600d, 900 }));
                Assert.That(voices.Select(note => note.EndTime), Is.EqualTo(new[] { 1800d, 2100 }));
                Assert.That(voices[0].SegmentArcAngles, Is.EqualTo(new[] { 30f, 30 }).Within(0.001));
                Assert.That(voices[1].SegmentArcAngles, Is.EqualTo(new[] { -30f, -30 }).Within(0.001));
            });
        }

        [Test]
        public void TestCompactFastFlowRemainsEligibleForSustainedPatterns()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 250, Enumerable.Range(0, 12).Select(index => position(index * 3)).ToArray());

            Assert.That(convert(source).Any(note => note is SticksHold or SticksSlider), Is.True,
                "Protecting large jumps must not disable all circle-to-sustain conversion.");
        }

        [Test]
        public void TestTempoChangeDoesNotJoinShortFragmentsIntoAProtectedJumpRun()
        {
            Beatmap<HitObject> source = createBeatmap();
            source.ControlPointInfo.Add(375, new TimingControlPoint { BeatLength = 400 });
            addPhrase(source, 0, 125, Enumerable.Range(0, 6).Select(index => position(index % 2 * 180)).ToArray());

            assertPreservedHeads(source, convert(source));
        }

        [Test]
        public void TestSourceSliderSeparatesShortCircleFragments()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 240, new[] { position(0), position(180), position(0) });
            source.HitObjects.Add(new PositionedDuration { StartTime = 600, Duration = 10, Position = position(270) });
            addPhrase(source, 720, 240, new[] { position(180), position(0), position(180) });

            SticksHitObject[] converted = convert(source);
            HitObject[] circles = source.HitObjects.Where(note => note is not IHasDuration).ToArray();
            Assert.That(converted, Has.Length.EqualTo(source.HitObjects.Count));
            foreach (HitObject circle in circles)
            {
                SticksHitObject flick = converted.Single(note => note.StartTime == circle.StartTime);
                Assert.That(flick, Is.TypeOf<SticksFlick>());
                Assert.That(flick.Angle, Is.EqualTo(sourceAngle((IHasPosition)circle)).Within(0.001));
            }
            Assert.That(converted.OfType<SticksSlider>().Single().StartTime, Is.EqualTo(600));
        }

        [Test]
        public void TestHistoricalConversionAndRepeatedModeChangesRemainIndependent()
        {
            Beatmap<HitObject> source = createBeatmap();
            addPhrase(source, 0, 250, Enumerable.Range(0, 8).Select(index =>
                position(index % 2 == 0 ? index : 180 - index)).ToArray());
            var converter = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.Standard,
                UseCounterpoint = false,
            };
            SticksHitObject[] historical = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            converter.ConversionMode = SticksConversionMode.Duet;
            SticksHitObject[] current = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            converter.ConversionMode = SticksConversionMode.Standard;
            SticksHitObject[] historicalAgain = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            converter.ConversionMode = SticksConversionMode.Duet;
            SticksHitObject[] currentAgain = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(historical.Any(note => note is SticksHold or SticksSlider), Is.True,
                    "This fixture must exercise the former circle-to-sustain substitution.");
                Assert.That(signature(historicalAgain), Is.EqualTo(signature(historical)));
                Assert.That(signature(currentAgain), Is.EqualTo(signature(current)));
            });
            assertPreservedHeads(source, current);
        }

        private static void assertPreservedHeads(Beatmap<HitObject> source, SticksHitObject[] converted, bool checkAngles = true)
        {
            Assert.That(converted, Has.Length.EqualTo(source.HitObjects.Count));
            Assert.That(converted, Has.All.TypeOf<SticksFlick>(), "Jump pulses should remain active flicks rather than passive slider checkpoints.");
            Assert.That(converted.Select(note => note.StartTime), Is.EqualTo(source.HitObjects.Select(note => note.StartTime)));
            for (int index = 0; index < source.HitObjects.Count; index++)
            {
                HitObject original = source.HitObjects[index];
                SticksHitObject note = converted[index];
                NUnitCompatibility.Multiple(() =>
                {
                    if (checkAngles)
                        Assert.That(Math.Abs(SticksHitObject.DeltaAngle(note.Angle, sourceAngle((IHasPosition)original))),
                            Is.LessThan(0.001), $"Direction changed at {original.StartTime}ms.");
                    Assert.That(note.Samples.Select(sample => (sample.Name, sample.Volume)),
                        Is.EqualTo(original.Samples.Select(sample => (sample.Name, sample.Volume))));
                    if (index > 0 && original.StartTime - source.HitObjects[index - 1].StartTime <= SticksBeatmapConverter.RAPID_ALTERNATION_THRESHOLD)
                        Assert.That(note.Side, Is.Not.EqualTo(converted[index - 1].Side), "Fast jumps need playable alternating hands.");
                });
            }
        }

        private static Beatmap<HitObject> createBeatmap(double beatLength = 500)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.CircleSize = 5;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beatLength });
            return source;
        }

        private static void addPhrase(Beatmap<HitObject> source, double start, double interval, Vector2[] positions)
        {
            for (int index = 0; index < positions.Length; index++)
            {
                source.HitObjects.Add(new PositionedCircle
                {
                    StartTime = start + index * interval,
                    Position = positions[index],
                    Samples = new[] { new HitSampleInfo(index % 3 == 0 ? HitSampleInfo.HIT_CLAP : HitSampleInfo.HIT_NORMAL, volume: 30 + source.HitObjects.Count) },
                });
            }
        }

        // Exercise jump protection and eligible sustain patterns with the full arrangement.
        // These short synthetic phrases must not pass/fail because of the beginner allowance;
        // its source-star ramp is covered by SticksBeginnerConversionTest.
        private static SticksHitObject[] convert(Beatmap<HitObject> source, SticksConversionMode mode = SticksConversionMode.Duet) =>
            new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = mode, LimitBeginnerCoordination = false }
                .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static string[] signature(SticksHitObject[] notes) => notes.Select(note =>
            $"{note.GetType().Name}:{note.StartTime}:{note.Side}:{note.Angle}:{(note is IHasDuration duration ? duration.Duration : 0)}").ToArray();

        private static float sourceAngle(IHasPosition source)
        {
            Vector2 offset = source.Position - SticksBeatmapConverter.STANDARD_CENTRE;
            return SticksHitObject.NormaliseAngle(MathF.Atan2(offset.Y, offset.X) * 180 / MathF.PI);
        }

        private static Vector2 position(float angle)
        {
            float radians = angle * MathF.PI / 180;
            return SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 165;
        }

        private class PositionedCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }

            public float X
            {
                get => Position.X;
                set => Position = new Vector2(value, Y);
            }

            public float Y
            {
                get => Position.Y;
                set => Position = new Vector2(X, value);
            }
        }

        private sealed class PositionedDuration : PositionedCircle, IHasDuration
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
        }
    }
}
