using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Rulesets.Difficulty;

namespace osu.Game.Rulesets.Sticks.Difficulty
{
    /// <summary>
    /// Local performance values for Sticks scores.
    /// </summary>
    public class SticksPerformanceAttributes : PerformanceAttributes
    {
        [JsonProperty("mechanical")]
        public double Mechanical { get; set; }

        [JsonProperty("reading")]
        public double Reading { get; set; }

        [JsonProperty("control")]
        public double Control { get; set; }

        [JsonProperty("coordination")]
        public double Coordination { get; set; }

        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }

        [JsonProperty("effective_miss_count")]
        public double EffectiveMissCount { get; set; }

        public override IEnumerable<PerformanceDisplayAttribute> GetAttributesForDisplay()
        {
            foreach (PerformanceDisplayAttribute attribute in base.GetAttributesForDisplay())
                yield return attribute;

            yield return new PerformanceDisplayAttribute(nameof(Mechanical), "Mechanical", Mechanical);
            yield return new PerformanceDisplayAttribute(nameof(Reading), "Reading", Reading);
            yield return new PerformanceDisplayAttribute(nameof(Control), "Control", Control);
            yield return new PerformanceDisplayAttribute(nameof(Coordination), "Coordination", Coordination);
            yield return new PerformanceDisplayAttribute(nameof(Accuracy), "Accuracy", Accuracy);
        }
    }
}
