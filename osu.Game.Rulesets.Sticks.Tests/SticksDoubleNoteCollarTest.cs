using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksDoubleNoteCollarTest
    {
        [TestCase(0, 0)]
        [TestCase(90, 90)]
        [TestCase(359, -1)]
        public void TestExactDoubleCollarFollowsSharedHeadIncludingAngleWrap(float firstAngle, float secondAngle)
        {
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            SticksHitObject[] heads = { flick(StickSide.Left, firstAngle), flick(StickSide.Right, secondAngle) };
            Drawable overlap = pool(layer)[0];
            Drawable collar = field<Drawable>(overlap, "collar");

            layer.UpdateOverlaps(heads, 400);
            assertPosition(collar, SticksPlayfield.PointAt(firstAngle, 115));
            layer.UpdateOverlaps(heads, 1000);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(overlap.Alpha, Is.EqualTo(1));
                Assert.That(collar.Alpha, Is.EqualTo(1));
                Assert.That(collar.Size, Is.EqualTo(new Vector2(36, 40)));
                Assert.That(collar.Scale, Is.EqualTo(Vector2.One));
                Assert.That(collar.Rotation, Is.EqualTo(firstAngle));
                assertPosition(collar, SticksPlayfield.PointAt(firstAngle, SticksPlayfield.GUIDE_RADIUS));
                Assert.That(field<Drawable>(overlap, "firstTick").Alpha, Is.EqualTo(1));
                Assert.That(field<Drawable>(overlap, "secondTick").Alpha, Is.Zero,
                    "An exact double must retain one shared white aiming tick.");
            });
        }

        [TestCase(1, 27.5f)]
        [TestCase(0, 20)]
        public void TestPartialOverlapKeepsArcWithoutExactDoubleCollar(float secondAngle, float secondSpan)
        {
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            SticksHitObject[] heads = { flick(StickSide.Left), flick(StickSide.Right, secondAngle) };
            heads[1].PrimaryHitAngle = secondSpan;

            layer.UpdateOverlaps(heads, 1000);

            Drawable overlap = pool(layer)[0];
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(overlap.Alpha, Is.EqualTo(1), "The existing partial-overlap arc remains visible.");
                Assert.That(field<Drawable>(overlap, "collar").Alpha, Is.Zero);
            });
        }

        [Test]
        public void TestDifferentTimeSameStickAndSeparatedAnglesCannotShowCollar()
        {
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            SticksHitObject[][] pairs =
            {
                new SticksHitObject[] { flick(StickSide.Left), flick(StickSide.Right, time: 1001) },
                new SticksHitObject[] { flick(StickSide.Left), flick(StickSide.Left) },
                new SticksHitObject[] { flick(StickSide.Left), flick(StickSide.Right, 180) },
            };

            foreach (SticksHitObject[] heads in pairs)
            {
                layer.UpdateOverlaps(new SticksHitObject[] { flick(StickSide.Left), flick(StickSide.Right) }, 1000);
                Assert.That(pool(layer).Count(overlap => overlap.Alpha > 0), Is.EqualTo(1));
                layer.UpdateOverlaps(heads, 1000);
                Assert.That(pool(layer).Select(overlap => overlap.Alpha), Has.All.Zero);
            }
        }

        [Test]
        public void TestReusedPoolClearsExactCollarForPartialOverlapAndRestoresItForNextDouble()
        {
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            Drawable overlap = pool(layer)[0];
            Drawable collar = field<Drawable>(overlap, "collar");
            SticksHitObject[] heads = { flick(StickSide.Left), flick(StickSide.Right) };

            layer.UpdateOverlaps(heads, 1000);
            Assert.That(collar.Alpha, Is.EqualTo(1));
            heads[1].Angle = 5;
            layer.UpdateOverlaps(heads, 1000);
            Assert.That(overlap.Alpha, Is.EqualTo(1));
            Assert.That(collar.Alpha, Is.Zero);
            heads[0].Angle = heads[1].Angle = 120;
            layer.UpdateOverlaps(heads, 1000);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(pool(layer)[0], Is.SameAs(overlap));
                Assert.That(field<Drawable>(overlap, "collar"), Is.SameAs(collar));
                Assert.That(collar.Alpha, Is.EqualTo(1));
                Assert.That(collar.Rotation, Is.EqualTo(120));
                assertPosition(collar, SticksPlayfield.PointAt(120, SticksPlayfield.GUIDE_RADIUS));
            });
        }

        [Test]
        public void TestUnusedPoolEntriesHideImmediately()
        {
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            SticksHitObject[] heads =
            {
                flick(StickSide.Left), flick(StickSide.Right),
                flick(StickSide.Left, 90), flick(StickSide.Right, 90),
            };
            Drawable[] originalPool = pool(layer);

            layer.UpdateOverlaps(heads, 1000);
            Assert.That(originalPool.Count(overlap => overlap.Alpha > 0), Is.EqualTo(2));
            layer.UpdateOverlaps(heads.AsSpan(2), 1000);
            Assert.That(originalPool.Count(overlap => overlap.Alpha > 0), Is.EqualTo(1));
            assertPosition(field<Drawable>(originalPool[0], "collar"), SticksPlayfield.PointAt(90, SticksPlayfield.GUIDE_RADIUS));
            layer.UpdateOverlaps(heads, 1000);
            Assert.That(originalPool.Count(overlap => overlap.Alpha > 0), Is.EqualTo(2));
            layer.UpdateOverlaps(ReadOnlySpan<SticksHitObject>.Empty, 1000);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(originalPool.Select(overlap => overlap.Alpha), Has.All.Zero);
                Assert.That(pool(layer), Is.EqualTo(originalPool), "Presentation updates must reuse the allocated visual pool.");
            });
        }

        [TestCase("flick")]
        [TestCase("slider")]
        [TestCase("hold")]
        public void TestFirstHeadHitRemovesCollarWithoutWaitingForOtherHeadOrSustain(string kind)
        {
            DrawableHitObject first = kind switch
            {
                "slider" => new DrawableSticksSlider(new SticksSlider { StartTime = 1000, Duration = 2000, Side = StickSide.Left, PrimaryHitAngle = 27.5f }),
                "hold" => new DrawableSticksHold(new SticksHold { StartTime = 1000, Duration = 2000, Side = StickSide.Left, PrimaryHitAngle = 27.5f }),
                _ => new DrawableSticksFlick(flick(StickSide.Left)),
            };
            var second = new DrawableSticksFlick(flick(StickSide.Right));
            var layer = new SticksCenterOutNoteOverlapLayer(null!);
            SticksHitObject firstHead = SticksCenterOutNoteOverlapLayer.UnjudgedHeadOf(first);

            Assert.That(firstHead, Is.Not.Null);
            layer.UpdateOverlaps(new[] { firstHead, SticksCenterOutNoteOverlapLayer.UnjudgedHeadOf(second) }, 1000);
            Assert.That(pool(layer)[0].Alpha, Is.EqualTo(1));

            if (first is DrawableSticksFlick)
            {
                // Stage the judged result without loading audio or an input manager.
                first.Result.Type = HitResult.Great;
                Assert.That(first.Judged, Is.True);
            }
            else
            {
                first.GetType().GetField("headHit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(first, true);
                first.GetType().GetField("headJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(first, true);
                Assert.That(first.Judged, Is.False, "The duration object remains active after its head is hit.");
            }

            Assert.That(SticksCenterOutNoteOverlapLayer.UnjudgedHeadOf(first), Is.Null);
            Assert.That(SticksCenterOutNoteOverlapLayer.UnjudgedHeadOf(second), Is.SameAs(second.HitObject));
            layer.UpdateOverlaps(new[] { SticksCenterOutNoteOverlapLayer.UnjudgedHeadOf(second) }, 1000);
            Assert.That(pool(layer).Select(overlap => overlap.Alpha), Has.All.Zero);
        }

        private static void assertPosition(Drawable drawable, Vector2 expected)
        {
            Assert.That(drawable.Position.X, Is.EqualTo(expected.X).Within(0.001));
            Assert.That(drawable.Position.Y, Is.EqualTo(expected.Y).Within(0.001));
        }

        private static Drawable[] pool(SticksCenterOutNoteOverlapLayer layer) => field<Array>(layer, "overlaps").Cast<Drawable>().ToArray();

        private static T field<T>(object target, string name) where T : class =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static SticksFlick flick(StickSide side, float angle = 0, double time = 1000) => new SticksFlick
        {
            StartTime = time,
            Side = side,
            Angle = angle,
            PrimaryHitAngle = 27.5f,
        };
    }
}
