// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// A compact, mania-style last-judgement display. Each result replaces the previous one at
    /// the exact centre of the playfield instead of accumulating multiple judgement drawables.
    /// </summary>
    public partial class SticksJudgementDisplay : CircularContainer
    {
        private readonly Box fill;
        private OsuColour colours = null!;

        public SticksJudgementDisplay()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Position = new Vector2(SticksPlayfield.SIZE / 2);
            Size = new Vector2(38);
            Masking = true;
            Alpha = 0;
            AlwaysPresent = true;
            Depth = -10;

            Child = fill = new Box
            {
                RelativeSizeAxes = Axes.Both,
            };
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours) => this.colours = colours;

        public void Display(HitResult timingResult, HitResult angleResult)
        {
            fill.Colour = colours.ForHitResult(CombinedResult(timingResult, angleResult));

            ClearTransforms();
            Alpha = 1;
            this.Delay(420).FadeOut(140);
        }

        public void ResetDisplay()
        {
            ClearTransforms();
            Alpha = 0;
        }

        public static HitResult CombinedResult(HitResult timingResult, HitResult angleResult)
        {
            (timingResult, angleResult) = SticksHitObject.ResolveComponentResults(timingResult, angleResult);

            if (timingResult == HitResult.Miss)
                return HitResult.Miss;

            return (timingResult, angleResult) switch
            {
                (HitResult.Great, HitResult.Great) => HitResult.Perfect,
                (HitResult.Great, HitResult.Ok) => HitResult.Great,
                (HitResult.Ok, HitResult.Great) => HitResult.Great,
                (HitResult.Meh, HitResult.Great) => HitResult.Good,
                (HitResult.Ok, HitResult.Ok) => HitResult.Ok,
                (HitResult.Meh, HitResult.Ok) => HitResult.Meh,
                _ => HitResult.Miss,
            };
        }
    }
}
