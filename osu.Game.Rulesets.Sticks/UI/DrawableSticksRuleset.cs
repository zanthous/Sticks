// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Input.Bindings;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Sticks.UI
{
    [Cached]
    public partial class DrawableSticksRuleset : DrawableRuleset<SticksHitObject>, IKeyBindingHandler<GlobalAction>
    {
        private const float approach_rate_step = 0.5f;
        private const float fine_approach_rate_step = 0.1f;

        protected new SticksRulesetConfigManager Config => (SticksRulesetConfigManager)base.Config;

        private readonly BindableFloat approachRate = new BindableFloat();
        private readonly BindableFloat flickActivationThreshold = new BindableFloat();
        private readonly Bindable<SticksChordLinkPresentation> chordLinkPresentation =
            new Bindable<SticksChordLinkPresentation>(SticksChordLinkPresentation.FullToCentre);
        private readonly Bindable<SticksStackedNotePresentation> stackedNotePresentation =
            new Bindable<SticksStackedNotePresentation>(SticksStackedNotePresentation.RadialSpacing);
        private readonly Bindable<SticksNotePresentation> notePresentation =
            new Bindable<SticksNotePresentation>(SticksNotePresentation.BracketMarkers);
        private readonly BindableBool hideInactiveCursors = new BindableBool();
        private readonly BindableBool sliderTrackingSparks = new BindableBool();
        private readonly BindableFloat noteCircleScale = new BindableFloat(SticksPlayfield.DEFAULT_NOTE_CIRCLE_SCALE);
        private readonly BindableFloat radialApproachDistance = new BindableFloat(SticksPlayfield.DEFAULT_RADIAL_APPROACH_DISTANCE);
        private readonly BindableFloat radialApproachSpeed = new BindableFloat(SticksPlayfield.DEFAULT_RADIAL_APPROACH_SPEED);
        private readonly SticksReplayInputProvider replayInputProvider = new SticksReplayInputProvider();

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public DrawableSticksRuleset(SticksRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods = null)
            : base(ruleset, beatmap, mods)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Config.BindWith(SticksRulesetSetting.ApproachRate, approachRate);
            approachRate.BindValueChanged(rate => applyApproachRate(rate.NewValue), true);
            Config.BindWith(SticksRulesetSetting.FlickActivationThreshold, flickActivationThreshold);
            flickActivationThreshold.BindValueChanged(threshold =>
                ((SticksPlayfield)Playfield).FlickActivationThreshold = threshold.NewValue, true);
            Config.BindWith(SticksRulesetSetting.ChordLinkPresentation, chordLinkPresentation);
            chordLinkPresentation.BindValueChanged(presentation =>
                ((SticksPlayfield)Playfield).ChordLinkPresentation = presentation.NewValue, true);
            Config.BindWith(SticksRulesetSetting.StackedNotePresentation, stackedNotePresentation);
            stackedNotePresentation.BindValueChanged(presentation =>
                ((SticksPlayfield)Playfield).StackedNotePresentation = presentation.NewValue, true);
            Config.BindWith(SticksRulesetSetting.NotePresentation, notePresentation);
            notePresentation.BindValueChanged(presentation =>
                ((SticksPlayfield)Playfield).NotePresentation = presentation.NewValue, true);
            Config.BindWith(SticksRulesetSetting.HideInactiveCursors, hideInactiveCursors);
            hideInactiveCursors.BindValueChanged(hidden =>
                ((SticksPlayfield)Playfield).HideInactiveCursors = hidden.NewValue, true);
            Config.BindWith(SticksRulesetSetting.SliderTrackingSparks, sliderTrackingSparks);
            sliderTrackingSparks.BindValueChanged(enabled =>
                ((SticksPlayfield)Playfield).SliderTrackingSparks = enabled.NewValue, true);
            Config.BindWith(SticksRulesetSetting.NoteCircleScale, noteCircleScale);
            noteCircleScale.BindValueChanged(scale =>
                ((SticksPlayfield)Playfield).NoteCircleScale = scale.NewValue, true);
            Config.BindWith(SticksRulesetSetting.RadialApproachDistance, radialApproachDistance);
            radialApproachDistance.BindValueChanged(distance =>
                ((SticksPlayfield)Playfield).RadialApproachDistance = distance.NewValue, true);
            Config.BindWith(SticksRulesetSetting.RadialApproachSpeed, radialApproachSpeed);
            radialApproachSpeed.BindValueChanged(speed =>
                ((SticksPlayfield)Playfield).RadialApproachSpeed = speed.NewValue, true);
        }

        public override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer() => new SticksPlayfieldAdjustmentContainer();

        protected override Playfield CreatePlayfield() => new SticksPlayfield(replayInputProvider);

        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new SticksFramedReplayInputHandler(
            replay,
            replayInputProvider,
            () => ((SticksPlayfield)Playfield).PhysicalStickDistanceAtGameEdge,
            () => ((SticksPlayfield)Playfield).FlickActivationThreshold);

        protected override ReplayRecorder CreateReplayRecorder(Score score) => new SticksReplayRecorder(score, (SticksPlayfield)Playfield);

        public override void SetReplayScore(Score replayScore)
        {
            // Unlike a normal player, editor test play can toggle autoplay on and off while the
            // same drawable ruleset remains alive. Do not retain the last bot stick position
            // while lazer replaces or removes its replay handler.
            replayInputProvider.Deactivate();
            base.SetReplayScore(replayScore);
        }

        public override DrawableHitObject<SticksHitObject> CreateDrawableRepresentation(SticksHitObject hitObject)
        {
            hitObject.ApplyPlayerApproachRate(Config.Get<float>(SticksRulesetSetting.ApproachRate));

            return hitObject switch
            {
                SticksSlider slider => new DrawableSticksSlider(slider),
                SticksHold hold => new DrawableSticksHold(hold),
                SticksFlick flick => new DrawableSticksFlick(flick),
                _ => null,
            };
        }

        protected override PassThroughInputManager CreateInputManager() => new SticksInputManager(Ruleset?.RulesetInfo);

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            float step = e.CurrentState.Keyboard.ShiftPressed ? fine_approach_rate_step : approach_rate_step;

            switch (e.Action)
            {
                case GlobalAction.IncreaseScrollSpeed:
                    adjustApproachRate(step);
                    return true;

                case GlobalAction.DecreaseScrollSpeed:
                    adjustApproachRate(-step);
                    return true;

                default:
                    return false;
            }
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        private void adjustApproachRate(float amount)
        {
            if (player?.AllowCriticalSettingsAdjustment == false)
                return;

            approachRate.Value += amount;
        }

        private void applyApproachRate(float value)
        {
            foreach (SticksHitObject hitObject in Beatmap.HitObjects)
                hitObject.ApplyPlayerApproachRate(value);

            foreach (DrawableHitObject drawable in Playfield.AllHitObjects)
                refreshApproachTransforms(drawable);
        }

        private static void refreshApproachTransforms(DrawableHitObject drawable)
        {
            if (drawable is ISticksApproachRateAdjustable adjustable)
                adjustable.RefreshApproachTransforms();

            foreach (DrawableHitObject nested in drawable.NestedHitObjects)
                refreshApproachTransforms(nested);
        }
    }
}
