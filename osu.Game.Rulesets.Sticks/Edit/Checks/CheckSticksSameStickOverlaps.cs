using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Edit.Checks
{
    public class CheckSticksSameStickOverlaps : ICheck
    {
        private const double time_leniency = 0.5;

        public CheckMetadata Metadata { get; } = new CheckMetadata(CheckCategory.Compose, "same-stick overlaps");

        public IEnumerable<IssueTemplate> PossibleTemplates => new[] { new IssueTemplateSameStickOverlap(this) };

        public IEnumerable<Issue> Run(BeatmapVerifierContext context)
        {
            SticksHitObject[] objects = context.CurrentDifficulty.Playable.HitObjects
                                                     .OfType<SticksHitObject>()
                                                     .OrderBy(hitObject => hitObject.StartTime)
                                                     .ToArray();

            for (int i = 0; i < objects.Length - 1; i++)
            {
                SticksHitObject current = objects[i];
                double currentEnd = endTimeOf(current);
                if (!double.IsFinite(currentEnd))
                    continue;

                for (int j = i + 1; j < objects.Length; j++)
                {
                    SticksHitObject next = objects[j];
                    if (!double.IsFinite(next.StartTime) || next.StartTime > currentEnd + time_leniency)
                        break;

                    if (current.Side == next.Side)
                        yield return new IssueTemplateSameStickOverlap(this).Create(current, next);
                }
            }
        }

        private static double endTimeOf(SticksHitObject hitObject) => hitObject switch
        {
            SticksSlider slider => slider.EndTime,
            SticksHold hold => hold.EndTime,
            _ => hitObject.StartTime,
        };

        public class IssueTemplateSameStickOverlap : IssueTemplate
        {
            public IssueTemplateSameStickOverlap(ICheck check)
                : base(check, IssueType.Problem, "{0} stick objects overlap here.")
            {
            }

            public Issue Create(SticksHitObject first, SticksHitObject second) =>
                new Issue(new HitObject[] { first, second }, this, first.Side)
                {
                    Time = second.StartTime,
                };
        }
    }
}
