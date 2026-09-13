using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;

#if STICKS_RULESET_API_2026_818
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool<osu.Game.Rulesets.Sticks.SticksAction>;
#else
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool;
#endif

namespace osu.Game.Rulesets.Sticks.Edit
{
    public class SticksFlickCompositionTool : SticksCompositionTool
    {
        public SticksFlickCompositionTool()
            : base("Flick")
        {
            TooltipText = "Place a flick near the ring: outside selects the left stick, inside selects the right stick, farther outside selects both";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksFlickPlacementBlueprint();
    }

    public class SticksClickCompositionTool : SticksCompositionTool
    {
        public SticksClickCompositionTool()
            : base("Click")
        {
            TooltipText = "Place a button note near the ring: outside selects the left stick, inside selects the right stick, farther outside selects both. No aim required.";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksClickPlacementBlueprint();
    }

    public class SticksSliderCompositionTool : SticksCompositionTool
    {
        public SticksSliderCompositionTool()
            : base("Slider")
        {
            TooltipText = "Click near the ring (farther outside selects both sticks), trace the path, and scroll to its end time. Left-click finishes; right-click places the point and continues. Keep the same angle for a stationary slider; Escape cancels the pending point";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorSlider };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksSliderPlacementBlueprint();
    }
}
