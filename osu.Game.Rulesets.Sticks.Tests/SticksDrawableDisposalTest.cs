using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksDrawableDisposalTest
    {
        [TestCase(true)]
        [TestCase(false)]
        public void TestCancelledLoadDisposesDurationNoteAndRemainingChildren(bool slider)
        {
            Drawable note = slider
                ? new DrawableSticksSlider(new SticksSlider { StartTime = 1000, Duration = 2000 })
                : new DrawableSticksHold(new SticksHold { StartTime = 1000, Duration = 2000 });
            var sibling = new Box();
            var parent = new Container { Children = new[] { note, sibling } };

            // Cancelling the player loader can dispose its children before their
            // dependency loaders have supplied a playfield.
            Assert.DoesNotThrow(parent.Dispose);
            Assert.DoesNotThrow(parent.Dispose);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(isDisposed(note), Is.True);
                Assert.That(isDisposed(sibling), Is.True);
                Assert.That(isDisposed(parent), Is.True);
            });
        }

        private static bool isDisposed(Drawable drawable) => (bool)typeof(Drawable)
            .GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
    }
}
