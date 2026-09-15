using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Brief coloured dots outside the ring identify non-perfect hits at their note angles.
    /// A bounded pool keeps simultaneous results independent without allocating visuals per hit.
    /// </summary>
    public partial class SticksJudgementDisplay : Container
    {
        public const float DOT_DIAMETER = 10;
        public const float DOT_RADIUS = SticksPlayfield.GUIDE_RADIUS + 22;
        public const float STICK_SEPARATION = 14;
        public const double DISPLAY_DURATION = 0;
        public const double FADE_DURATION = 600;
        internal const int MAX_DOTS = 32;

        private readonly SticksSkinnedSprite[] dots = new SticksSkinnedSprite[MAX_DOTS];
        private readonly double[] shownAt = new double[MAX_DOTS];
        private int nextDot;
        private readonly Dictionary<SticksAngleComponent, HitResult> pendingTimingResults = new Dictionary<SticksAngleComponent, HitResult>();
        private readonly Dictionary<SticksAngleComponent, HitResult> pendingAngleResults = new Dictionary<SticksAngleComponent, HitResult>();

        public HitResult? LastResult { get; private set; }

        /// <summary>One complete head result, after both timing and aim are known.</summary>
        public event Action<SticksHitObject, HitResult> HeadJudged;

        public SticksJudgementDisplay()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            Size = new Vector2(SticksPlayfield.SIZE);
            Alpha = 0;
            AlwaysPresent = true;
            Depth = -10;

            for (int i = 0; i < dots.Length; i++)
            {
                Add(dots[i] = new SticksSkinnedSprite("sticks-judgement", new Circle { RelativeSizeAxes = Axes.Both })
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(DOT_DIAMETER),
                    Alpha = 0,
                });
            }
        }

        /// <summary>
        /// Collects the independently-scored timing and angle halves of one note and displays
        /// their combined grade once both have arrived. Results are paired by the generated
        /// angle hit object, so simultaneous notes cannot overwrite one shared pending value.
        /// </summary>
        public void Process(JudgementResult result, bool showDots = true)
        {
            if (result.HitObject is SticksHitObject click && click is SticksClick or SticksSlice)
            {
                // Halos have no aim location. Use a stable side-specific point for timing feedback.
                displayResult(click, result.Type switch
                {
                    HitResult.Great => HitResult.Perfect,
                    _ => result.Type,
                }, showDots);
                return;
            }

            if (result.HitObject is not ISticksAccuracyComponent component)
                return;

            if (component.AccuracyComponent == SticksAccuracyComponent.Timing)
            {
                SticksAngleComponent angleComponent = result.HitObject.NestedHitObjects.OfType<SticksAngleComponent>().SingleOrDefault();
                if (angleComponent == null)
                    return;

                if (pendingAngleResults.Remove(angleComponent, out HitResult angleResult))
                    displayResult(angleComponent, CombinedResult(result.Type, angleResult), showDots);
                else
                    pendingTimingResults[angleComponent] = result.Type;

                return;
            }

            if (result.HitObject is not SticksAngleComponent angleHitObject)
                return;

            if (pendingTimingResults.Remove(angleHitObject, out HitResult timingResult))
                displayResult(angleHitObject, CombinedResult(timingResult, result.Type), showDots);
            else
                pendingAngleResults[angleHitObject] = result.Type;
        }

        public void Revert(JudgementResult result)
        {
            // A rewind invalidates both the visible feedback and any half-finished pairs.
            ResetDisplay();
        }

        private void displayResult(SticksHitObject source, HitResult result, bool showDots)
        {
            HeadJudged?.Invoke(source, result);
            // Successful action checkpoints and perfect heads need no accuracy correction.
            // Misses retain their existing audio feedback rather than adding a dot.
            if (!showDots || result == HitResult.Perfect || !result.IsHit())
                return;

            LastResult = result;
            SticksSkinnedSprite dot = dots[nextDot];
            // Keep both hands outside the ring, with the left hand slightly farther out
            // (matching its outer lane) so exact doubles cannot overwrite each other.
            dot.Position = SticksPlayfield.PointAt(source is SticksClick ? (source.Side == StickSide.Left ? 180 : 0) : source.Angle, DOT_RADIUS + (source.Side == StickSide.Left ? STICK_SEPARATION : 0));
            dot.Colour = ColourForResult(result);
            dot.Alpha = 1;
            shownAt[nextDot] = Time.Current;
            nextDot = (nextDot + 1) % dots.Length;
            Alpha = 1;
        }

        protected override void Update()
        {
            base.Update();
            bool anyVisible = false;

            for (int i = 0; i < dots.Length; i++)
            {
                SticksSkinnedSprite dot = dots[i];
                if (dot.Alpha == 0)
                    continue;

                dot.Alpha = (float)(1 - Math.Clamp((Time.Current - shownAt[i] - DISPLAY_DURATION) / FADE_DURATION, 0, 1));
                anyVisible |= dot.Alpha > 0;
            }

            Alpha = anyVisible ? 1 : 0;
        }

        public void ResetDisplay()
        {
            foreach (SticksSkinnedSprite dot in dots)
                dot.Alpha = 0;

            pendingTimingResults.Clear();
            pendingAngleResults.Clear();
            nextDot = 0;
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
