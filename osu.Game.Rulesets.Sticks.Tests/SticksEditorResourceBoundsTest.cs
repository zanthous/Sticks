// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;

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

        private static void collectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
