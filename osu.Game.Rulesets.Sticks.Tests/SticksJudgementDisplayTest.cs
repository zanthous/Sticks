using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public partial class SticksJudgementDisplayTest
    {
        [TestCase("flick", false)]
        [TestCase("slider", true)]
        [TestCase("hold", false)]
        public void TestDotAppearsAtHeadAngleOnlyAfterBothComponents(string kind, bool angleFirst)
        {
            var display = new TestDisplay();
            SticksHitObject head = createHead(kind, StickSide.Right, 90);
            var timing = result(head, HitResult.Ok);
            var angle = result(angleOf(head), HitResult.Ok);

            display.Process(angleFirst ? angle : timing);
            Assert.That(visible(display), Is.Empty);
            display.Process(angleFirst ? timing : angle);
            Drawable dot = visible(display).Single();

            Assert.Multiple(() =>
            {
                Assert.That(dot.Position.X, Is.EqualTo(SticksPlayfield.SIZE / 2).Within(0.001));
                Assert.That(dot.Position.Y, Is.EqualTo(SticksPlayfield.SIZE / 2 + SticksJudgementDisplay.DOT_RADIUS).Within(0.001));
                Assert.That(dot.Position.Y - dot.Height / 2, Is.GreaterThan(SticksPlayfield.SIZE / 2 + SticksPlayfield.OUTER_RADIUS));
                Assert.That(dot.Size, Is.EqualTo(new Vector2(SticksJudgementDisplay.DOT_DIAMETER)));
                Assert.That(dot.Colour.AverageColour.SRGB, Is.EqualTo(Color4Extensions.FromHex("FFCC22")));
            });
        }

        [TestCase(HitResult.Great, HitResult.Ok, "00E589")]
        [TestCase(HitResult.Meh, HitResult.Great, "4EC42B")]
        [TestCase(HitResult.Ok, HitResult.Ok, "FFCC22")]
        [TestCase(HitResult.Meh, HitResult.Ok, "FF802B")]
        public void TestDotUsesCombinedAccuracyColour(HitResult timing, HitResult angle, string colour)
        {
            var display = new TestDisplay();
            processHead(display, createHead(), timing, angle);
            Assert.That(visible(display).Single().Colour.AverageColour.SRGB, Is.EqualTo(Color4Extensions.FromHex(colour)));
        }

        [Test]
        public void TestExactDoubleKeepsBothGradesVisibleWithoutOverlappingDots()
        {
            var display = new TestDisplay();
            SticksHitObject left = createHead(side: StickSide.Left, angle: 359);
            SticksHitObject right = createHead(side: StickSide.Right, angle: -1);

            display.Process(result(left, HitResult.Meh));
            display.Process(result(right, HitResult.Ok));
            display.Process(result(angleOf(right), HitResult.Ok));
            display.Process(result(angleOf(left), HitResult.Great));
            Drawable[] dots = visible(display);

            Assert.Multiple(() =>
            {
                Assert.That(dots.Length, Is.EqualTo(2));
                Assert.That((dots[0].Position - dots[1].Position).Length, Is.GreaterThan(SticksJudgementDisplay.DOT_DIAMETER));
                Assert.That(dots[0].Colour.AverageColour.SRGB, Is.EqualTo(Color4Extensions.FromHex("FFCC22")));
                Assert.That(dots[1].Colour.AverageColour.SRGB, Is.EqualTo(Color4Extensions.FromHex("4EC42B")));
            });
        }

        [Test]
        public void TestPerfectHitsRemainQuietAndDotsFadeIndependently()
        {
            var display = new TestDisplay();
            display.UpdateAt(1000);
            processHead(display, createHead(), HitResult.Great, HitResult.Great);
            Assert.That(visible(display), Is.Empty);

            processHead(display, createHead(), HitResult.Ok, HitResult.Ok);
            Drawable first = visible(display).Single();
            Assert.That(first.Alpha, Is.EqualTo(1));
            display.UpdateAt(1100);
            Assert.That(first.Alpha, Is.EqualTo(5.0 / 6).Within(0.001), "The dot must begin fading without an opaque hold.");
            processHead(display, createHead(angle: 180), HitResult.Meh, HitResult.Ok);
            Drawable second = visible(display).Single(dot => dot != first);

            display.UpdateAt(1000 + SticksJudgementDisplay.DISPLAY_DURATION + SticksJudgementDisplay.FADE_DURATION / 2);
            Assert.That(first.Alpha, Is.EqualTo(0.5).Within(0.001));
            Assert.That(second.Alpha, Is.EqualTo(2.0 / 3).Within(0.001));
            processHead(display, createHead(), HitResult.Great, HitResult.Great);
            Assert.That(first.Alpha, Is.EqualTo(0.5).Within(0.001), "A perfect hit must not erase or refresh another note's feedback.");

            display.UpdateAt(1000 + SticksJudgementDisplay.DISPLAY_DURATION + SticksJudgementDisplay.FADE_DURATION);
            Assert.That(first.Alpha, Is.Zero);
            Assert.That(second.Alpha, Is.EqualTo(1.0 / 6).Within(0.001));
            display.UpdateAt(1100 + SticksJudgementDisplay.DISPLAY_DURATION + SticksJudgementDisplay.FADE_DURATION);
            Assert.That(visible(display), Is.Empty);
            Assert.That(display.Alpha, Is.Zero);
        }

        [Test]
        public void TestDenseResultsReuseTheBoundedPool()
        {
            var display = new TestDisplay();
            Drawable[] pool = display.Children.ToArray();
            for (int i = 0; i < SticksJudgementDisplay.MAX_DOTS; i++)
                processHead(display, createHead(angle: i * 10), HitResult.Meh, HitResult.Ok);

            processHead(display, createHead(side: StickSide.Right, angle: 90), HitResult.Ok, HitResult.Ok);
            Assert.Multiple(() =>
            {
                Assert.That(display.Children, Is.EqualTo(pool));
                Assert.That(visible(display).Length, Is.EqualTo(SticksJudgementDisplay.MAX_DOTS));
                Assert.That(pool[0].Position, Is.EqualTo(SticksPlayfield.PointAt(90, SticksJudgementDisplay.DOT_RADIUS)));
                Assert.That(pool[0].Colour.AverageColour.SRGB, Is.EqualTo(Color4Extensions.FromHex("FFCC22")));
            });
        }

        [Test]
        public void TestRewindClearsDotsAndAllPendingPairs()
        {
            var display = new TestDisplay();
            SticksHitObject first = createHead();
            SticksHitObject second = createHead(angle: 90);
            processHead(display, createHead(angle: 180), HitResult.Ok, HitResult.Ok);
            display.Process(result(first, HitResult.Meh));
            display.Process(result(angleOf(second), HitResult.Ok));

            display.Revert(result(first, HitResult.Meh));
            display.Process(result(angleOf(first), HitResult.Ok));
            display.Process(result(second, HitResult.Ok));

            Assert.That(visible(display), Is.Empty);
            Assert.That(display.LastResult, Is.Null);
        }

        private static Drawable[] visible(SticksJudgementDisplay display) => display.Children.Where(dot => dot.Alpha > 0).ToArray();

        private static SticksAngleComponent angleOf(SticksHitObject head) => head.NestedHitObjects.OfType<SticksAngleComponent>().Single();

        private static JudgementResult result(SticksHitObject hitObject, HitResult type) => new JudgementResult(hitObject, hitObject.CreateJudgement()) { Type = type };

        private static void processHead(SticksJudgementDisplay display, SticksHitObject head, HitResult timing, HitResult angle)
        {
            display.Process(result(head, timing));
            display.Process(result(angleOf(head), angle));
        }

        private static SticksHitObject createHead(string kind = "flick", StickSide side = StickSide.Left, float angle = 0)
        {
            SticksHitObject head = kind switch
            {
                "slider" => new SticksSliderHead(),
                "hold" => new SticksHoldHead(),
                _ => new SticksFlick(),
            };
            head.StartTime = 1000;
            head.Side = side;
            head.Angle = angle;
            head.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return head;
        }

        private partial class TestDisplay : SticksJudgementDisplay
        {
            public TestDisplay()
            {
                UpdateAt(1000);
            }

            public void UpdateAt(double time)
            {
                var clock = new FramedClock(new ManualClock { CurrentTime = time });
                clock.ProcessFrame();
                Clock = clock;
                base.Update();
            }
        }
    }
}
