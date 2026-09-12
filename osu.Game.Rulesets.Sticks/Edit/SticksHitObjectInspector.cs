using System;
using System.Linq;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public partial class SticksHitObjectInspector : HitObjectInspector
    {
        protected override void AddInspectorValues(HitObject[] objects)
        {
            base.AddInspectorValues(objects);

            if (objects.Length != 1 || objects.Single() is not SticksHitObject sticks)
                return;

            AddHeader("Stick");
            AddValue(sticks.Side == StickSide.Left ? "Left (outer / blue)" : "Right (inner / red)");

            if (sticks is SticksClick)
            {
                AddHeader("Input");
                AddValue("Button press — no aim required");
            }
            else
            {
                AddHeader("Angle");
                AddValue($"{SticksHitObject.NormaliseAngle(sticks.Angle):0.###}°");
            }

            switch (sticks)
            {
                case SticksSlider slider:
                    AddHeader("Path");
                    AddValue($"{slider.SegmentCount} segment{(slider.SegmentCount == 1 ? string.Empty : "s")}");
                    AddValue($"{slider.TotalAngularDistance:0.###}° total");
                    AddValue($"End {SticksHitObject.NormaliseAngle(slider.SegmentStartAngleAt(slider.SegmentCount)):0.###}°");
                    if (slider.HasTimedSegments)
                    {
                        double[] speeds = Enumerable.Range(0, slider.SegmentCount)
                                                    .Select(index => Math.Abs(slider.SegmentArcAngleAt(index)) / Math.Max(0.001, slider.SegmentDurationAt(index) / 1000))
                                                    .ToArray();
                        AddValue($"{speeds.Min():0.##}–{speeds.Max():0.##}°/s (timed segments)");
                    }
                    else
                        AddValue($"{slider.TotalAngularDistance / Math.Max(0.001, slider.Duration / 1000):0.##}°/s (constant)");
                    AddHeader("Editor controls");
                    AddValue("Drag tail: final point");
                    AddValue("Shift + drag tail: snap 15°");
                    AddValue("Tail +: place next reversal point");
                    AddValue("Tail −: remove final point");
                    AddValue("Right-click: cancel point placement");
                    AddValue("Timeline end handle: duration / global speed");
                    break;

                case SticksHold:
                    AddHeader("Editor controls");
                    AddValue("Timeline end handle: duration");
                    break;
            }
        }
    }
}
