using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksPerspectiveTest : PlayerTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new SticksRuleset();

        private SticksRulesetConfigManager config => (SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(Ruleset.Value.CreateInstance())!;
        private SticksPlayfield playfield => (SticksPlayfield)Player.DrawableRuleset.Playfield;

        [Test]
        public void TestGameplayShowsTheTunnel()
        {
            AddUntilStep("player loaded", () => Player.IsLoaded);
            AddAssert("flat by default", () => playfield.PerspectiveDepth == 1);
            AddStep("full perspective", () => config.SetValue(SticksRulesetSetting.Perspective, 1f));
            AddAssert("notes come from four times deeper", () => playfield.PerspectiveDepth == 4);
            AddAssert("notes emerge from the centre", () => near(playfield.ApproachRadiusAt(0, 1000, 1000), 0));
            AddAssert("a note halfway through its approach is a fifth of the way out",
                () => near(playfield.ApproachRadiusAt(500, 1000, 1000), SticksPlayfield.GUIDE_RADIUS * 0.2f));
            AddAssert("notes still reach the ring at their hit time",
                () => near(playfield.ApproachRadiusAt(1000, 1000, 1000), SticksPlayfield.GUIDE_RADIUS));
            AddStep("restore default", () => config.GetBindable<float>(SticksRulesetSetting.Perspective).SetDefault());
        }

        private static bool near(float actual, float expected) => System.Math.Abs(actual - expected) < 0.01f;
    }
}
