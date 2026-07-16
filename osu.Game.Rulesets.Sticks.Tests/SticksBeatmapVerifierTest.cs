// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksBeatmapVerifierTest
    {
        [Test]
        public void TestRulesetProvidesVerifier() =>
            Assert.That(new SticksRuleset().CreateBeatmapVerifier(), Is.TypeOf<SticksBeatmapVerifier>());

        [Test]
        public void TestSameStickOverlapsAreProblemsButOppositeStickOverlapIsAllowed()
        {
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.HitObjects.Add(new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 90,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1500,
                Side = StickSide.Right,
                Angle = 180,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1500,
                Side = StickSide.Left,
                Angle = 90,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 2500,
                Side = StickSide.Left,
                Angle = 180,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 3000,
                Side = StickSide.Right,
                Angle = 0,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 3000,
                Side = StickSide.Right,
                Angle = 180,
            });

            Issue[] issues = verify(beatmap);

            Assert.Multiple(() =>
            {
                Assert.That(issues, Has.Length.EqualTo(2));
                Assert.That(issues, Has.All.Matches<Issue>(issue => issue.Template.Type == IssueType.Problem));
                Assert.That(issues.Select(issue => issue.Time), Is.EqualTo(new double?[] { 1500, 3000 }));
            });
        }

        [Test]
        public void TestObjectEndingAtSameStickObjectStartIsReported()
        {
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.HitObjects.Add(new SticksHold
            {
                StartTime = 1000,
                Duration = 500,
                Side = StickSide.Left,
                Angle = 0,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1500,
                Side = StickSide.Left,
                Angle = 90,
            });

            Assert.That(verify(beatmap), Has.One.Matches<Issue>(issue => issue.ToString().Contains("Left stick")));
        }

        [Test]
        public void TestInvalidObjectDataIsReportedWithoutSpeedPolicy()
        {
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = float.NaN,
            });
            beatmap.HitObjects.Add(new SticksHold
            {
                StartTime = 2000,
                Duration = -100,
                Side = StickSide.Right,
                Angle = 0,
            });
            beatmap.HitObjects.Add(new SticksSlider
            {
                StartTime = 3000,
                Duration = 1,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 10000,
            });
            beatmap.HitObjects.Add(new SticksSlider
            {
                StartTime = 4000,
                Duration = 1000,
                Side = StickSide.Right,
                Angle = 0,
                ArcAngle = 0,
            });

            string[] messages = verify(beatmap).Select(issue => issue.ToString()).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(messages, Does.Contain("The angle is not finite."));
                Assert.That(messages, Does.Contain("The hold duration is invalid."));
                Assert.That(messages, Does.Contain("The slider contains an invalid path segment."));
                Assert.That(messages, Does.Contain("The slider path has no valid angular distance."));
                Assert.That(messages, Has.None.Contains("speed"),
                    "The verifier must not impose an arbitrary authored slider-speed limit.");
                Assert.That(messages.Count(message => message.Contains("slider")), Is.EqualTo(2),
                    "The extremely fast but structurally valid slider should not be rejected.");
            });
        }

        private static Issue[] verify(Beatmap<SticksHitObject> beatmap) =>
            new SticksBeatmapVerifier().Run(new BeatmapVerifierContext(beatmap, null!)).ToArray();
    }
}
