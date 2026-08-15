// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// A compact last-judgement display. Each result replaces the previous one in a thin bar at
    /// the bottom of the playfield instead of accumulating multiple judgement drawables.
    /// </summary>
    public partial class SticksJudgementDisplay : Container
    {
        public const float BAR_HEIGHT = 4;
        public const double DISPLAY_DURATION = 420;
        public const double FADE_DURATION = 100;

        private readonly Box fill;
        private readonly Dictionary<SticksAngleComponent, HitResult> pendingTimingResults = new Dictionary<SticksAngleComponent, HitResult>();
        private readonly Dictionary<SticksAngleComponent, HitResult> pendingAngleResults = new Dictionary<SticksAngleComponent, HitResult>();

        public HitResult? LastResult { get; private set; }

        public SticksJudgementDisplay()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            Position = new Vector2(0, SticksPlayfield.SIZE - BAR_HEIGHT);
            Size = new Vector2(SticksPlayfield.SIZE, BAR_HEIGHT);
            Alpha = 0;
            AlwaysPresent = true;
            Depth = -10;

            Child = fill = new Box
            {
                RelativeSizeAxes = Axes.Both,
            };
        }

        /// <summary>
        /// Collects the independently-scored timing and angle halves of one note and displays
        /// their combined grade once both have arrived. Results are paired by the generated
        /// angle hit object, so simultaneous notes cannot overwrite one shared pending value.
        /// </summary>
        public void Process(JudgementResult result)
        {
            if (result.HitObject is not ISticksAccuracyComponent component)
            {
                if (isActionCheckpoint(result.HitObject))
                    displayResult(result.Type.IsHit() ? HitResult.Perfect : result.Type);

                return;
            }

            if (component.AccuracyComponent == SticksAccuracyComponent.Timing)
            {
                SticksAngleComponent angleComponent = result.HitObject.NestedHitObjects.OfType<SticksAngleComponent>().SingleOrDefault();
                if (angleComponent == null)
                    return;

                if (pendingAngleResults.Remove(angleComponent, out HitResult angleResult))
                    Display(result.Type, angleResult);
                else
                    pendingTimingResults[angleComponent] = result.Type;

                return;
            }

            if (result.HitObject is not SticksAngleComponent angleHitObject)
                return;

            if (pendingTimingResults.Remove(angleHitObject, out HitResult timingResult))
                Display(timingResult, result.Type);
            else
                pendingAngleResults[angleHitObject] = result.Type;
        }

        public void Revert(JudgementResult result)
        {
            SticksAngleComponent angleComponent = result.HitObject switch
            {
                SticksAngleComponent angle => angle,
                ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Timing } =>
                    result.HitObject.NestedHitObjects.OfType<SticksAngleComponent>().SingleOrDefault(),
                _ => null,
            };

            if (angleComponent != null)
            {
                pendingTimingResults.Remove(angleComponent);
                pendingAngleResults.Remove(angleComponent);
            }

            ResetDisplay();
        }

        public void Display(HitResult timingResult, HitResult angleResult)
        {
            displayResult(CombinedResult(timingResult, angleResult));
        }

        private void displayResult(HitResult result)
        {
            // Misses already have audio feedback. Keeping them out of this display prevents a
            // red flash from obscuring the next useful accuracy colour during dense patterns.
            if (!result.IsHit())
                return;

            LastResult = result;
            fill.Colour = ColourForResult(result);

            ClearTransforms();
            Alpha = 1;
            this.Delay(DISPLAY_DURATION).FadeOut(FADE_DURATION);
        }

        private static bool isActionCheckpoint(HitObject hitObject) => hitObject is
            SticksSliderTail or SticksHoldTail or SticksSliderRepeat or SticksSliderExtension;

        public void ResetDisplay()
        {
            ClearTransforms();
            Alpha = 0;
            LastResult = null;
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

        internal static Color4 ColourForResult(HitResult result) => result switch
        {
            HitResult.Perfect => Color4Extensions.FromHex("99EEFF"),
            HitResult.Great or HitResult.LargeTickHit or HitResult.SmallTickHit or HitResult.SliderTailHit => Color4Extensions.FromHex("00E589"),
            HitResult.Good => Color4Extensions.FromHex("4EC42B"),
            HitResult.Ok => Color4Extensions.FromHex("FFCC22"),
            HitResult.Meh => Color4Extensions.FromHex("FF802B"),
            _ => Color4Extensions.FromHex("ED1121"),
        };
    }
}
