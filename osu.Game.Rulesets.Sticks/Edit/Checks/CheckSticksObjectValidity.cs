// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Edit.Checks
{
    public class CheckSticksObjectValidity : ICheck
    {
        public CheckMetadata Metadata { get; } = new CheckMetadata(CheckCategory.Compose, "invalid Sticks objects");

        public IEnumerable<IssueTemplate> PossibleTemplates => new[] { new IssueTemplateInvalidObject(this) };

        public IEnumerable<Issue> Run(BeatmapVerifierContext context)
        {
            foreach (SticksHitObject hitObject in context.CurrentDifficulty.Playable.HitObjects.OfType<SticksHitObject>())
            {
                foreach (string problem in problemsFor(hitObject))
                    yield return new IssueTemplateInvalidObject(this).Create(hitObject, problem);
            }
        }

        private static IEnumerable<string> problemsFor(SticksHitObject hitObject)
        {
            if (!double.IsFinite(hitObject.StartTime))
                yield return "The start time is not finite.";

            if (!Enum.IsDefined(hitObject.Side))
                yield return "The stick side is invalid.";

            if (!float.IsFinite(hitObject.Angle))
                yield return "The angle is not finite.";

            if (!float.IsFinite(hitObject.PrimaryHitAngle) || hitObject.PrimaryHitAngle <= 0
                || !float.IsFinite(hitObject.SecondaryHitAngle) || hitObject.SecondaryHitAngle <= 0)
                yield return "The hit-angle windows are invalid.";

            if (hitObject.SyncedNoteSide is StickSide syncedSide)
            {
                if (!Enum.IsDefined(syncedSide))
                    yield return "The linked note's stick side is invalid.";

                if (!float.IsFinite(hitObject.SyncedNoteAngle))
                    yield return "The linked note's angle is not finite.";
            }

            switch (hitObject)
            {
                case SticksHold hold:
                    if (!double.IsFinite(hold.Duration) || hold.Duration <= 0 || !double.IsFinite(hold.EndTime))
                        yield return "The hold duration is invalid.";

                    break;

                case SticksSlider slider:
                    if (!double.IsFinite(slider.Duration) || slider.Duration <= 0 || !double.IsFinite(slider.EndTime))
                        yield return "The slider duration is invalid.";

                    if (slider.SegmentCount < 1 || slider.SegmentCount > SticksSlider.MAX_SEGMENT_COUNT)
                        yield return "The slider has an invalid number of path segments.";

                    if (slider.SegmentArcAngles.Any(segment => !float.IsFinite(segment) || Math.Abs(segment) < 1))
                        yield return "The slider contains an invalid path segment.";

                    if (!float.IsFinite(slider.TotalAngularDistance) || slider.TotalAngularDistance <= 0)
                        yield return "The slider path has no valid angular distance.";

                    for (int i = 0; i < slider.SegmentCount; i++)
                    {
                        if (!double.IsFinite(slider.SegmentStartTimeAt(i)) || !double.IsFinite(slider.SegmentDurationAt(i))
                                                                          || slider.SegmentDurationAt(i) <= 0
                                                                          || !float.IsFinite(slider.SegmentStartAngleAt(i)))
                        {
                            yield return "The slider path contains invalid timing or angle data.";
                            break;
                        }
                    }

                    break;
            }
        }

        public class IssueTemplateInvalidObject : IssueTemplate
        {
            public IssueTemplateInvalidObject(ICheck check)
                : base(check, IssueType.Problem, "{0}")
            {
            }

            public Issue Create(SticksHitObject hitObject, string message) => new Issue(hitObject, this, message);
        }
    }
}
