using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.Osu;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    [NonParallelizable]
    public partial class SticksControllerSelectionTest : OsuTestScene
    {
        [Resolved]
        private GameHost host { get; set; }

        private ImmutableArray<InputHandler> originalHandlers;
        private JoystickHandler joystick;
        private ToolbarRulesetTabButton button;

        private static readonly PropertyInfo availableHandlers = typeof(GameHost).GetProperty(nameof(GameHost.AvailableInputHandlers))!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<IBindable<RulesetInfo>>(Ruleset);
            return dependencies;
        }

        [SetUpSteps]
        public void SetUpController() => AddStep("install disabled headless joystick", () =>
        {
            Clear();
            originalHandlers = host.AvailableInputHandlers;
            joystick = new JoystickHandler();
            joystick.Enabled.Value = false;
            // Headless hosts expose no joystick devices. Add an uninitialised handler to
            // exercise the real icon/host setting path without SDL or physical hardware.
            availableHandlers.SetValue(host, originalHandlers.Add(joystick));
        });

        [TestCase(false)]
        [TestCase(true)]
        public void TestSelectionEnablesGamepadBeforeGameplay(bool initiallySelected)
        {
            AddStep("load toolbar button with gamepad disabled", () =>
            {
                Ruleset.Value = initiallySelected ? new SticksRuleset().RulesetInfo : new OsuRuleset().RulesetInfo;
                joystick.Enabled.Value = false;
                Add(button = new ToolbarRulesetTabButton(new SticksRuleset().RulesetInfo) { Height = 50 });
            });
            AddUntilStep("toolbar icon loaded", () => button.ChildrenOfType<SticksRulesetIcon>().SingleOrDefault()?.IsLoaded == true);
            AddAssert("only the selected ruleset enables input on load", () => joystick.Enabled.Value == initiallySelected);
            AddStep("switch away and disable gamepad", () =>
            {
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                joystick.Enabled.Value = false;
            });
            AddWaitStep("keep another ruleset selected", 3);
            AddAssert("Sticks icon alone does not enable input", () => !joystick.Enabled.Value);
            AddStep("switch to Sticks without starting a map", () => Ruleset.Value = new SticksRuleset().RulesetInfo);
            AddAssert("selection immediately enables gamepad", () => joystick.Enabled.Value);
            AddStep("hide toolbar and switch away", () =>
            {
                button.Alpha = 0;
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                joystick.Enabled.Value = false;
            });
            AddStep("switch back while toolbar hidden", () => Ruleset.Value = new SticksRuleset().RulesetInfo);
            AddAssert("hidden toolbar still observes selection", () => joystick.Enabled.Value);
            AddStep("dispose toolbar button", () =>
            {
                Remove(button, false);
                button.Dispose();
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                joystick.Enabled.Value = false;
            });
            AddStep("switch after disposal", () => Ruleset.Value = new SticksRuleset().RulesetInfo);
            AddAssert("disposed icon no longer observes selection", () => !joystick.Enabled.Value);
        }

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
