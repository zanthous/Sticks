// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksEditorResourceBoundsTest
    {
        [Test]
        public void TestMaximumAuthoredSliderHasBoundedPreviewVertices()
        {
            var slider = new SticksSlider
            {
                Duration = 1000,
                ArcAngle = 360,
                RepeatCount = int.MaxValue,
                Side = StickSide.Left,
            };
            var preview = new SticksBlueprintPiece();

            preview.UpdateFrom(slider);

            int pathVertexCount = preview.ChildrenOfType<SmoothPath>().Sum(path => path.Vertices.Count);
            Assert.Multiple(() =>
            {
                Assert.That(slider.SegmentCount, Is.EqualTo(SticksSlider.MAX_SEGMENT_COUNT));
                Assert.That(pathVertexCount, Is.LessThanOrEqualTo(10_000));
            });
        }

        [Test]
        public void TestRepeatedDurationMarkerUpdatesDoNotRetainMemory()
        {
            var slider = new SticksSlider
            {
                Duration = 1000,
                ArcAngle = 180,
                Side = StickSide.Left,
            };
            slider.SetCustomSegments(new[] { 90f, -135f, 180f });
            slider.EnsureLegacyEditorMarker();

            collectGarbage();
            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < 10_000; i++)
                slider.Duration = 1000 + i % 2 * 500;

            collectGarbage();
            long retained = GC.GetTotalMemory(false) - before;

            TestContext.Progress.WriteLine($"10,000 marker updates retained {retained / 1024d / 1024:0.0} MiB");
            Assert.That(retained, Is.LessThan(8L * 1024 * 1024));
        }

        [Test]
        public void TestRepeatedEditorCarrierCloseAndReopenDoesNotRetainSessions()
        {
            const int session_count = 250;

            // Warm the editor processor and carrier projection before measuring retained state.
            for (int i = 0; i < 5; i++)
                runEditorSession(i);

            collectGarbage();
            long before = GC.GetTotalMemory(false);
            var sessionReferences = new List<WeakReference>(session_count * 4);

            for (int i = 0; i < session_count; i++)
                sessionReferences.AddRange(runEditorSession(i));

            collectGarbage();
            long retained = GC.GetTotalMemory(false) - before;
            int retainedSessions = sessionReferences.Count(reference => reference.IsAlive);

            TestContext.Progress.WriteLine(
                $"{session_count} editor carrier close/reopen cycles retained {retained / 1024d / 1024:0.0} MiB " +
                $"and {retainedSessions}/{sessionReferences.Count} session objects");

            Assert.Multiple(() =>
            {
                Assert.That(retainedSessions, Is.LessThanOrEqualTo(2),
                    "Editor beatmaps or change handlers are being retained after their session becomes unreachable.");
                Assert.That(retained, Is.LessThan(32L * 1024 * 1024));
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IReadOnlyList<WeakReference> runEditorSession(int index)
        {
            var ruleset = new SticksRuleset();
            RulesetInfo persistedRuleset = ruleset.RulesetInfo.Clone();
            var source = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(persistedRuleset.Clone()),
            };
            source.HitObjects.Add(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 30,
            });
            var sourceSlider = new SticksSlider
            {
                StartTime = 2000,
                Duration = 2000,
                Side = StickSide.Right,
                Angle = 210,
            };
            sourceSlider.SetCustomSegments(new[] { 90f, -135f, 180f });
            source.HitObjects.Add(sourceSlider);

            var editor = new EditorBeatmap(source, beatmapInfo: new BeatmapInfo(persistedRuleset.Clone()));
            var changeHandler = new BeatmapEditorChangeHandler(editor);
            editor.SelectedHitObjects.Add(sourceSlider);
            editor.PerformOnSelection(hitObject =>
            {
                var slider = (SticksSlider)hitObject;
                slider.Angle = 210 + index % 15;
                slider.Duration = 2000 + index % 4 * 125;
            });

            var standardRuleset = new RulesetInfo
            {
                OnlineID = 0,
                ShortName = "osu",
                Name = "osu!",
            };
            IBeatmap carrier = SticksEditorCarrierBeatmap.Create(editor, standardRuleset);

            IBeatmap reopenedSource = new SticksBeatmapConverter(carrier, ruleset).Convert();
            var reopenedEditor = new EditorBeatmap(reopenedSource, beatmapInfo: new BeatmapInfo(persistedRuleset.Clone()));
            var reopenedChangeHandler = new BeatmapEditorChangeHandler(reopenedEditor);

            Assert.That(reopenedEditor.HitObjects, Has.Count.EqualTo(2));
            Assert.That(((SticksSlider)reopenedEditor.HitObjects[1]).SegmentArcAngles, Is.EqualTo(new[] { 90f, -135f, 180f }));

            return new[]
            {
                new WeakReference(editor),
                new WeakReference(changeHandler),
                new WeakReference(reopenedEditor),
                new WeakReference(reopenedChangeHandler),
            };
        }

        private static void collectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
