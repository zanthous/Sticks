using System;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace osu.Game.Rulesets.Sticks.Tests
{
    internal static class NUnitCompatibility
    {
        public static void Multiple(TestDelegate action) => Assert.Multiple(action);

        public static void That(TestDelegate action, IResolveConstraint expression) => Assert.That(action, expression);

        public static void That(TestDelegate action, IResolveConstraint expression, string message) =>
            Assert.That(action, expression, message);

        public static TException Throws<TException>(TestDelegate action)
            where TException : Exception =>
            Assert.Throws<TException>(action)!;
    }
}
