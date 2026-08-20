#nullable enable

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

#if STICKS_RULESET_API_2026_818
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool<osu.Game.Rulesets.Sticks.SticksAction>;
#else
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool;
#endif

namespace osu.Game.Rulesets.Sticks.Edit
{
    [Cached]
    public partial class SticksHitObjectComposer :
#if STICKS_RULESET_API_2026_818
        HitObjectComposer<SticksHitObject, SticksAction>
#else
        HitObjectComposer<SticksHitObject>
#endif
    {
        public SticksHitObjectComposer(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

#if STICKS_RULESET_API_2026_818
        public override Bindable<TernaryState>? SelectionNewComboState => null;
#endif

        protected override IReadOnlyList<SticksCompositionTool> CompositionTools => new SticksCompositionTool[]
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
