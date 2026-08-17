using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    [Cached]
    public partial class SticksHitObjectComposer : HitObjectComposer<SticksHitObject>
    {
        public SticksHitObjectComposer(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

        protected override IReadOnlyList<CompositionTool> CompositionTools => new CompositionTool[]
        {
            new SticksFlickCompositionTool(),
            new SticksHoldCompositionTool(),
            new SticksSliderCompositionTool(),
        };

        protected override ComposeBlueprintContainer CreateBlueprintContainer() => new SticksBlueprintContainer(this);

        protected override Drawable CreateHitObjectInspector() => new SticksHitObjectInspector();

        public bool TryGetPlacement(Vector2 screenSpacePosition, out StickSide side, out float angle) =>
            SticksEditorCoordinates.TryGetPlacement(Playfield.ToLocalSpace(screenSpacePosition), out side, out angle);
    }
}
