using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    [NonParallelizable]
    public partial class SticksRawStickInputTest : OsuTestScene
    {
        [Resolved]
        private GameHost host { get; set; }

        private ImmutableArray<InputHandler> originalHandlers;
        private JoystickHandler joystick;
        private SticksRawStickInput reader;
        private SticksRawStickInput secondReader;
        private DrawableSticksRuleset drawable;

        private static readonly PropertyInfo availableHandlers = typeof(GameHost).GetProperty(nameof(GameHost.AvailableInputHandlers))!;

        private SticksRulesetConfigManager config => (SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(new SticksRuleset())!;
        private float remembered => config.Get<float>(SticksRulesetSetting.OsuJoystickDeadzone);
        private float deadzone => joystick.DeadzoneThreshold.Value;

        [SetUpSteps]
        public void SetUpController() => AddStep("install headless joystick", () =>
        {
            Clear();
            originalHandlers = host.AvailableInputHandlers;
            // Headless hosts expose no joystick devices. Add an uninitialised handler to
            // exercise the real host setting without SDL or physical hardware.
            joystick = new JoystickHandler();
            availableHandlers.SetValue(host, originalHandlers.Add(joystick));
            config.SetValue(SticksRulesetSetting.OsuJoystickDeadzone, SticksRawStickInput.NOTHING_REMEMBERED);
        });

        [Test]
        public void TestDeadzoneIsOffOnlyWhileTheControllerIsRead()
        {
            AddStep("player uses a 25% deadzone", () => joystick.DeadzoneThreshold.Value = 0.25f);
            AddStep("start reading", () => reader = SticksRawStickInput.Acquire(host, RulesetConfigs));
            AddAssert("switched off", () => deadzone, () => Is.Zero);
            AddAssert("player's value remembered", () => remembered, () => Is.EqualTo(0.25f).Within(0.0001));
            AddStep("stop reading", () => reader.Dispose());
            AddUntilStep("player's value is back", () => deadzone, () => Is.EqualTo(0.25f).Within(0.0001));
            AddAssert("nothing left to put back", () => remembered, () => Is.EqualTo(SticksRawStickInput.NOTHING_REMEMBERED));
        }

        [Test]
        public void TestDeadzoneStaysOffUntilTheLastReaderStops()
        {
            AddStep("player uses a 10% deadzone", () => joystick.DeadzoneThreshold.Value = 0.1f);
            AddStep("two readers, as during a retry", () =>
            {
                reader = SticksRawStickInput.Acquire(host, RulesetConfigs);
                secondReader = SticksRawStickInput.Acquire(host, RulesetConfigs);
            });
            AddStep("first stops, twice", () =>
            {
                reader.Dispose();
                reader.Dispose();
            });
            AddWaitStep("let the release run", 3);
            AddAssert("still off", () => deadzone, () => Is.Zero);
            AddStep("second stops", () => secondReader.Dispose());
            AddUntilStep("player's value is back", () => deadzone, () => Is.EqualTo(0.1f).Within(0.0001));
        }

        [Test]
        public void TestChangingTheSettingDuringPlayIsUndoneAndKeptForLater()
        {
            AddStep("player uses a 10% deadzone", () => joystick.DeadzoneThreshold.Value = 0.1f);
            AddStep("start reading", () => reader = SticksRawStickInput.Acquire(host, RulesetConfigs));
            AddStep("player raises it to 60% in the settings", () => joystick.DeadzoneThreshold.Value = 0.6f);
            AddStep("next frame", () => reader.Enforce());
            AddAssert("switched off again", () => deadzone, () => Is.Zero);
            AddStep("stop reading", () => reader.Dispose());
            AddUntilStep("the newer choice is put back", () => deadzone, () => Is.EqualTo(0.6f).Within(0.0001));
        }

        [Test]
        public void TestNothingIsRememberedWhenThePlayerAlreadyUsesNoDeadzone()
        {
            AddStep("player uses no deadzone", () => joystick.DeadzoneThreshold.Value = 0);
            AddStep("start reading", () => reader = SticksRawStickInput.Acquire(host, RulesetConfigs));
            AddAssert("nothing remembered", () => remembered, () => Is.EqualTo(SticksRawStickInput.NOTHING_REMEMBERED));
            AddStep("stop reading", () => reader.Dispose());
            AddWaitStep("let the release run", 3);
            AddAssert("still none", () => deadzone, () => Is.Zero);
        }

        [Test]
        public void TestNextLaunchPutsTheDeadzoneBackAfterACrash()
        {
            AddStep("previous run ended during play", () =>
            {
                joystick.DeadzoneThreshold.Value = 0;
                config.SetValue(SticksRulesetSetting.OsuJoystickDeadzone, 0.2f);
            });
            AddStep("startup", () => SticksRawStickInput.RestoreIfUnused(host, RulesetConfigs));
            AddAssert("player's value is back", () => deadzone, () => Is.EqualTo(0.2f).Within(0.0001));
            AddAssert("nothing left to put back", () => remembered, () => Is.EqualTo(SticksRawStickInput.NOTHING_REMEMBERED));

            AddStep("player set a new value before the next launch", () =>
            {
                joystick.DeadzoneThreshold.Value = 0.15f;
                config.SetValue(SticksRulesetSetting.OsuJoystickDeadzone, 0.2f);
            });
            AddStep("startup", () => SticksRawStickInput.RestoreIfUnused(host, RulesetConfigs));
            AddAssert("their newer value is kept", () => deadzone, () => Is.EqualTo(0.15f).Within(0.0001));
            AddAssert("nothing left to put back", () => remembered, () => Is.EqualTo(SticksRawStickInput.NOTHING_REMEMBERED));
        }

        [Test]
        public void TestStartupLeavesTheDeadzoneOffWhileItIsBeingRead()
        {
            AddStep("player uses a 10% deadzone", () => joystick.DeadzoneThreshold.Value = 0.1f);
            AddStep("start reading", () => reader = SticksRawStickInput.Acquire(host, RulesetConfigs));
            AddStep("another icon loads", () => SticksRawStickInput.RestoreIfUnused(host, RulesetConfigs));
            AddAssert("still off", () => deadzone, () => Is.Zero);
            AddStep("stop reading", () => reader.Dispose());
            AddUntilStep("player's value is back", () => deadzone, () => Is.EqualTo(0.1f).Within(0.0001));
        }

        [Test]
        public void TestGameplayReadsTheControllerRawAndLeavesReplaysAlone()
        {
            SticksReplayInputProvider replay = null;

            AddStep("player uses a 10% deadzone", () => joystick.DeadzoneThreshold.Value = 0.1f);
            AddStep("load gameplay", () =>
            {
                var ruleset = new SticksRuleset();
                drawable = new DrawableSticksRuleset(ruleset, new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                });
                replay = (SticksReplayInputProvider)typeof(DrawableSticksRuleset)
                                                    .GetField("replayInputProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drawable)!;
                Add(drawable);
            });
            AddUntilStep("loaded", () => drawable.IsLoaded);
            AddAssert("osu!'s deadzone is off", () => deadzone, () => Is.Zero);

            AddStep("controller held half way out", () =>
            {
                setController("leftX", 0.3f);
                setController("leftY", 0.4f);
            });
            AddWaitStep("update", 2);
            AddAssert("controller is converted to stick units",
                () => near(playfield.StickVector(StickSide.Left), SticksStickResponse.FromRaw(new Vector2(0.3f, 0.4f))));
            AddAssert("which differ from the real position here", () => playfield.StickVector(StickSide.Left).Length, () => Is.EqualTo(0.4314f).Within(0.0005));
            AddAssert("the recorder stores the same stick units",
                () => near(playfield.PhysicalStickVector(StickSide.Left), playfield.StickVector(StickSide.Left)));

            AddStep("replay holds the same numbers", () => replay.Update(new Vector2(0.3f, 0.4f), new Vector2(-0.07f, 0.02f)));
            AddWaitStep("update", 2);
            AddAssert("replay positions are used as recorded", () => near(playfield.StickVector(StickSide.Left), new Vector2(0.3f, 0.4f)));
            AddAssert("including ones near the centre", () => near(playfield.StickVector(StickSide.Right), new Vector2(-0.07f, 0.02f)));

            AddStep("leave gameplay", () =>
            {
                Remove(drawable, false);
                drawable.Dispose();
            });
            AddUntilStep("player's value is back", () => deadzone, () => Is.EqualTo(0.1f).Within(0.0001));
        }

        private SticksPlayfield playfield => (SticksPlayfield)drawable.Playfield;

        private static bool near(Vector2 actual, Vector2 expected) => (actual - expected).Length < 0.00001f;

        private void setController(string axis, float value) =>
            typeof(SticksPlayfield).GetField(axis, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(playfield, value);

        [TearDownSteps]
        public void TearDownController() => AddStep("restore host input handlers", restoreHandlers);

        private void restoreHandlers()
        {
            if (originalHandlers.IsDefault)
                return;

            availableHandlers.SetValue(host, originalHandlers);
            originalHandlers = default;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
                restoreHandlers();
            base.Dispose(isDisposing);
        }
    }
}
