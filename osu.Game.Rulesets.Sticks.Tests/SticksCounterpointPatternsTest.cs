using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SticksCounterpointPatternsTest
    {
        [Test]
        public void TestExperimentPreservesEveryExistingHeadAndTrajectory()
        {
            Beatmap<HitObject> source = longSliderMap(3);
            for (int i = 0; i < 24; i++)
            {
                source.HitObjects.Add(new SourceCircle
                {
                    StartTime = 12000 + i * 250,
                    Position = position(i % 2 == 0 ? 20 : 200),
                    Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 35 + i) },
                });
            }

            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);

            Assert.That(experimental.Length, Is.GreaterThan(baseline.Length), "Exercise an actual addition, not a no-op conversion.");
            assertBaselinePreserved(baseline, experimental);
            assertNoSameHandOverlap(experimental);
        }

        [TestCase(200)]
        [TestCase(225)]
        [TestCase(250)]
        [TestCase(260)]
        public void TestProtectedJumpRunsKeepTheirHandsAndAttacksAtHighDifficulty(double interval)
        {
            Beatmap<HitObject> source = map(9);
            for (int i = 0; i < 32; i++)
                source.HitObjects.Add(new SourceCircle { StartTime = 1000 + i * interval, Position = position(i % 2 == 0 ? 0 : 180) });

            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);

            Assert.Multiple(() =>
            {
                Assert.That(baseline, Has.Length.EqualTo(source.HitObjects.Count));
                Assert.That(baseline, Has.All.TypeOf<SticksFlick>());
                Assert.That(experimental.Select(note => signature(note, true)), Is.EqualTo(baseline.Select(note => signature(note, true))));
            });
        }

        [Test]
        public void TestDelayedVoicePreservesSignedMovementBeyondHalfATurn()
        {
            Beatmap<HitObject> source = longSliderMap();
            SticksSlider primary = convert(source, false).OfType<SticksSlider>().Single();
            SticksHitObject[] experimental = convert(source, true);
            SticksSlider[] echoes = experimental.OfType<SticksSlider>().Where(note => note.StartTime > primary.StartTime).ToArray();

            Assert.That(echoes, Has.Length.EqualTo(1), "The source has a long free secondary window and should produce a delayed voice.");
            SticksSlider echo = echoes.Single();
            double expectedArc = primary.SegmentArcAngleAt(0) * echo.Duration / primary.Duration;
            Assert.Multiple(() =>
            {
                Assert.That(Math.Abs(expectedArc), Is.GreaterThan(180), "This fixture must catch accidental shortest-angle wrapping.");
                Assert.That(echo.Side, Is.Not.EqualTo(primary.Side));
                Assert.That(echo.EndTime, Is.EqualTo(primary.EndTime).Within(0.001));
                Assert.That(echo.SegmentCount, Is.EqualTo(1));
                Assert.That(echo.SegmentArcAngleAt(0), Is.EqualTo(expectedArc).Within(0.001));
            });
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestDelayedVoiceKeepsReversalTimesAndAuthoredNodeSounds()
        {
            Beatmap<HitObject> source = longSliderMap(3);
            SourceSlider original = source.HitObjects.OfType<SourceSlider>().Single();
            original.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 69) };
            string[] names = { HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_CLAP, HitSampleInfo.HIT_WHISTLE, HitSampleInfo.HIT_FINISH, HitSampleInfo.HIT_NORMAL };
            for (int i = 0; i < names.Length; i++)
                original.NodeSamples.Add(new[] { new HitSampleInfo(names[i], volume: 11 * (i + 1)) });

            SticksSlider primary = convert(source, false).OfType<SticksSlider>().Single();
            SticksHitObject[] experimental = convert(source, true);
            SticksSlider echo = experimental.OfType<SticksSlider>().Single(note => note.StartTime > primary.StartTime);
            double span = original.Duration / (original.RepeatCount + 1);
            int startingNode = (int)Math.Round((echo.StartTime - original.StartTime) / span);

            Assert.Multiple(() =>
            {
                Assert.That(startingNode, Is.GreaterThan(0));
                Assert.That(echo.SegmentCount, Is.GreaterThanOrEqualTo(2), "Exercise an interior reversal, not just a trailing segment.");
                Assert.That(echo.StartTime, Is.EqualTo(original.StartTime + startingNode * span).Within(0.001));
                Assert.That(echo.EndTime, Is.EqualTo(original.EndTime).Within(0.001));
                Assert.That(soundSignature(echo.Samples), Is.EqualTo(soundSignature(original.NodeSamples[startingNode])));
            });

            for (int i = 0; i < echo.SegmentCount; i++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(echo.SegmentStartTimeAt(i), Is.EqualTo(primary.SegmentStartTimeAt(startingNode + i)).Within(0.001));
                    Assert.That(echo.SegmentDurationAt(i), Is.EqualTo(primary.SegmentDurationAt(startingNode + i)).Within(0.001));
                    Assert.That(echo.SegmentArcAngleAt(i), Is.EqualTo(primary.SegmentArcAngleAt(startingNode + i)).Within(0.001));
                });
            }

            Assert.That(echo.NodeSamples.Select(soundSignature),
                Is.EqualTo(original.NodeSamples.Skip(startingNode).Select(soundSignature)));
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestTickEntryDoesNotRepeatAHeadOnlyHeavyHitsound()
        {
            Beatmap<HitObject> source = longSliderMap();
            SourceSlider original = source.HitObjects.OfType<SourceSlider>().Single();
            original.Samples = new[]
            {
                new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 67),
                new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 67),
            };
            original.NodeSamples.Add(original.Samples);
            original.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 42) });

            SticksSlider echo = convert(source, true).OfType<SticksSlider>().Single(note => note.StartTime > original.StartTime);

            Assert.Multiple(() =>
            {
                Assert.That(echo.StartTime, Is.LessThan(original.EndTime));
                Assert.That(echo.Samples.Select(sample => sample.Name), Is.EqualTo(new[] { HitSampleInfo.HIT_NORMAL }));
                Assert.That(soundSignature(echo.NodeSamples.Last()), Is.EqualTo(soundSignature(original.NodeSamples.Last())));
            });
        }

        [TestCase(2)]
        [TestCase(4)]
        public void TestLowDifficultyOnlyAddsASpacedAccentedReleaseAnswer(double overallDifficulty)
        {
            Beatmap<HitObject> source = longSliderMap(3, overallDifficulty);
            SourceSlider original = source.HitObjects.OfType<SourceSlider>().Single();
            for (int i = 0; i <= original.RepeatCount + 1; i++)
                original.NodeSamples.Add(new[] { new HitSampleInfo(i == original.RepeatCount + 1 ? HitSampleInfo.HIT_CLAP : HitSampleInfo.HIT_NORMAL) });
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);
            SticksHitObject[] additions = addedObjects(baseline, experimental);

            Assert.That(additions, Is.Not.Empty, "The easy release opportunity should remain available.");
            Assert.Multiple(() =>
            {
                Assert.That(additions, Has.All.TypeOf<SticksFlick>());
                Assert.That(additions.Select(note => note.StartTime), Has.All.EqualTo(original.EndTime));
                Assert.That(experimental.OfType<SticksSlider>().Count(), Is.EqualTo(baseline.OfType<SticksSlider>().Count()));
            });
            assertBaselinePreserved(baseline, experimental);
            assertNoSameHandOverlap(experimental);
        }

        [TestCase(2)]
        [TestCase(4)]
        public void TestIsolatedUnaccentedReleaseDoesNotAutomaticallyBecomeAManualAttack(double overallDifficulty)
        {
            Beatmap<HitObject> source = longSliderMap(3, overallDifficulty);
            Assert.That(convert(source, true).Select(note => signature(note, true)),
                Is.EqualTo(convert(source, false).Select(note => signature(note, true))),
                "A release alone provides no source evidence for another manual attack.");
        }

        [Test]
        public void TestAReleaseCoincidentWithAnExistingHeadDoesNotCreateAnUnbudgetedChord()
        {
            Beatmap<HitObject> source = longSliderMap();
            SourceSlider original = source.HitObjects.OfType<SourceSlider>().Single();
            source.HitObjects.Add(new SourceCircle { StartTime = original.EndTime, Position = position(220) });
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);

            Assert.Multiple(() =>
            {
                Assert.That(baseline.Count(note => note.StartTime == original.EndTime), Is.EqualTo(1));
                Assert.That(experimental.Count(note => note.StartTime == original.EndTime), Is.EqualTo(1));
            });
            assertBaselinePreserved(baseline, experimental);
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestAdditionalDoublesAreSparseExactStacksAtSourcePhraseEdges()
        {
            Beatmap<HitObject> source = map(9);
            for (int i = 0; i < 6; i++)
                source.HitObjects.Add(new SourceCircle { StartTime = 1000 + i * 3000, Position = position(i * 53) });
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);
            var newChords = experimental.GroupBy(note => note.StartTime)
                .Where(group => group.Count() == 2 && baseline.Count(note => note.StartTime == group.Key) == 1)
                .OrderBy(group => group.Key).ToArray();

            Assert.That(newChords, Is.Not.Empty, "Exercise a new structural double.");
            foreach (var chord in newChords)
            {
                SticksHitObject[] notes = chord.ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(source.HitObjects.Select(note => note.StartTime), Does.Contain(chord.Key));
                    Assert.That(notes[0].Side, Is.Not.EqualTo(notes[1].Side));
                    Assert.That(SticksHitObject.DeltaAngle(notes[0].Angle, notes[1].Angle), Is.Zero.Within(0.001));
                });
            }
            for (int i = 1; i < newChords.Length; i++)
                Assert.That(newChords[i].Key - newChords[i - 1].Key, Is.GreaterThanOrEqualTo(8000));
            assertBaselinePreserved(baseline, experimental);
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestRepeatedConversionDoesNotAccumulateOrLeakArrangementChanges()
        {
            Beatmap<HitObject> source = longSliderMap(3);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            string[] baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => signature(note, true)).ToArray();
            converter.UseCounterpoint = true;
            string[] first = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => signature(note, true)).ToArray();
            string[] second = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => signature(note, true)).ToArray();
            converter.UseCounterpoint = false;
            string[] restored = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => signature(note, true)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(first.Length, Is.GreaterThan(baseline.Length));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(restored, Is.EqualTo(baseline));
                Assert.That(source.HitObjects, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void TestLeadPhrasesCanUseExistingDoubleAccentsWithoutReplacingTheirHeads()
        {
            Beatmap<HitObject> source = map(9);
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 375 });
            for (int i = 0; i < 64; i++)
            {
                double time = 1125 + i * 375;
                SourceCircle note = i % 7 == 0
                    ? new SourceSlider { StartTime = time, Duration = 187.5, Position = position(i * 67 % 360) }
                    : new SourceCircle { StartTime = time, Position = position(i * 67 % 360), NewCombo = i % 7 == 1 };
                if (i % 4 == 0)
                    note.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 61) };
                source.HitObjects.Add(note);
            }

            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);
            var chords = baseline.GroupBy(note => note.StartTime).Where(group => group.Count() == 2).ToArray();

            Assert.That(chords.Length, Is.GreaterThanOrEqualTo(4), "Exercise baseline punctuation inside multiple source phrases.");
            assertBaselinePreserved(baseline, experimental);
            foreach (var chord in chords)
                Assert.That(experimental.Where(note => note.StartTime == chord.Key).Select(note => signature(note, true)),
                    Is.EquivalentTo(chord.Select(note => signature(note, true))), "Existing doubles must retain both sides and directions.");

            Assert.That(baseline.OfType<SticksFlick>().Any(note => experimental.OfType<SticksFlick>().Any(candidate =>
                signature(candidate) == signature(note) && candidate.Side != note.Side)), Is.True,
                "At least one source phrase should actually be regrouped around retained baseline accents.");
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestOffbeatSourceComboCanGainSupportWithoutConsumingItsCircleAttacks()
        {
            Beatmap<HitObject> source = map(9);
            // A short native pickup leaves a real recovery gap and prevents the base
            // converter from treating the first circle as an isolated sustain arrival.
            source.HitObjects.Add(new SourceSlider { StartTime = 0, Duration = 50, Position = position(270) });
            for (int i = 0; i < 4; i++)
                source.HitObjects.Add(new SourceCircle
                {
                    StartTime = 250 + i * 500,
                    Position = position(i * 25),
                    NewCombo = i == 0,
                    Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 30 + i * 10) },
                });

            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] experimental = convert(source, true);
            SticksFlick[] originalAttacks = baseline.OfType<SticksFlick>().Where(note => note.StartTime >= 250).ToArray();
            SticksHitObject[] additions = addedObjects(baseline, experimental);

            Assert.Multiple(() =>
            {
                Assert.That(originalAttacks, Has.Length.EqualTo(4), "The base must leave this source combo articulated.");
                Assert.That(additions, Has.Length.EqualTo(1));
                Assert.That(additions.Single(), Is.TypeOf<SticksSlider>());
            });
            SticksSlider support = (SticksSlider)additions.Single();
            Assert.Multiple(() =>
            {
                Assert.That(support.StartTime, Is.EqualTo(250));
                Assert.That(support.EndTime, Is.EqualTo(1750));
                Assert.That(support.Angle, Is.EqualTo(0).Within(0.001));
                Assert.That(support.SegmentCount, Is.EqualTo(3));
            });

            for (int i = 0; i < originalAttacks.Length; i++)
            {
                SticksFlick attack = experimental.OfType<SticksFlick>().Single(note => note.StartTime == originalAttacks[i].StartTime);
                Assert.Multiple(() =>
                {
                    Assert.That(attack.Side, Is.Not.EqualTo(support.Side));
                    Assert.That(attack.Angle, Is.EqualTo(originalAttacks[i].Angle).Within(0.001));
                    Assert.That(support.AngleAt(attack.StartTime), Is.EqualTo(attack.Angle).Within(0.001));
                    Assert.That(soundSignature(attack.Samples), Is.EqualTo(soundSignature(originalAttacks[i].Samples)));
                });
                if (i < support.SegmentCount)
                {
                    Assert.That(support.SegmentDurationAt(i), Is.EqualTo(500).Within(0.001));
                    Assert.That(support.SegmentArcAngleAt(i), Is.EqualTo(25).Within(0.001));
                }
            }
            Assert.That(experimental.Count(note => note.StartTime == support.StartTime), Is.EqualTo(2), "The supporting entry is one deliberately budgeted double.");
            assertBaselinePreserved(baseline, experimental);
            assertNoSameHandOverlap(experimental);
        }

        [Test]
        public void TestStaggeredPhraseRecallsAnotherContourAndHandsOffToArticulatedSourceNotes()
        {
            Beatmap<HitObject> source = longSliderMap(3);
            SourceSlider original = (SourceSlider)source.HitObjects[0];
            for (int i = 0; i <= 4; i++)
                original.NodeSamples.Add(new[] { new HitSampleInfo(i == 1 ? HitSampleInfo.HIT_CLAP : HitSampleInfo.HIT_NORMAL, volume: 30 + i) });
            source.HitObjects.Add(new SourceSlider { StartTime = 7300, Duration = 1000, Position = position(150) });
            source.HitObjects.Add(new SourceCircle { StartTime = 8500, Position = position(180) });
            source.HitObjects.Add(new SourceCircle { StartTime = 8800, Position = position(210) });
            SticksHitObject[] baseline = convert(source, false);
            var families = new List<string>();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                UseCounterpoint = true,
                CounterpointArrangementObserved = (family, _, _, _, _) => families.Add(family),
            };
            SticksHitObject[] result = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(families, Does.Contain("StaggeredPhrase"), "Exercise a complete selected arrangement through actual conversion. Baseline: "
                + string.Join(" | ", baseline.Select(note => $"{note.GetType().Name}@{note.StartTime}-{note.GetEndTime()}:{note.Side}")));
            SticksSlider primary = result.OfType<SticksSlider>().Single(note => note.StartTime == original.StartTime);
            SticksSlider template = result.OfType<SticksSlider>().Where(note => note.StartTime == 7300)
                .OrderBy(note => Math.Abs(SticksHitObject.DeltaAngle(note.Angle, 150))).First();
            SticksSlider echo = addedObjects(baseline, result).OfType<SticksSlider>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(echo.StartTime, Is.EqualTo(2500));
                Assert.That(echo.EndTime, Is.EqualTo(7000));
                Assert.That(echo.Side, Is.Not.EqualTo(primary.Side));
                foreach (double time in new[] { 8500.0, 8800 })
                    Assert.That(result.Where(note => note.StartTime == time).Select(note => note.Side), Does.Contain(template.Side),
                        "The answering hand continues through existing accents; their other-hand partners remain present.");
                Assert.That(echo.SegmentCount, Is.EqualTo(template.SegmentCount));
                Assert.That(echo.SegmentCount, Is.Not.EqualTo(primary.SegmentCount), "The second voice must actually recall a different contour.");
                Assert.That(echo.NodeSamples.First().Select(sample => sample.Name), Does.Contain(HitSampleInfo.HIT_CLAP));
                Assert.That(soundSignature(echo.NodeSamples.Last()), Is.EqualTo(soundSignature(original.NodeSamples.Last())));
            });
            for (int i = 0; i < template.SegmentCount; i++)
                Assert.Multiple(() =>
                {
                    Assert.That(echo.SegmentArcAngleAt(i), Is.EqualTo(template.SegmentArcAngleAt(i)).Within(0.001));
                    Assert.That(echo.SegmentDurationAt(i) / echo.Duration, Is.EqualTo(template.SegmentDurationAt(i) / template.Duration).Within(0.00001));
                });
            assertBaselinePreserved(baseline, result);
            assertNoSameHandOverlap(result);
        }

        private static void assertBaselinePreserved(SticksHitObject[] baseline, SticksHitObject[] experimental)
        {
            var available = experimental.GroupBy(note => signature(note)).ToDictionary(group => group.Key, group => group.Count());
            foreach (var group in baseline.GroupBy(note => signature(note)))
                Assert.That(available.GetValueOrDefault(group.Key), Is.GreaterThanOrEqualTo(group.Count()), $"Lost or altered baseline gesture: {group.Key}");
        }

        private static SticksHitObject[] addedObjects(SticksHitObject[] baseline, SticksHitObject[] experimental)
        {
            var remaining = baseline.GroupBy(note => signature(note)).ToDictionary(group => group.Key, group => group.Count());
            var additions = new List<SticksHitObject>();
            foreach (SticksHitObject note in experimental)
            {
                string key = signature(note);
                if (remaining.GetValueOrDefault(key) > 0)
                    remaining[key]--;
                else
                    additions.Add(note);
            }
            return additions.ToArray();
        }

        private static void assertNoSameHandOverlap(IEnumerable<SticksHitObject> notes)
        {
            foreach (var lane in notes.GroupBy(note => note.Side))
            {
                double occupiedUntil = double.NegativeInfinity;
                foreach (SticksHitObject note in lane.OrderBy(note => note.StartTime))
                {
                    Assert.That(note.StartTime, Is.GreaterThanOrEqualTo(occupiedUntil), $"Overlapping {lane.Key} gesture at {note.StartTime}.");
                    occupiedUntil = Math.Max(occupiedUntil, note.GetEndTime());
                }
            }
        }

        private static string signature(SticksHitObject note, bool includeSide = false)
        {
            string path = note is SticksSlider slider
                ? string.Join(";", Enumerable.Range(0, slider.SegmentCount).Select(i =>
                    $"{number(slider.SegmentArcAngleAt(i))}/{number(slider.SegmentDurationAt(i))}"))
                : string.Empty;
            return $"{note.GetType().Name}:{number(note.StartTime)}:{number(note.GetEndTime())}:{number(note.Angle)}:{(includeSide ? note.Side.ToString() : string.Empty)}:{path}:{soundSignature(note.Samples)}";
        }

        private static string number(double value) => Math.Round(value, 4).ToString(CultureInfo.InvariantCulture);
        private static string soundSignature(IEnumerable<HitSampleInfo> samples) => string.Join(";", samples.Select(sample => $"{sample.Name}:{sample.Volume}"));
        private static SticksHitObject[] convert(Beatmap<HitObject> source, bool counterpoint) => new SticksBeatmapConverter(source, new SticksRuleset())
        {
            UseCounterpoint = counterpoint,
        }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static Beatmap<HitObject> longSliderMap(int repeats = 0, double overallDifficulty = 9)
        {
            Beatmap<HitObject> source = map(overallDifficulty);
            // Twelve beats prevents existing eight-beat accompaniment templates from occupying
            // the secondary voice, exposing the new planner's actual choices to the assertions.
            source.HitObjects.Add(new SourceSlider { StartTime = 1000, Duration = 6000, RepeatCount = repeats, Position = position(20) });
            return source;
        }

        private static Beatmap<HitObject> map(double overallDifficulty)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.OverallDifficulty = (float)overallDifficulty;
            source.Difficulty.CircleSize = 4;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return source;
        }

        private static Vector2 position(float angle)
        {
            float radians = angle * MathF.PI / 180;
            return SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 160;
        }

        private class SourceCircle : HitObject, IHasPosition, IHasCombo
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
            public bool NewCombo { get; set; }
            public int ComboOffset { get; set; }
        }

        private sealed class SourceSlider : SourceCircle, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
