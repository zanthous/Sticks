using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksEncoreConversionTest
    {
        [Test]
        public void EncoreIsRegisteredUnrankedAndIndependentOfOtherConversionMods()
        {
            Mod[] mods = new SticksRuleset().GetModsFor(ModType.Conversion).ToArray();
            SticksModEncore encore = mods.OfType<SticksModEncore>().Single();
            Assert.That(encore.Ranked, Is.False);
            Assert.That(encore.Type, Is.EqualTo(ModType.Conversion));
            foreach (Mod mod in mods.Where(mod => mod != encore))
            {
                Assert.That(encore.IncompatibleMods, Does.Not.Contain(mod.GetType()));
                Assert.That(mod.IncompatibleMods, Does.Not.Contain(typeof(SticksModEncore)));
            }
        }

        [TestCase(SticksConversionMode.Duet)]
        [TestCase(SticksConversionMode.ParityDuet)]
        public void GameplayPipelineAddsClicksOnlyWhenEnabled(SticksConversionMode mode)
        {
            Beatmap<HitObject> source = accentedPhrase();
            source.HitObjects.Add(spinner(9000, 2000));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = mode };
            SticksHitObject[] original = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(original.OfType<SticksClick>(), Is.Empty);

            var mods = new List<Mod> { new SticksModEncore(), new SticksModDifficultyAdjust() };
            if (mode == SticksConversionMode.ParityDuet)
                mods.Add(new SticksModParity());
            var working = new FlatWorkingBeatmap(source);
            SticksHitObject[] converted = working.GetPlayableBeatmap(new SticksRuleset().RulesetInfo,
                mods.ToArray(), CancellationToken.None).HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.That(converted.OfType<SticksClick>(), Is.Not.Empty);
            Assert.That(converted.OfType<SticksSlice>(), Is.Empty);
            SticksSlider sourceSpinnerHold = converted.OfType<SticksSlider>().Single(hold => hold.IsStationary && hold.StartTime == 9000);
            Assert.That(sourceSpinnerHold.EndTime, Is.EqualTo(11000));
            Assert.That(signature(converted.OfType<SticksSlider>().Where(slider => slider.IsStationary)), Is.EqualTo(signature(original.OfType<SticksSlider>().Where(slider => slider.IsStationary))));
        }

        [TestCase(60)]
        [TestCase(100)]
        [TestCase(125)]
        [TestCase(150)]
        public void EncoreKeepsFastConnectedMovementsAsFlicksWhileSlicesAreDisabled(double interval)
        {
            var source = map(Enumerable.Range(0, 32).Select(i => (HitObject)circle(1000 + i * interval, i * 20, false)).ToArray());
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            var normal = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(normal.OfType<SticksSlice>(), Is.Empty);
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            var encore = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(encore.OfType<SticksSlice>(), Is.Empty);
            Assert.That(signature(encore), Is.EqualTo(signature(normal)));
        }

        [Test]
        public void AlternatingTripletBecomesAConnectedSliceRunWithRecoveryAtItsExit()
        {
            var notes = new List<SticksHitObject>
            {
                new SticksFlick { StartTime = 1000, Angle = 0 },
                new SticksFlick { StartTime = 1100, Angle = 25, Side = StickSide.Right },
                new SticksFlick { StartTime = 1200, Angle = 50 },
                new SticksFlick { StartTime = 1300, Angle = 150, Side = StickSide.Right },
                new SticksFlick { StartTime = 1400, Angle = 200 },
            };
            var geometry = notes.Select(note => (note.StartTime, note.Angle, note.Samples)).ToArray();
            SticksBeatmapConverter.AddEncoreSlices(notes, map(), CancellationToken.None);
            Assert.That(notes.OfType<SticksSlice>().Select(note => (note.StartTime, note.Side)),
                Is.EqualTo(new[] { (1100d, StickSide.Left), (1200d, StickSide.Left) }));
            Assert.That(notes.Select(note => (note.StartTime, note.Angle, note.Samples)), Is.EqualTo(geometry));
            Assert.That(notes[0], Is.TypeOf<SticksFlick>());
            Assert.That(notes.Skip(3), Has.All.TypeOf<SticksFlick>());
            Assert.That(notes[3].Side, Is.EqualTo(StickSide.Right));
            Assert.That(notes[4].Side, Is.EqualTo(StickSide.Left));
        }

        [Test]
        public void SliceReassignmentCannotCrowdTheNextFlickOrSplitADouble()
        {
            var notes = new List<SticksHitObject>
            {
                new SticksFlick { StartTime = 1000, Angle = 0 },
                new SticksFlick { StartTime = 1100, Angle = 30, Side = StickSide.Right },
                new SticksFlick { StartTime = 1200, Angle = 170 },
                new SticksFlick { StartTime = 3000, Angle = 0 },
                new SticksFlick { StartTime = 3100, Angle = 30, Side = StickSide.Right },
                new SticksFlick { StartTime = 3200, Angle = 50 },
                new SticksFlick { StartTime = 3200, Angle = 50, Side = StickSide.Right },
            };
            string[] baseline = signature(notes);
            SticksBeatmapConverter.AddEncoreSlices(notes, map(), CancellationToken.None);
            Assert.That(signature(notes), Is.EqualTo(baseline));
        }

        [Test]
        public void SliceCannotBorrowAStickFromAnUnfinishedSlider()
        {
            var notes = new List<SticksHitObject>
            {
                new SticksSlider { StartTime = 1000, Duration = 1000, Angle = 0, ArcAngle = 90 },
                new SticksFlick { StartTime = 1100, Angle = 30, Side = StickSide.Right },
            };
            SticksBeatmapConverter.AddEncoreSlices(notes, map(), CancellationToken.None);
            Assert.That(notes[1], Is.TypeOf<SticksFlick>());
            Assert.That(notes[1].Side, Is.EqualTo(StickSide.Right));
        }

        [TestCase(50, 500, true)]
        [TestCase(100, 500, true)]
        [TestCase(150, 500, true)]
        [TestCase(151, 500, false)]
        [TestCase(400, 500, false)]
        [TestCase(100, 200, true)]
        [TestCase(125, 200, false)]
        public void SliceFollowUpsMustBeFastInRealTimeAndAtTheLocalTempo(double gap, double beatLength, bool eligible)
        {
            var source = map();
            source.ControlPointInfo.Add(1050, new TimingControlPoint { BeatLength = beatLength });
            var notes = new List<SticksHitObject>
            {
                new SticksFlick { StartTime = 1000, Angle = 0 },
                new SticksFlick { StartTime = 1000 + gap, Angle = 30 },
            };
            SticksBeatmapConverter.AddEncoreSlices(notes, source, CancellationToken.None);
            Assert.That(notes[0], Is.TypeOf<SticksFlick>());
            Assert.That(notes[1] is SticksSlice, Is.EqualTo(eligible));
        }

        [Test]
        public void SliceFollowUpsRestartAtSourcePhraseBoundariesForBothHands()
        {
            var opening = circle(1075, 20);
            opening.NewCombo = true;
            var source = map(circle(1000, 0), opening, circle(1125, 30), circle(1200, 40), circle(1250, 50));
            var notes = new List<SticksHitObject>
            {
                new SticksFlick { StartTime = 1000, Angle = 0 },
                new SticksFlick { StartTime = 1075, Angle = 20, Side = StickSide.Right },
                new SticksFlick { StartTime = 1125, Angle = 30 },
                new SticksFlick { StartTime = 1200, Angle = 40, Side = StickSide.Right },
                new SticksFlick { StartTime = 1250, Angle = 50 },
            };
            SticksBeatmapConverter.AddEncoreSlices(notes, source, CancellationToken.None);
            Assert.That(notes.Take(2), Has.All.TypeOf<SticksFlick>(), "The new phrase must start with an aimed note.");
            Assert.That(notes.OfType<SticksSlice>().Select(note => (note.StartTime, note.Side)),
                Is.EqualTo(new[] { (1125d, StickSide.Right), (1200d, StickSide.Right) }),
                "The run must follow the new phrase's right-hand opening, not the preceding left-hand phrase.");
        }

        [Test]
        public void SliceFollowUpsCannotStartAfterRestsBreaksOrClicks()
        {
            var source = map();
            source.Breaks.Add(new BreakPeriod(3000, 3100));
            var notes = new List<SticksHitObject>
            {
                new SticksFlick { StartTime = 1000 },
                new SticksFlick { StartTime = 2000, Angle = 30 },
                new SticksFlick { StartTime = 2050, Angle = 50 },
                new SticksFlick { StartTime = 2980 },
                new SticksFlick { StartTime = 3120, Angle = 30 },
                new SticksFlick { StartTime = 4000 },
                new SticksClick { StartTime = 4050 },
                new SticksFlick { StartTime = 4100, Angle = 30 },
            };
            SticksBeatmapConverter.AddEncoreSlices(notes, source, CancellationToken.None);
            Assert.That(notes.OfType<SticksSlice>().Select(note => note.StartTime), Is.EqualTo(new[] { 2050d }));
        }

        [TestCase(1990, false)]
        [TestCase(2000, false)]
        [TestCase(2050, true)]
        [TestCase(2200, false)]
        public void SliceAfterASliderUsesItsTailTimeAndAngle(double time, bool eligible)
        {
            var notes = new List<SticksHitObject>
            {
                new SticksSlider { StartTime = 1000, Duration = 1000, Angle = 0, ArcAngle = 90 },
                new SticksFlick { StartTime = time, Angle = 120 },
            };
            SticksBeatmapConverter.AddEncoreSlices(notes, map(), CancellationToken.None);
            Assert.That(notes[1] is SticksSlice, Is.EqualTo(eligible));
        }

        [Test]
        public void ReusingConverterDoesNotLeakNewObjectsOrMutateSource()
        {
            Beatmap<HitObject> source = accentedPhrase();
            source.HitObjects.Add(spinner(9000, 2000));
            HitObject[] originalObjects = source.HitObjects.ToArray();
            string[] originalSamples = source.HitObjects.Select(hitObject => string.Join(',', hitObject.Samples.Select(sample => sample.Name))).ToArray();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.ParityDuet };
            string[] baseline = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            string[] enabled = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(enabled));
            converter.AddClickNotes = false;
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(baseline));
            converter.AddClickNotes = true;
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(enabled));
            Assert.That(source.HitObjects, Is.EqualTo(originalObjects));
            Assert.That(source.HitObjects.Select(hitObject => string.Join(',', hitObject.Samples.Select(sample => sample.Name))), Is.EqualTo(originalSamples));
        }

        [TestCase(SticksConversionMode.Standard)]
        [TestCase(SticksConversionMode.Parity)]
        [TestCase(SticksConversionMode.Duet)]
        [TestCase(SticksConversionMode.ParityDuet)]
        public void SourceSpinnersKeepOrdinaryConversionAndDoNotTruncateOtherDurations(SticksConversionMode mode)
        {
            Beatmap<HitObject> source = map(new SourceSlider { StartTime = 500, Duration = 3000, Position = position(45) },
                spinner(1000, 2000), spinner(2000, 2000), spinner(4000, 1000), spinner(6000, 100));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = mode };
            SticksHitObject[] baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.That(signature(converted), Is.EqualTo(signature(baseline)));
            Assert.That(converted.OfType<SticksSlider>().Where(slider => slider.IsStationary), Is.Not.Empty);
            Assert.That(converted.OfType<SticksClick>(), Is.Empty);
        }

        [Test]
        public void ClickAccentsStaySelectivePreserveTimesAndUseBothHands()
        {
            Beatmap<HitObject> source = accentedPhrase();
            SticksClick[] clicks = convert(source).OfType<SticksClick>().ToArray();
            Assert.That(clicks.Length, Is.InRange(2, source.HitObjects.Count / 4));
            Assert.That(clicks.Select(click => click.Side).Distinct().Count(), Is.EqualTo(2));
            foreach (SticksClick click in clicks)
            {
                HitObject original = source.HitObjects.Single(hitObject => hitObject.StartTime == click.StartTime);
                Assert.That(original.Samples.Any(sample => sample.Name == HitSampleInfo.HIT_CLAP), Is.True);
                Assert.That(click.Samples.Select(sample => sample.Name), Is.EqualTo(original.Samples.Select(sample => sample.Name)));
            }
            for (int index = 1; index < clicks.Length; index++)
                Assert.That(clicks[index].StartTime - clicks[index - 1].StartTime, Is.GreaterThanOrEqualTo(5000));
        }

        [TestCase(2)]
        [TestCase(8)]
        public void AccentsInsideTwoHoldsRemainPartOfTheSustainedPattern(float overallDifficulty)
        {
            Beatmap<HitObject> source = map(new SourceHold { StartTime = 0, Duration = 4000, Position = position(0) },
                new SourceHold { StartTime = 0, Duration = 4000, Position = position(180) },
                circle(500, 45, true), circle(1500, 60, true), circle(2500, 75, true), circle(3500, 90, true));
            source.Difficulty.OverallDifficulty = overallDifficulty;
            SticksHitObject[] converted = convert(source);
            Assert.That(converted.OfType<SticksSlider>().Where(slider => slider.IsStationary).Count(), Is.EqualTo(2));
            Assert.That(converted.OfType<SticksClick>(), Is.Empty);
        }

        [Test]
        public void EasierMapsReceiveFewerClicksWithLongerRecovery()
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 64).Select(index => (HitObject)circle(index * 1000, index * 47, true)).ToArray());
            source.Difficulty.OverallDifficulty = 2;
            SticksClick[] easy = convert(source).OfType<SticksClick>().ToArray();
            source.Difficulty.OverallDifficulty = 8;
            SticksClick[] hard = convert(source).OfType<SticksClick>().ToArray();

            Assert.That(easy, Is.Not.Empty);
            Assert.That(hard.Length, Is.GreaterThan(easy.Length));
            Assert.That(easy.Length, Is.LessThanOrEqualTo(8));
            for (int index = 1; index < easy.Length; index++)
                Assert.That(easy[index].StartTime - easy[index - 1].StartTime, Is.GreaterThanOrEqualTo(8000));
            for (int index = 1; index < hard.Length; index++)
                Assert.That(hard[index].StartTime - hard[index - 1].StartTime, Is.GreaterThanOrEqualTo(4000));
        }

        [TestCase(2)]
        [TestCase(8)]
        [TestCase(10)]
        public void DenseAccentsCannotBypassSpacingAtAnyDifficulty(float overallDifficulty)
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 48).Select(index => (HitObject)circle(index * 125, index * 47, true)).ToArray());
            source.Difficulty.OverallDifficulty = overallDifficulty;
            Assert.That(convert(source).OfType<SticksClick>(), Is.Empty);
        }

        [TestCase(250)]
        [TestCase(300)]
        public void EasierMapsDoNotInterleaveClicksIntoRegularAccents(double interval)
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 32).Select(index => (HitObject)circle(index * interval, index * 47, true)).ToArray());
            source.Difficulty.OverallDifficulty = 3;
            Assert.That(convert(source).OfType<SticksClick>(), Is.Empty);
        }

        [TestCase(171)]
        [TestCase(172)]
        public void HarderMapsAllowSpacedHalfBeatsDespiteTimestampRounding(double interval)
        {
            Beatmap<HitObject> source = map(circle(0, 47, true), circle(interval, 137, true));
            source.ControlPointInfo = new ControlPointInfo();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 60000d / 175 });
            source.Difficulty.OverallDifficulty = 7;
            SticksClick click = convert(source).OfType<SticksClick>().Single();
            Assert.That(click.StartTime, Is.EqualTo(0));

            source.Difficulty.OverallDifficulty = 3;
            Assert.That(convert(source).OfType<SticksClick>(), Is.Empty);
        }

        [TestCase(250)]
        [TestCase(500)]
        [TestCase(1500)]
        public void EasierClicksRespectBothRealTimeAndLocalBeatSpacing(double beatLength)
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 32).Select(index => (HitObject)circle(index * 1000, index * 47, true)).ToArray());
            source.ControlPointInfo = new ControlPointInfo();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beatLength });
            source.Difficulty.OverallDifficulty = 2;
            SticksHitObject[] converted = convert(source);

            foreach (SticksClick click in converted.OfType<SticksClick>())
            {
                double clearance = Math.Max(500, beatLength);
                Assert.That(source.HitObjects.Where(hitObject => hitObject.StartTime != click.StartTime)
                                  .All(hitObject => hitObject.StartTime - click.StartTime >= clearance
                                      || click.StartTime - hitObject.GetEndTime() >= clearance), Is.True);
                Assert.That(converted.Where(hitObject => hitObject != click)
                                     .All(hitObject => hitObject.StartTime - click.StartTime >= clearance
                                         || click.StartTime - hitObject.GetEndTime() >= clearance), Is.True);
            }

            if (beatLength <= 500)
                Assert.That(converted.OfType<SticksClick>(), Is.Not.Empty);
            else
                Assert.That(converted.OfType<SticksClick>(), Is.Empty, "A one-second gap is shorter than one beat at this tempo.");
        }

        [Test]
        public void IsolatedClickRetainsSourceTimingAndSamplesAfterGeneratedOpeningDouble()
        {
            Beatmap<HitObject> source = map(circle(6000, 47, true), circle(8000, 213));
            source.Difficulty.OverallDifficulty = 2;
            SticksHitObject[] converted = convert(source, beginner: false);
            SticksFlick[] opening = converted.OfType<SticksFlick>().Where(note => note.StartTime == 6000).ToArray();
            Assert.That(opening, Has.Length.EqualTo(2));
            Assert.That(opening.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2));
            Assert.That(SticksHitObject.DeltaAngle(opening[0].Angle, opening[1].Angle), Is.Zero.Within(0.001));
            SticksClick click = converted.OfType<SticksClick>().Single();
            Assert.That(click.StartTime, Is.EqualTo(8000), "Encore must keep the opening double intact and use the next eligible source note.");
            Assert.That(click.Samples.Select(sample => (sample.Name, sample.Volume)),
                Is.EqualTo(source.HitObjects[1].Samples.Select(sample => (sample.Name, sample.Volume))));
        }

        [TestCase(2)]
        [TestCase(8)]
        public void ClickCannotSplitAnExplicitDouble(float overallDifficulty)
        {
            Beatmap<HitObject> source = map(circle(2000, 47, true), circle(2000, 213, true));
            source.Difficulty.OverallDifficulty = overallDifficulty;
            SticksHitObject[] converted = convert(source);
            Assert.That(converted.OfType<SticksClick>(), Is.Empty);
            Assert.That(converted.OfType<SticksFlick>().Count(), Is.EqualTo(2));
        }

        [TestCase(2)]
        [TestCase(8)]
        public void ClickNeedsSpaceAfterTheEndOfASustain(float overallDifficulty)
        {
            Beatmap<HitObject> source = map(new SourceHold { StartTime = 0, Duration = 1900, Position = position(0) },
                circle(2000, 47, true));
            source.Difficulty.OverallDifficulty = overallDifficulty;
            Assert.That(convert(source).OfType<SticksClick>(), Is.Empty);
        }

        [Test]
        public void ReleasedSlidersNeedLessClickClearanceThanAnotherNoteHead()
        {
            Beatmap<HitObject> source = map(new SourceHold { StartTime = 0, Duration = 800, Position = position(0) },
                circle(1000, 47, true));
            source.Difficulty.OverallDifficulty = 4;
            SticksHitObject[] converted = convert(source);
            Assert.That(converted.OfType<SticksClick>().Select(note => note.StartTime), Is.EqualTo(new[] { 1000d }));
            Assert.That(converted.OfType<SticksSlider>().Single().EndTime, Is.EqualTo(800));

            source.HitObjects[0] = circle(800, 0);
            Assert.That(convert(source).OfType<SticksClick>(), Is.Empty, "Two nearby heads still need the full preparation gap.");
        }

        [Test]
        public void GeneratedDoubleHeadsRemainDirectional()
        {
            Beatmap<HitObject> source = accentedPhrase();
            source.Difficulty.OverallDifficulty = 8;
            SticksHitObject[] baseline = new SticksBeatmapConverter(source, new SticksRuleset()) { LimitBeginnerCoordination = false }.Convert()
                .HitObjects.Cast<SticksHitObject>().ToArray();
            double[] doubleTimes = baseline.GroupBy(hitObject => hitObject.StartTime)
                                          .Where(group => group.Count() == 2).Select(group => group.Key).ToArray();
            Assert.That(doubleTimes, Is.Not.Empty, "The control must contain generated doubles.");

            SticksHitObject[] converted = convert(source, beginner: false);
            Assert.That(converted.OfType<SticksClick>().Any(click => doubleTimes.Contains(click.StartTime)), Is.False);
            Assert.That(signature(converted.Where(hitObject => doubleTimes.Contains(hitObject.StartTime))),
                Is.EquivalentTo(signature(baseline.Where(hitObject => doubleTimes.Contains(hitObject.StartTime)))));
        }

        [Test]
        public void MapsWithoutAccentHitsoundsStillReceiveSparseClicks()
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 16).Select(index => (HitObject)circle(index * 1000, index * 67)).ToArray());
            SticksHitObject[] converted = convert(source);
            Assert.That(converted.OfType<SticksClick>(), Is.Not.Empty);
            Assert.That(converted.OfType<SticksClick>().Count(), Is.LessThanOrEqualTo(4));
        }

        [TestCase(2)]
        [TestCase(5)]
        public void SpacedNotesOffTheDownbeatDoNotNeedAccentHitsounds(float overallDifficulty)
        {
            Beatmap<HitObject> source = map(Enumerable.Range(0, 32)
                .Select(index => (HitObject)circle(125 + index * 1000, index * 67)).ToArray());
            source.Difficulty.OverallDifficulty = overallDifficulty;
            SticksClick[] clicks = convert(source).OfType<SticksClick>().ToArray();
            Assert.That(clicks.Length, Is.InRange(2, 7));
            Assert.That(clicks.Select(click => click.StartTime), Is.SubsetOf(source.HitObjects.Select(note => note.StartTime)));
            Assert.That(clicks.All(click => click.StartTime % 500 == 125));
        }

        [Test]
        public void NearbyAccentIsPreferredOverAnUnaccentedClickCandidate()
        {
            Beatmap<HitObject> source = map(circle(1125, 0), circle(1375, 45, true));
            source.Difficulty.OverallDifficulty = 8;
            Assert.That(convert(source).OfType<SticksClick>().Select(note => note.StartTime), Is.EqualTo(new[] { 1375d }));
        }

        [Test]
        public void DisablingHitsoundsDoesNotChangeAccentSelection()
        {
            Beatmap<HitObject> source = accentedPhrase();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { AddClickNotes = true };
            double[] times = converter.Convert().HitObjects.OfType<SticksClick>().Select(click => click.StartTime).ToArray();
            converter.DisableBeatmapHitsounds = true;
            SticksClick[] silent = converter.Convert().HitObjects.OfType<SticksClick>().ToArray();
            Assert.That(silent.Select(click => click.StartTime), Is.EqualTo(times));
            Assert.That(silent.SelectMany(click => click.Samples).All(sample => sample.Name == HitSampleInfo.HIT_NORMAL), Is.True);
        }

        [Test]
        public void AuthoredNotesIncludingClicksRemainExact()
        {
            SticksHitObject[] authored =
            {
                new SticksClick { StartTime = 1000, Side = StickSide.Right },
                new SticksSlider { StartTime = 2000, Duration = 3000, Angle = 13, ArcAngle = 90 },
                new SticksFlick { StartTime = 6000, Angle = 63 },
            };
            Beatmap<HitObject> source = map(authored.Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy).ToArray());
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            string[] baseline = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(baseline));
            Assert.That(converter.IsAuthoredCarrier, Is.True);
        }

        [Test]
        public void DirectNativeObjectsWithoutCarrierMarkersAreAlsoUnaffected()
        {
            var native = new Beatmap<SticksHitObject>();
            native.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            native.HitObjects.AddRange(new SticksHitObject[]
            {
                new SticksFlick { StartTime = 0, Angle = 30 },
                new SticksClick { StartTime = 500, Side = StickSide.Right },
                new SticksHold { StartTime = 1000, Duration = 2000, Angle = 75 },
            });
            var converter = new SticksBeatmapConverter(native, new SticksRuleset());
            Assert.That(converter.IsAuthoredCarrier, Is.False);
            string[] baseline = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(baseline));
        }

        private static string[] signature(IEnumerable<SticksHitObject> objects) => objects.Select(hitObject =>
            $"{hitObject.GetType().Name}:{hitObject.StartTime}:{hitObject.GetEndTime()}:{hitObject.Side}:{hitObject.Angle}:"
            + (hitObject is SticksSlider slider ? $"{slider.ArcAngle}:{slider.RepeatCount}" : "")).ToArray();

        private static SticksHitObject[] convert(Beatmap<HitObject> source, bool beginner = true) => new SticksBeatmapConverter(source, new SticksRuleset())
        {
            AddClickNotes = true,
            LimitBeginnerCoordination = beginner,
        }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static Beatmap<HitObject> accentedPhrase() => map(Enumerable.Range(0, 32)
            .Select(index => (HitObject)circle(index * 1000, index * 47, true)).ToArray());

        private static Beatmap<HitObject> map(params HitObject[] objects)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.AddRange(objects);
            return source;
        }

        private static SourceSpinner spinner(double time, double duration) => new SourceSpinner { StartTime = time, Duration = duration };

        private static SourceCircle circle(double time, float angle, bool accent = false) => new SourceCircle
        {
            StartTime = time,
            Position = position(angle),
            Samples = new[] { new HitSampleInfo(accent ? HitSampleInfo.HIT_CLAP : HitSampleInfo.HIT_NORMAL, volume: 73) },
        };

        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE
            + new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180)) * 160;

        private class SourceCircle : HitObject, IHasPosition, IHasCombo
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
            public bool NewCombo { get; set; }
            public int ComboOffset { get; set; }
        }

        private class SourceSlider : SourceCircle, IHasDuration
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
        }

        private sealed class SourceHold : SourceSlider
        {
        }

        private sealed class SourceSpinner : SourceSlider
        {
        }
    }
}
