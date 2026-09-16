using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksSliderSizeTest
    {
        [Test]
        public void InterpolatedSizesAgreeWithRibbonCheckpointsAndDifficultyAdjust()
        {
            var slider = create();
            slider.SetNodeSizeMultipliers(new[] { 1f, 2f, 0.5f });
            Assert.That(slider.SizeMultiplierAt(0), Is.EqualTo(1));
            Assert.That(slider.SizeMultiplierAt(1250), Is.EqualTo(1.5));
            Assert.That(slider.SizeMultiplierAt(1500), Is.EqualTo(2));
            Assert.That(slider.SizeMultiplierAt(2000), Is.EqualTo(1.25));
            Assert.That(slider.SizeMultiplierAt(3000), Is.EqualTo(0.5));

            for (int pass = 0; pass < 2; pass++)
            {
                defaults(slider);
                if (pass == 1)
                    new SticksModDifficultyAdjust { PrimaryHitAngle = { Value = 20 } }.ApplyToHitObject(slider);
                float baseWidth = pass == 0 ? 27.5f : 20;
                foreach (SticksHitObject checkpoint in slider.NestedHitObjects)
                {
                    Assert.That(checkpoint.PrimaryHitAngle, Is.EqualTo(slider.PrimaryHitAngleAt(checkpoint.StartTime)).Within(1e-5));
                    Assert.That(checkpoint.PrimaryHitAngle, Is.EqualTo(baseWidth * slider.SizeMultiplierAt(checkpoint.StartTime)).Within(1e-5));
                    Assert.That(checkpoint.LenientHalfAngle, Is.EqualTo(slider.LenientHalfAngleAt(checkpoint.StartTime)).Within(1e-5));
                }
                using var ribbon = new SticksRadialTimelinePath(StickSide.Left);
                foreach (double now in new[] { 1000d, 1500, 1100 })
                {
                    ribbon.SetSliderGeometry(slider, now, 2000);
                    int count = (int)typeof(SticksRadialTimelinePath).GetField("pointCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ribbon)!;
                    var points = (SticksRibbonPoint[])typeof(SticksRadialTimelinePath).GetField("points", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ribbon)!;
                    foreach (var point in points.Take(count))
                    {
                        double time = now + 2000 * (1 - (double)point.Radius / SticksPlayfield.GUIDE_RADIUS);
                        Assert.That(point.HalfSpan * 2, Is.EqualTo(slider.PrimaryHitAngleAt(time)).Within(0.0001));
                    }
                    Assert.That(points.Take(count).Any(point => Math.Abs(point.HalfSpan * 2 - baseWidth * 2) < 0.0001));
                }
            }
        }

        [Test]
        public void SizesSurviveCarrierClipboardCopiesAndPathEdits()
        {
            var slider = create();
            slider.SetNodeSizeMultipliers(new[] { 1f, 2f, 0.5f });
            slider.SizeMultiplier = 1.25f;
            string marker = SticksAuthoredBeatmapCodec.EncodeMarker(slider);
            Assert.That(marker, Does.StartWith("sticks-v5~"));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slider), out var decoded));
                slider = JsonConvert.DeserializeObject<SticksSlider>(JsonConvert.SerializeObject(decoded))!;
                Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(slider), Is.EqualTo(marker));
            }

            // Clipboard fields need not arrive in the serializer's preferred order.
            var reordered = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(slider));
            var sizeField = reordered.Property("nodeSizeMultipliers")!;
            sizeField.Remove();
            reordered.AddFirst(sizeField);
            Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(reordered.ToObject<SticksSlider>()!), Is.EqualTo(marker));

            Assert.That(SticksInspectorStickEdits.TryPrepare(new[] { slider }, new[] { slider }, null, out var plan, out string error), Is.True, error);
            var copy = (SticksSlider)plan.Additions.Single();
            Assert.That(copy.SerialisedNodeSizeMultipliers, Is.EqualTo(slider.SerialisedNodeSizeMultipliers));
            Assert.That(SticksInspectorStickEdits.IsBoth(new[] { slider, copy }));
            copy.SetNodeSizeMultipliers(new[] { 1f, 1f, 0.5f });
            Assert.That(SticksInspectorStickEdits.IsBoth(new[] { slider, copy }), Is.False);

            Assert.That(slider.AppendTimedSegment(45, 3000));
            Assert.That(slider.SerialisedNodeSizeMultipliers, Is.EqualTo(new[] { 1f, 2f, 0.5f, 0.5f }));
            slider.ReplaceFinalSegment(60);
            Assert.That(slider.RemoveFinalSegmentAtConstantSpeed());
            Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(slider), Is.EqualTo(marker));
        }

        [Test]
        public void PointEditsValidateEveryEndpointAndPreserveOtherProperties()
        {
            var slider = create();
            defaults(slider);
            foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity, 1000, double.Epsilon })
            {
                Assert.That(SticksInspectorEdits.TryPrepare(new[] { slider }, new SticksInspectorEdit
                {
                    NodeSizes = new Dictionary<int, double> { [1] = value },
                }, null, out _, out _), Is.False);
                Assert.That(slider.HasNodeSizes, Is.False);
            }
            Assert.That(SticksInspectorEdits.TryPrepare(new[] { slider }, new SticksInspectorEdit
            {
                NodeSizes = new Dictionary<int, double> { [1] = 2, [2] = 0.75 },
            }, null, out var plans, out string error), Is.True, error);
            plans.Single().Apply();
            Assert.That(slider.SerialisedNodeSizeMultipliers, Is.EqualTo(new[] { 1f, 2f, 0.75f }));
            Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 180f, -60f }));
            Assert.That(slider.SegmentDurationAt(0), Is.EqualTo(500));
            Assert.That(slider.Duration, Is.EqualTo(1500));
            slider.SizeMultiplier = 2;
            Assert.That(slider.SizeMultiplierAt(1500), Is.EqualTo(4));
            Assert.That(SticksInspectorEdits.TryPrepare(new[] { slider }, new SticksInspectorEdit { SizeMultiplier = 10 }, null, out _, out _), Is.False,
                "The global scale must validate the widest point too.");
        }

        [Test]
        public void MalformedProfilesCannotBecomeProceduralConversions()
        {
            foreach (string sizes in new[] { "1", "1_2_3_4", "1_0_1", "1_-2_1", "1_NaN_1", "1_Infinity_1", "1_1e40_1" })
            {
                var source = new HitObject
                {
                    StartTime = 1000,
                    Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo($"sticks-v5~1~{sizes}~v3~s~l~0~1500~180_-60~1_2.wav", 100) },
                };
                Assert.That(SticksAuthoredBeatmapCodec.InspectMarker(source).Status,
                    Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.MalformedSupported));
            }
        }

        [Test]
        public void ControlWeightsSizeWhereTheFastMovementOccurs()
        {
            double factor(float[] sizes)
            {
                var slider = create();
                slider.SetTimedSegments(new[] { 360f, -30f }, new[] { 500d, 500d });
                slider.SetNodeSizeMultipliers(sizes);
                defaults(slider);
                return SticksDifficultyScaling.ControlSizeStrainMultiplier(slider, 1);
            }
            // Identical average width and identical speed pattern, but different width
            // during the fast span. A whole-slider average cannot distinguish these.
            Assert.That(factor(new[] { 2f, 1.5f, 1f }), Is.LessThan(factor(new[] { 1f, 1.5f, 2f })));
            var uniform = create();
            uniform.SizeMultiplier = 2;
            defaults(uniform);
            Assert.That(factor(new[] { 2f, 2f, 2f }), Is.EqualTo(SticksDifficultyScaling.NoteSizeStrainMultiplier(uniform)).Within(1e-10));
            Assert.That(factor(new[] { 1f, 1f, 1f }), Is.EqualTo(1));
        }

        private static SticksSlider create()
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 1500, Angle = 30 };
            slider.SetTimedSegments(new[] { 180f, -60f }, new[] { 500d, 1000d });
            return slider;
        }

        private static void defaults(SticksSlider slider)
        {
            var points = new ControlPointInfo();
            points.Add(0, new TimingControlPoint { BeatLength = 250 });
            slider.ApplyDefaults(points, new BeatmapDifficulty { CircleSize = 4 });
        }
    }
}
