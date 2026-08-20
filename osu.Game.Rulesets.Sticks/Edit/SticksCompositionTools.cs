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
            TooltipText = "Place a flick; the outer lane is left stick and the inner lane is right stick";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHitCircle };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksFlickPlacementBlueprint();
    }

    public class SticksHoldCompositionTool : SticksCompositionTool
    {
        public SticksHoldCompositionTool()
            : base("Hold")
        {
            TooltipText = "Press and drag radially to place a hold; edit its duration from the timeline end handle";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorHoldNote };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksHoldPlacementBlueprint();
    }

    public class SticksSliderCompositionTool : SticksCompositionTool
    {
        public SticksSliderCompositionTool()
            : base("Slider")
        {
            TooltipText = "Press and trace the first arc, then release; select the slider and use + at its endpoint to place each reversal point";
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = OsuIcon.EditorSlider };

        public override HitObjectPlacementBlueprint CreatePlacementBlueprint() => new SticksSliderPlacementBlueprint();
    }
}
