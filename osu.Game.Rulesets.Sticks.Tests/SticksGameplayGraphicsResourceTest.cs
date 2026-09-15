using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    [NonParallelizable]
    public partial class SticksGameplayGraphicsResourceTest
    {
        [Test]
        [Explicit("Requires a desktop graphics renderer; the ordinary gameplay resource fixture runs headlessly in CI.")]
        public void TestGameplayResourcesWithGraphicsRenderer()
        {
            // Isolate configuration and databases. Leaving framework.ini beside the runner
            // makes later headless tests treat its entire binary directory as disposable storage.
            using var storage = new TemporaryNativeStorage($"sticks-gameplay-graphics-{Guid.NewGuid()}");
            using var host = Host.GetSuitableDesktopHost("sticks-gameplay-resource-tests", new HostOptions { PortableInstallation = true });
            using var game = new IsolatedTestRunner(storage);
            using var stopped = new CancellationTokenSource();
            Task tests = Task.Run(() =>
            {
                try
                {
                    while (!game.IsLoaded && !stopped.IsCancellationRequested)
                        Thread.Sleep(10);
                    if (!game.IsLoaded)
                        throw new InvalidOperationException("The graphics host could not start. Check the runtime log for the renderer failure.");

                    foreach (Action<SticksGameplayResourceTest> configure in new Action<SticksGameplayResourceTest>[]
                             {
                                 scene => scene.TestRepeatedGameplayExitReleasesPlayfieldsAndReplayRecorders(),
                                 scene => scene.TestSustainedGameplayTrailsAndEffectsHaveBoundedMemory(),
                                 scene => scene.TestRepeatedGameplaySkinSwitchReleasesOldTextures(),
                             })
                    {
                        using var scene = new SticksGameplayResourceTest { RequireGraphicsRendering = true };
                        scene.SetUpResources();
                        configure(scene);
                        scene.TearDownResources();
                        game.RunTestBlocking(scene);
                    }
                }
                finally
                {
                    host.Exit();
                }
            });

            try
            {
                host.Run(game);
            }
            finally
            {
                stopped.Cancel();
            }
            tests.GetAwaiter().GetResult();
        }

        private partial class IsolatedTestRunner : OsuTestScene.OsuTestSceneTestRunner
        {
            private readonly Storage storage;

            public IsolatedTestRunner(Storage storage)
            {
                this.storage = storage;
            }

            protected override Storage CreateStorage(GameHost host, Storage defaultStorage) => base.CreateStorage(host, storage);
        }
    }
}
