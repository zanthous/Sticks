using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public class SticksFlickCompositionTool : CompositionTool<SticksAction>
    {
        public SticksFlickCompositionTool()
            : base("Flick")
        {
            Action = SticksAction.EditorFlickTool;
            TooltipText = "Place a flick near the ring: outside selects the left stick, inside selects the right stick, farther outside selects both";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksFlickPlacementBlueprint();
    }

    public class SticksSliceCompositionTool : CompositionTool<SticksAction>
    {
        public SticksSliceCompositionTool() : base("Slice")
        {
            Action = SticksAction.EditorSliceTool;
            TooltipText = "Move through a Slice without flicking. Set its direction in the inspector.";
        }
        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };
        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksSlicePlacementBlueprint();
    }

    public class SticksClickCompositionTool : CompositionTool<SticksAction>
    {
        public SticksClickCompositionTool()
            : base("Click")
        {
            Action = SticksAction.EditorClickTool;
            TooltipText = "Place a button note near the ring: outside selects the left stick, inside selects the right stick, farther outside selects both. No aim required.";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksClickPlacementBlueprint();
    }

    public class SticksSliderCompositionTool : CompositionTool<SticksAction>
    {
        public SticksSliderCompositionTool()
            : base("Slider")
        {
            Action = SticksAction.EditorSliderTool;
            TooltipText = "Click near the ring (farther outside selects both sticks), trace the path, and scroll to its end time. Left-click finishes; right-click places the point and continues. Keep the same angle for a stationary slider; Escape cancels the pending point";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorSlider };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksSliderPlacementBlueprint();
    }
}
