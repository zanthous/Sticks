using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
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

        internal double PlayerApproachDuration => SticksHitObject.ApproachDurationFor(Config.Get<float>(SticksRulesetSetting.ApproachRate));

        private readonly BindableFloat approachRate = new BindableFloat();
        private readonly BindableFloat flickActivationThreshold = new BindableFloat();
        private readonly BindableBool hideInactiveCursors = new BindableBool();
        private readonly BindableBool sliderTrackingSparks = new BindableBool();
        private readonly Bindable<SticksHitEffectMode> hitEffects = new Bindable<SticksHitEffectMode>(SticksHitEffectMode.Perfect);
        private readonly BindableBool showCursorTrails = new BindableBool();
        private readonly BindableBool useSkinColours = new BindableBool(true);
        private readonly BindableBool saveReplays = new BindableBool(true);
        private readonly Bindable<Colour4> leftStickColour = new Bindable<Colour4>((Colour4)SticksPlayfield.LEFT_COLOUR);
        private readonly Bindable<Colour4> rightStickColour = new Bindable<Colour4>((Colour4)SticksPlayfield.RIGHT_COLOUR);
        private readonly Bindable<Colour4> overlapColour = new Bindable<Colour4>((Colour4)SticksPlayfield.OVERLAP_COLOUR);
        private readonly SticksReplayInputProvider replayInputProvider = new SticksReplayInputProvider();
        private SticksReplayStore replayStore;
        private SticksReplayPersistence replayPersistence;

        [Resolved(CanBeNull = true)]
        private RealmAccess realm { get; set; }

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public DrawableSticksRuleset(SticksRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods = null)
            : base(ruleset, beatmap, mods)
        {
        }

        [BackgroundDependencyLoader]
        private void load(Storage storage)
        {
            replayStore = new SticksReplayStore(storage);
            if (realm != null)
                replayPersistence = new SticksReplayPersistence(replayStore, realm);

            Config.BindWith(SticksRulesetSetting.ApproachRate, approachRate);
            approachRate.BindValueChanged(rate => applyApproachRate(rate.NewValue), true);
            Config.BindWith(SticksRulesetSetting.FlickActivationThreshold, flickActivationThreshold);
            flickActivationThreshold.BindValueChanged(threshold =>
                ((SticksPlayfield)Playfield).FlickActivationThreshold = threshold.NewValue, true);
            Config.BindWith(SticksRulesetSetting.HideInactiveCursors, hideInactiveCursors);
            hideInactiveCursors.BindValueChanged(hidden =>
                ((SticksPlayfield)Playfield).HideInactiveCursors = hidden.NewValue, true);
            Config.BindWith(SticksRulesetSetting.SliderTrackingSparks, sliderTrackingSparks);
            sliderTrackingSparks.BindValueChanged(enabled =>
                ((SticksPlayfield)Playfield).SliderTrackingSparks = enabled.NewValue, true);
            Config.BindWith(SticksRulesetSetting.HitEffects, hitEffects);
            hitEffects.BindValueChanged(mode =>
                ((SticksPlayfield)Playfield).HitEffects = mode.NewValue, true);
            Config.BindWith(SticksRulesetSetting.ShowCursorTrails, showCursorTrails);
            showCursorTrails.BindValueChanged(enabled =>
                ((SticksPlayfield)Playfield).ShowCursorTrails = enabled.NewValue, true);
            Config.BindWith(SticksRulesetSetting.UseSkinColours, useSkinColours);
            useSkinColours.BindValueChanged(enabled =>
                ((SticksPlayfield)Playfield).UseSkinColours = enabled.NewValue, true);
            Config.BindWith(SticksRulesetSetting.SaveReplays, saveReplays);
            Config.BindWith(SticksRulesetSetting.LeftStickColour, leftStickColour);
            Config.BindWith(SticksRulesetSetting.RightStickColour, rightStickColour);
            Config.BindWith(SticksRulesetSetting.OverlapColour, overlapColour);
            leftStickColour.BindValueChanged(_ => applyColours(), true);
            rightStickColour.BindValueChanged(_ => applyColours(), true);
            overlapColour.BindValueChanged(_ => applyColours(), true);
        }

        public override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer() => new SticksPlayfieldAdjustmentContainer();

        protected override Playfield CreatePlayfield() => new SticksPlayfield(replayInputProvider);

        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new SticksFramedReplayInputHandler(
            replay,
            replayInputProvider,
            () => ((SticksPlayfield)Playfield).PhysicalStickDistanceAtGameEdge,
            () => ((SticksPlayfield)Playfield).FlickActivationThreshold);

        protected override ReplayRecorder CreateReplayRecorder(Score score)
        {
            if (saveReplays.Value)
                replayPersistence?.Track(score);

            return new SticksReplayRecorder(score, (SticksPlayfield)Playfield);
        }

        public override void SetReplayScore(Score replayScore)
        {
            // Unlike a normal player, editor test play can toggle autoplay on and off while the
            // same drawable ruleset remains alive. Do not retain the last bot stick position
            // while lazer replaces or removes its replay handler.
            replayInputProvider.Deactivate();

            if (replayScore != null)
                replayStore?.TryRestore(replayScore, replaceExisting: true);

            base.SetReplayScore(replayScore);
        }

        public override DrawableHitObject<SticksHitObject> CreateDrawableRepresentation(SticksHitObject hitObject)
        {
            hitObject.ApplyPlayerApproachRate(Config.Get<float>(SticksRulesetSetting.ApproachRate));

            DrawableHitObject<SticksHitObject> drawable = hitObject switch
            {
                SticksSlider slider => new DrawableSticksSlider(slider),
                SticksHold hold => new DrawableSticksHold(hold),
                SticksClick click => new DrawableSticksClick(click),
                SticksFlick flick => new DrawableSticksFlick(flick),
                _ => null,
            };

            if (drawable != null)
                drawable.HitObjectApplied += restorePlayerApproachRate;

            return drawable;
        }

        private void restorePlayerApproachRate(DrawableHitObject drawable)
        {
            // Editing reapplies map defaults and rebuilds slider checkpoints without
            // recreating the drawable. Restore the display preference on every apply.
            ((SticksHitObject)drawable.HitObject).ApplyPlayerApproachRate(Config.Get<float>(SticksRulesetSetting.ApproachRate));
            refreshApproachTransforms(drawable);
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

        private void applyColours() => ((SticksPlayfield)Playfield).SetColours(
            leftStickColour.Value,
            rightStickColour.Value,
            overlapColour.Value);

        protected override void Dispose(bool isDisposing)
        {
            // Dispose before the drawable hierarchy. If an incomplete recorder is detached as part
            // of a retry or quit, it must not begin a new persistence operation while children expire.
            replayPersistence?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
