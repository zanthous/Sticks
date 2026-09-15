using System;
using System.ComponentModel;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>A small, fixed-size target played by moving the stick through it, without recharging.</summary>
    public class SticksSlice : SticksHitObject, ISticksAccuracyComponent
    {
        public const float RADIUS = 13;
        public static float HitAngle => 2 * MathF.Asin(RADIUS / SticksPlayfield.GUIDE_RADIUS) * 180 / MathF.PI;
        private SticksSliceDirection direction;

        public SticksSliceDirection Direction
        {
            get => direction;
            set
            {
                if (!Enum.IsDefined(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                direction = value;
                RefreshLegacyEditorMarker();
            }
        }

        public SticksAccuracyComponent AccuracyComponent => SticksAccuracyComponent.Timing;

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);
            PrimaryHitAngle = HitAngle;
            SecondaryHitAngle = HitAngle / 2;
        }

        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AddNested(new SticksClick.TimingWeight { StartTime = StartTime });
        }
    }

    public enum SticksSliceDirection
    {
        [Description("Either direction")]
        Neutral = 0,
        [Description("Clockwise")]
        Clockwise = 1,
        [Description("Counterclockwise")]
        Counterclockwise = -1,
    }
}
