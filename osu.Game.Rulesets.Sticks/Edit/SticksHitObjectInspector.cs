// Copyright (c) Zanthous. Licensed under the MIT Licence.

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

            AddHeader("Angle");
            AddValue($"{SticksHitObject.NormaliseAngle(sticks.Angle):0.###}°");

            switch (sticks)
            {
                case SticksSlider slider:
                    AddHeader("Arc");
                    AddValue($"{slider.ArcAngle:+0.###;-0.###;0}°");
                    AddValue($"End {SticksHitObject.NormaliseAngle(slider.Angle + slider.ArcAngle):0.###}°");
                    AddValue($"{Math.Abs(slider.ArcAngle) / Math.Max(0.001, slider.SpanDuration / 1000):0.##}°/s");
                    AddHeader("Editor controls");
                    AddValue("Drag tail: arc end");
                    AddValue("Shift + drag tail: snap 15°");
                    AddValue("Tail − / +: reversals");
                    AddValue("Timeline end handle: duration");
                    break;

                case SticksHold:
                    AddHeader("Editor controls");
                    AddValue("Timeline end handle: duration");
                    break;
            }
        }
    }
}
