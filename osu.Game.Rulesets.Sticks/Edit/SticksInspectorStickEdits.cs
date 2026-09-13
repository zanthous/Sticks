#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Edit
{
    internal sealed record SticksInspectorStickEditPlan(
        SticksHitObject[] Additions,
        SticksHitObject[] Removals,
        SticksInspectorEditPlan[] Updates,
        SticksHitObject[] Selection);

    internal static class SticksInspectorStickEdits
    {
        private const double head_tolerance = 0.5;

        public static StickSide? GetSelectionSide(IReadOnlyList<SticksHitObject> selection) =>
            selection.Count > 0 && selection.All(note => note.Side == selection[0].Side) ? selection[0].Side : null;

        public static bool IsBoth(IReadOnlyList<SticksHitObject> selection) =>
            selection.Count > 0 && groupsFor(selection).Values.All(group => group.Left.Count > 0 && group.Right.Count > 0);

        public static bool CanUseBoth(IReadOnlyList<SticksHitObject> selection, IReadOnlyList<SticksHitObject> allNotes, out string error) =>
            tryPropose(selection, allNotes, null, out _, out error);

        public static bool TryPrepare(IReadOnlyList<SticksHitObject> selection, IReadOnlyList<SticksHitObject> allNotes, StickSide? side,
                                      out SticksInspectorStickEditPlan plan, out string error)
        {
            plan = new SticksInspectorStickEditPlan(Array.Empty<SticksHitObject>(), Array.Empty<SticksHitObject>(),
                Array.Empty<SticksInspectorEditPlan>(), Array.Empty<SticksHitObject>());
            if (!tryPropose(selection, allNotes, side, out Proposal proposal, out error))
                return false;

            SticksHitObject[] additions = proposal.Copies.Select(copy => copyForSide(copy.Source, copy.Side)).ToArray();
            plan = new SticksInspectorStickEditPlan(additions, proposal.Removals.ToArray(), proposal.Updates,
                proposal.Selection.Concat(additions).ToArray());
            return true;
        }

        private static bool tryPropose(IReadOnlyList<SticksHitObject> selection, IReadOnlyList<SticksHitObject> allNotes, StickSide? side,
                                       out Proposal proposal, out string error)
        {
            proposal = new Proposal();
            var removals = proposal.Removals;
            error = string.Empty;
            if (side.HasValue && side != StickSide.Left && side != StickSide.Right)
                return fail("Choose Left, Right or Both.", out error);

            var all = new HashSet<SticksHitObject>(allNotes, ReferenceEqualityComparer.Instance);
            var selectedSet = new HashSet<SticksHitObject>(ReferenceEqualityComparer.Instance);
            SticksHitObject[] selected = selection.Where(selectedSet.Add).ToArray();
            if (selected.Length == 0 || selected.Any(note => !all.Contains(note)))
                return fail("The selected notes have changed. Select them again.", out error);
            if (selected.Any(note => note is not (SticksFlick or SticksClick or SticksSlider or SticksHold)))
                return fail("This note type does not support changing its stick.", out error);
            if (!SticksInspectorEdits.TryPrepare(selected, new SticksInspectorEdit(), null, out _, out error))
                return false;

            Dictionary<SticksHitObject, PairGroup> selectedGroups = groupsFor(selected);
            if (side.HasValue)
            {
                foreach (PairGroup group in selectedGroups.Values)
                {
                    if (group.For(side.Value).Count > 0)
                    {
                        foreach (SticksHitObject counterpart in group.For(opposite(side.Value)))
                            proposal.Removals.Add(counterpart);
                    }
                }

                proposal.Selection.AddRange(selected.Where(note => !removals.Contains(note)));
                if (!SticksInspectorEdits.TryPrepare(proposal.Selection, new SticksInspectorEdit { Side = side }, null,
                        out SticksInspectorEditPlan[] updates, out error))
                    return false;
                proposal.Updates = updates.Where(update => update.Side != update.Target.Side).ToArray();
            }
            else
            {
                proposal.Selection.AddRange(selected);
                Dictionary<SticksHitObject, PairGroup> allGroups = groupsFor(all);
                foreach ((SticksHitObject representative, PairGroup group) in selectedGroups)
                {
                    PairGroup existing = allGroups[representative];
                    foreach (StickSide wanted in new[] { StickSide.Left, StickSide.Right })
                    {
                        if (group.For(wanted).Count > 0)
                            continue;

                        SticksHitObject? partner = existing.For(wanted).FirstOrDefault();
                        if (partner != null)
                            proposal.Selection.Add(partner);
                        else
                            proposal.Copies.Add(new ProposedNote(representative, wanted, true));
                    }
                }
            }

            var changedSides = new Dictionary<SticksHitObject, StickSide>(ReferenceEqualityComparer.Instance);
            foreach (SticksInspectorEditPlan update in proposal.Updates)
                changedSides.Add(update.Target, update.Side);
            ProposedNote[] resulting = all.Where(note => !removals.Contains(note))
                                         .Select(note => new ProposedNote(note, changedSides.GetValueOrDefault(note, note.Side), false))
                                         .Concat(proposal.Copies).ToArray();
            if (introducesOverlap(resulting))
                return fail("That stick is already occupied by another note at this time.", out error);
            return true;
        }

        private static bool introducesOverlap(ProposedNote[] notes)
        {
            // Only pairs that used to belong to different sticks (or newly added copies) can
            // introduce a conflict. Existing unchanged overlaps remain untouched. Sorting and
            // bounded active-window accounting avoids a global scan for every selected note.
            foreach (var group in notes.GroupBy(note => (note.Side, Click: note.Source is SticksClick)))
            {
                ProposedNote[] sorted = group.OrderBy(note => note.Source.StartTime).ToArray();
                var recentHeads = new Origins();
                var sustaining = new Origins();
                var sustains = new PriorityQueue<ProposedNote, double>();
                int firstRecent = 0;
                for (int i = 0; i < sorted.Length; i++)
                {
                    ProposedNote current = sorted[i];
                    double time = current.Source.StartTime;
                    while (firstRecent < i && time - sorted[firstRecent].Source.StartTime > head_tolerance)
                        recentHeads.Add(sorted[firstRecent++], -1);
                    while (sustains.TryPeek(out ProposedNote ended, out double endTime) && endTime <= time)
                    {
                        sustains.Dequeue();
                        sustaining.Add(ended, -1);
                    }

                    if (recentHeads.Conflicts(current) || sustaining.Conflicts(current))
                        return true;
                    recentHeads.Add(current, 1);
                    if (current.Source is IHasDuration duration && duration.Duration > 0)
                    {
                        sustaining.Add(current, 1);
                        sustains.Enqueue(current, time + duration.Duration);
                    }
                }
            }
            return false;
        }

        private static SticksHitObject copyForSide(SticksHitObject source, StickSide side)
        {
            SticksHitObject copy = source switch
            {
                SticksSlider => new SticksSlider(),
                SticksHold => new SticksHold(),
                SticksClick => new SticksClick(),
                SticksFlick => new SticksFlick(),
                _ => throw new InvalidOperationException("Unsupported stick-edit source."),
            };
            copy.StartTime = source.StartTime;
            copy.Angle = source.Angle;
            copy.Side = side;
            copy.PrimaryHitAngle = source.PrimaryHitAngle;
            copy.SecondaryHitAngle = source.SecondaryHitAngle;
            copy.Samples = source.CreatePlayableSamples().Select(sample => sample.With()).ToList();
            if (source is SticksSlider slider && copy is SticksSlider sliderCopy)
            {
                sliderCopy.Duration = slider.Duration;
                if (slider.HasTimedSegments)
                    sliderCopy.SetTimedSegments(slider.SegmentArcAngles, slider.SegmentDurationWeights);
                else if (slider.HasCustomSegments)
                    sliderCopy.SetCustomSegments(slider.SegmentArcAngles);
                else
                {
                    sliderCopy.ArcAngle = slider.ArcAngle;
                    sliderCopy.RepeatCount = slider.RepeatCount;
                }

                foreach (var samples in slider.NodeSamples)
                    sliderCopy.NodeSamples.Add(samples.Select(sample => sample.With()).ToList());
            }
            else if (source is SticksHold hold && copy is SticksHold holdCopy)
                holdCopy.Duration = hold.Duration;

            copy.EnsureLegacyEditorMarker();
            return copy;
        }

        private static Dictionary<SticksHitObject, PairGroup> groupsFor(IEnumerable<SticksHitObject> notes)
        {
            var groups = new Dictionary<SticksHitObject, PairGroup>(ExactGeometryComparer.Instance);
            foreach (SticksHitObject note in notes)
            {
                if (!groups.TryGetValue(note, out PairGroup? group))
                    groups.Add(note, group = new PairGroup());
                group.For(note.Side).Add(note);
            }
            return groups;
        }

        private static StickSide opposite(StickSide side) => side == StickSide.Left ? StickSide.Right : StickSide.Left;

        private static bool fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private sealed class Proposal
        {
            public readonly HashSet<SticksHitObject> Removals = new HashSet<SticksHitObject>(ReferenceEqualityComparer.Instance);
            public readonly List<SticksHitObject> Selection = new List<SticksHitObject>();
            public readonly List<ProposedNote> Copies = new List<ProposedNote>();
            public SticksInspectorEditPlan[] Updates = Array.Empty<SticksInspectorEditPlan>();
        }

        private readonly record struct ProposedNote(SticksHitObject Source, StickSide Side, bool Addition);

        private sealed class Origins
        {
            private int left;
            private int right;
            private int additions;

            public void Add(ProposedNote note, int delta)
            {
                if (note.Addition)
                    additions += delta;
                else if (note.Source.Side == StickSide.Left)
                    left += delta;
                else
                    right += delta;
            }

            public bool Conflicts(ProposedNote note) => note.Addition ? left + right + additions > 0
                : additions > 0 || (note.Source.Side == StickSide.Left ? right > 0 : left > 0);
        }

        private sealed class PairGroup
        {
            public readonly List<SticksHitObject> Left = new List<SticksHitObject>();
            public readonly List<SticksHitObject> Right = new List<SticksHitObject>();
            public List<SticksHitObject> For(StickSide side) => side == StickSide.Left ? Left : Right;
        }

        private sealed class ExactGeometryComparer : IEqualityComparer<SticksHitObject>
        {
            public static readonly ExactGeometryComparer Instance = new ExactGeometryComparer();

            public bool Equals(SticksHitObject? first, SticksHitObject? second)
            {
                if (ReferenceEquals(first, second))
                    return true;
                if (first == null || second == null || first.GetType() != second.GetType() || first.StartTime != second.StartTime
                    || first is not SticksClick && SticksHitObject.NormaliseAngle(first.Angle) != SticksHitObject.NormaliseAngle(second.Angle))
                    return false;
                if (first is SticksSlider left && second is SticksSlider right)
                    return left.Duration == right.Duration && left.SegmentCount == right.SegmentCount
                           && Enumerable.Range(0, left.SegmentCount).All(index => left.SegmentArcAngleAt(index) == right.SegmentArcAngleAt(index)
                                                                               && left.SegmentDurationAt(index) == right.SegmentDurationAt(index));
                return first is not SticksHold firstHold || second is SticksHold secondHold && firstHold.Duration == secondHold.Duration;
            }

            public int GetHashCode(SticksHitObject note)
            {
                var hash = new HashCode();
                hash.Add(note.GetType());
                hash.Add(note.StartTime);
                if (note is not SticksClick)
                    hash.Add(SticksHitObject.NormaliseAngle(note.Angle));
                if (note is SticksSlider slider)
                {
                    hash.Add(slider.Duration);
                    for (int i = 0; i < slider.SegmentCount; i++)
                    {
                        hash.Add(slider.SegmentArcAngleAt(i));
                        hash.Add(slider.SegmentDurationAt(i));
                    }
                }
                else if (note is SticksHold hold)
                    hash.Add(hold.Duration);
                return hash.ToHashCode();
            }
        }
    }
}
