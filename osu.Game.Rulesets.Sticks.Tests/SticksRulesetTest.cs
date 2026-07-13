// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Effects;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public partial class SticksRulesetTest
    {
        [Test]
        public void TestRulesetIsDiscoverable()
        {
            var assembly = typeof(SticksRuleset).Assembly;
            Type[] rulesets = assembly.GetTypes().Where(type => type.IsPublic && type.IsSubclassOf(typeof(Ruleset))).ToArray();
            var icon = (SticksRulesetIcon)new SticksRuleset().CreateIcon();

            Assert.Multiple(() =>
            {
                Assert.That(rulesets, Has.Length.EqualTo(1));
                Assert.That(Activator.CreateInstance(rulesets.Single()), Is.TypeOf<SticksRuleset>());
                Assert.That(new SticksRuleset().Description, Is.EqualTo("Sticks"));
                Assert.That(new SticksRuleset().ShortName, Is.EqualTo("sticks"));
                Assert.That(new SticksRuleset().GetModsFor(ModType.Conversion).Single(), Is.TypeOf<SticksModDifficultyAdjust>());
                Assert.That(new SticksRuleset().GetModsFor(ModType.Automation).Single(), Is.TypeOf<SticksModAutoplay>());
                Assert.That(new SticksRuleset().CreateConfig(null), Is.TypeOf<SticksRulesetConfigManager>());
                Assert.That(new SticksRuleset().CreateSettings(), Is.TypeOf<SticksSettingsSubsection>());
                Assert.That(new SticksRuleset().CreateHitObjectComposer(), Is.TypeOf<SticksHitObjectComposer>());
                Assert.That(icon.RelativeSizeAxes, Is.EqualTo(Axes.None));
                Assert.That(icon.Size, Is.EqualTo(new osuTK.Vector2(32)));
                Assert.That(new SticksJudgement().MaxResult, Is.EqualTo(HitResult.Great));
                Assert.That(new SticksRuleset().GetValidHitResults(), Is.EqualTo(new[]
                {
                    HitResult.Great,
                    HitResult.Ok,
                    HitResult.Miss,
                    HitResult.LargeTickHit,
                    HitResult.LargeTickMiss,
                    HitResult.IgnoreMiss,
                }));
            });
        }

        [Test]
        public void TestEditorCircularCoordinates()
        {
            foreach (StickSide side in Enum.GetValues<StickSide>())
            {
                foreach (float angle in new[] { 0f, 45f, 180f, 315f })
                {
                    var position = SticksEditorCoordinates.PositionFor(side, angle);

                    Assert.That(SticksEditorCoordinates.TryGetPlacement(position, out StickSide decodedSide, out float decodedAngle), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(decodedSide, Is.EqualTo(side));
                        Assert.That(decodedAngle, Is.EqualTo(angle).Within(0.001f));
                    });
                }
            }

            Assert.That(SticksEditorCoordinates.TryGetPlacement(SticksEditorCoordinates.Centre, out _, out _), Is.False);
        }

        [Test]
        public void TestEditorPlacementPreservesOppositeStickChord()
        {
            var placement = new SticksFlickPlacementBlueprint();
            placement.HitObject.StartTime = 1000;
            placement.HitObject.Side = StickSide.Left;

            Assert.Multiple(() =>
            {
                Assert.That(placement.ReplacesExistingObject(new SticksFlick
                {
                    StartTime = 1000,
                    Side = StickSide.Left,
                }), Is.True);
                Assert.That(placement.ReplacesExistingObject(new SticksFlick
                {
                    StartTime = 1000,
                    Side = StickSide.Right,
                }), Is.False);
            });
        }

        [Test]
        public void TestEditorAdjustmentsClampLogically()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(82, false), Is.EqualTo(82));
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(82, true), Is.EqualTo(75));
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(-82, true), Is.EqualTo(-75));
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(2, true), Is.EqualTo(15));
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(-2, true), Is.EqualTo(-15));
                Assert.That(SticksSelectionBlueprint.AdjustDraggedArcAngle(0.25f, false), Is.EqualTo(1));

                Assert.That(SticksEditorCoordinates.SnapAngle(7.49f), Is.EqualTo(0));
                Assert.That(SticksEditorCoordinates.SnapAngle(7.5f), Is.EqualTo(15));
                Assert.That(SticksEditorCoordinates.SnapAngle(82), Is.EqualTo(75));
                Assert.That(SticksEditorCoordinates.SnapAngle(352.5f), Is.EqualTo(0));
                Assert.That(SticksEditorCoordinates.SnapAngleOffset(22), Is.EqualTo(15));
                Assert.That(SticksEditorCoordinates.SnapAngleOffset(-22), Is.EqualTo(-15));
                Assert.That(SticksEditorCoordinates.SnapAngleOffset(352.5f), Is.EqualTo(360));
                Assert.That(SticksEditorCoordinates.SnapAngleOffset(-352.5f), Is.EqualTo(-360));

                Assert.That(SticksSelectionBlueprint.ReversalArcTo(90, 0, 1, false), Is.EqualTo(-90));
                Assert.That(SticksSelectionBlueprint.ReversalArcTo(90, 180, -1, false), Is.EqualTo(90));
                Assert.That(SticksSelectionBlueprint.ReversalArcTo(90, 2, 1, true), Is.EqualTo(-90));
            });
        }

        [Test]
        public void TestEditorMarkersRefreshLaneAndDirectionInPlace()
        {
            var arc = new SticksArcMarker(StickSide.Left, SticksPlayfield.LEFT_COLOUR);
            arc.SetLane(StickSide.Right, SticksPlayfield.RIGHT_COLOUR);
            arc.Angle = 135;

            var sliderHead = new SticksSliderHeadMarker(StickSide.Left, 1, SticksPlayfield.LEFT_COLOUR);
            sliderHead.SetLaneAndDirection(StickSide.Right, -1, SticksPlayfield.RIGHT_COLOUR);
            sliderHead.Angle = 225;

            Assert.Multiple(() =>
            {
                Assert.That(arc.Side, Is.EqualTo(StickSide.Right));
                Assert.That(arc.Angle, Is.EqualTo(135));
                Assert.That(sliderHead.Side, Is.EqualTo(StickSide.Right));
                Assert.That(sliderHead.Direction, Is.EqualTo(-1));
                Assert.That(sliderHead.Angle, Is.EqualTo(225));
            });
        }

        [Test]
        public void TestSelectedObjectsOutsideLifetimeDoNotForceBlueprintRendering()
        {
            var blueprint = new TestSticksSelectionBlueprint(new SticksFlick());
            Assert.That(blueprint.ForcesRenderingOutsideLifetime, Is.False);
        }

        [Test]
        public void TestRingLayout()
        {
            var playfield = new SticksPlayfield();

            Assert.Multiple(() =>
            {
                Assert.That(playfield.RelativeSizeAxes, Is.EqualTo(Axes.Both));
                Assert.That(playfield.Size.X, Is.EqualTo(1));
                Assert.That(playfield.Size.Y, Is.EqualTo(1));
                Assert.That(SticksPlayfield.RadiusFor(StickSide.Left), Is.GreaterThan(SticksPlayfield.RadiusFor(StickSide.Right)));
                Assert.That(SticksPlayfield.RadiusFor(StickSide.Left) - SticksPlayfield.GUIDE_RADIUS, Is.EqualTo(SticksPlayfield.LANE_OFFSET));
                Assert.That(SticksPlayfield.GUIDE_RADIUS - SticksPlayfield.RadiusFor(StickSide.Right), Is.EqualTo(SticksPlayfield.LANE_OFFSET));
                Assert.That(SticksPlayfield.PointAt(0, 100).X, Is.EqualTo(SticksPlayfield.SIZE / 2 + 100).Within(0.001));
                Assert.That(SticksPlayfield.PointAt(90, 100).Y, Is.EqualTo(SticksPlayfield.SIZE / 2 + 100).Within(0.001));
                Assert.That(playfield.LeftStickCursor.Size, Is.EqualTo(new osuTK.Vector2(24)));
                Assert.That(playfield.LeftStickCursor.BorderThickness, Is.EqualTo(3));
                Assert.That(playfield.LeftStickCursor.BorderColour.TopLeft.SRGB, Is.EqualTo(osuTK.Graphics.Color4.White));
                Assert.That(playfield.LeftStickCursor.EdgeEffect.Type, Is.EqualTo(EdgeEffectType.Shadow));
            });
        }

        [Test]
        public void TestDifficultyAdjustSettings()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            };

            Assert.That(slider.ShowSyncedNoteLink, Is.True);
            Assert.That(slider.ChordLinkStyle, Is.EqualTo(ChordLinkStyle.ToCentre));

            var mod = new SticksModDifficultyAdjust
            {
                PrimaryHitAngle = { Value = 30 },
                SecondaryHitAngle = { Value = 10 },
                ShowCursorTrails = { Value = true },
                ShowSyncedNoteLinks = { Value = true },
                ChordLinkStyle = { Value = ChordLinkStyle.ToCentre },
            };

            var difficulty = new BeatmapDifficulty { ApproachRate = 5 };
            slider.ApplyDefaults(new ControlPointInfo(), difficulty);
            mod.ApplyToHitObject(slider);

            var drawableRuleset = new DrawableSticksRuleset(new SticksRuleset(), new Beatmap<SticksHitObject>());
            mod.ApplyToDrawableRuleset(drawableRuleset);

            Assert.Multiple(() =>
            {
                Assert.That(slider.PrimaryHitAngle, Is.EqualTo(30));
                Assert.That(slider.SecondaryHitAngle, Is.EqualTo(10));
                Assert.That(slider.PreciseHalfAngle, Is.EqualTo(15));
                Assert.That(slider.LenientHalfAngle, Is.EqualTo(20));
                Assert.That(slider.ResultForCurrentAngleError(15), Is.EqualTo(HitResult.Great));
                Assert.That(slider.ResultForCurrentAngleError(15.01f), Is.EqualTo(HitResult.Ok));
                Assert.That(slider.NestedHitObjects.Cast<SticksHitObject>(), Has.All.Matches<SticksHitObject>(nested =>
                    nested.PrimaryHitAngle == 30 && nested.SecondaryHitAngle == 10 && nested.ShowSyncedNoteLink));
                Assert.That(((SticksPlayfield)drawableRuleset.Playfield).ShowCursorTrails, Is.True);
                Assert.That(slider.ShowSyncedNoteLink, Is.True);
                Assert.That(slider.ChordLinkStyle, Is.EqualTo(ChordLinkStyle.ToCentre));
            });

            var centreLink = new SticksSyncedNoteLink(StickSide.Left, 0, StickSide.Right, 180, slider.ChordLinkStyle);
            Assert.Multiple(() =>
            {
                Assert.That(centreLink.Style, Is.EqualTo(ChordLinkStyle.ToCentre));
                Assert.That(SticksSyncedNoteLink.ColourFor(StickSide.Left), Is.EqualTo(SticksPlayfield.LEFT_COLOUR));
                Assert.That(SticksSyncedNoteLink.ColourFor(StickSide.Right), Is.EqualTo(SticksPlayfield.RIGHT_COLOUR));
                Assert.That(SticksSyncedNoteLink.AlphaAtGrowth(0), Is.EqualTo(0.45f).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtGrowth(1), Is.EqualTo(0.8f).Within(0.001));
            });

            mod.ShowSyncedNoteLinks.Value = false;
            mod.ChordLinkStyle.Value = ChordLinkStyle.BetweenNotes;
            mod.ApplyToHitObject(slider);
            Assert.Multiple(() =>
            {
                Assert.That(slider.ShowSyncedNoteLink, Is.False);
                Assert.That(slider.ChordLinkStyle, Is.EqualTo(ChordLinkStyle.BetweenNotes));
            });
        }

        [Test]
        public void TestPersistentApproachRateOverridesMapForDisplay()
        {
            var config = new SticksRulesetConfigManager(null, new SticksRuleset().RulesetInfo);
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            };
            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { ApproachRate = 10 });

            Assert.That(config.Get<float>(SticksRulesetSetting.ApproachRate), Is.EqualTo(5));

            config.SetValue(SticksRulesetSetting.ApproachRate, 8f);
            slider.ApplyPlayerApproachRate(config.Get<float>(SticksRulesetSetting.ApproachRate));

            Assert.Multiple(() =>
            {
                Assert.That(config.Get<float>(SticksRulesetSetting.ApproachRate), Is.EqualTo(8));
                Assert.That(slider.ApproachDuration, Is.EqualTo(640).Within(0.001));
                Assert.That(slider.NestedHitObjects.Cast<SticksHitObject>(), Has.All.Matches<SticksHitObject>(nested =>
                    Math.Abs(nested.ApproachDuration - 640) < 0.001));
            });
        }

        [Test]
        public void TestAutoplayControlsBothSticksAndTracksSliders()
        {
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 0,
            });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Right,
                Angle = 90,
            });

            var slider = new SticksSlider
            {
                StartTime = 2000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 90,
            };
            slider.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            beatmap.HitObjects.Add(slider);

            SticksReplayFrame[] frames = new SticksAutoGenerator(beatmap).Generate().Frames.Cast<SticksReplayFrame>().ToArray();
            SticksReplayFrame beforeChord = frames.Single(frame => frame.Time == 999);
            SticksReplayFrame chord = frames.Single(frame => frame.Time == 1000);
            SticksReplayFrame middleTick = frames.Single(frame => frame.Time == 2500);

            Assert.Multiple(() =>
            {
                Assert.That(beforeChord.LeftStick, Is.EqualTo(osuTK.Vector2.Zero));
                Assert.That(beforeChord.RightStick, Is.EqualTo(osuTK.Vector2.Zero));
                Assert.That(chord.LeftStick.X, Is.EqualTo(1).Within(0.001));
                Assert.That(chord.LeftStick.Y, Is.EqualTo(0).Within(0.001));
                Assert.That(chord.RightStick.X, Is.EqualTo(0).Within(0.001));
                Assert.That(chord.RightStick.Y, Is.EqualTo(1).Within(0.001));
                Assert.That(middleTick.LeftStick.Length, Is.EqualTo(1).Within(0.001));
                Assert.That(System.Math.Atan2(middleTick.LeftStick.Y, middleTick.LeftStick.X) * 180 / System.Math.PI, Is.EqualTo(45).Within(0.1));
            });
        }

        [Test]
        public void TestReplayInputProviderPreservesEveryUpdateWithoutRateLimiting()
        {
            var provider = new SticksReplayInputProvider();

            for (int i = 0; i < 1000; i++)
                provider.Update(new osuTK.Vector2(i / 1000f, 0), new osuTK.Vector2(0, -i / 1000f));

            (osuTK.Vector2 left, osuTK.Vector2 right) = provider.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(provider.Active, Is.True);
                Assert.That(left.X, Is.EqualTo(0.999f).Within(0.0001));
                Assert.That(right.Y, Is.EqualTo(-0.999f).Within(0.0001));
            });
        }

        [Test]
        public void TestDifficultyScalingSeparatesOrdinaryAndImpossibleFlickPatterns()
        {
            var ordinary = new List<SticksHitObject>();
            var impossible = new List<SticksHitObject>();

            for (int i = 0; i < 40; i++)
            {
                ordinary.Add(new SticksFlick
                {
                    StartTime = 1000 + i * 500,
                    Side = i % 2 == 0 ? StickSide.Left : StickSide.Right,
                    Angle = i % 2 == 0 ? 0 : 90,
                });
                impossible.Add(new SticksFlick
                {
                    StartTime = 1000 + i * 50,
                    Side = i % 2 == 0 ? StickSide.Left : StickSide.Right,
                    Angle = i % 2 == 0 ? 0 : 180,
                });
            }

            double ordinaryStars = SticksDifficultyCalculator.CalculateStarRating(ordinary);
            double impossibleStars = SticksDifficultyCalculator.CalculateStarRating(impossible);

            Assert.Multiple(() =>
            {
                Assert.That(ordinaryStars, Is.InRange(1, 3));
                Assert.That(impossibleStars, Is.GreaterThan(8));
                Assert.That(impossibleStars, Is.GreaterThan(ordinaryStars * 3));
            });
        }

        [Test]
        public void TestVeryFastSliderRatesAboveEightStars()
        {
            double stars = SticksDifficultyCalculator.CalculateStarRating(new[]
            {
                new SticksSlider
                {
                    StartTime = 1000,
                    Duration = 500,
                    Side = StickSide.Left,
                    Angle = 0,
                    ArcAngle = 270,
                },
            });

            Assert.That(stars, Is.GreaterThan(8));
        }

        [Test]
        public void TestSliderDrawableCanBeConstructed()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 500,
                Angle = 30,
                ArcAngle = 90,
                Side = StickSide.Left,
            };

            Assert.That(() => new DrawableSticksSlider(slider), Throws.Nothing);
        }

        [Test]
        public void TestHoldRailsPointAwayFromSharedGuideCircle()
        {
            var blue = new DrawableSticksHold(new SticksHold
            {
                StartTime = 1000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 0,
            });
            var red = new DrawableSticksHold(new SticksHold
            {
                StartTime = 1000,
                Duration = 1000,
                Side = StickSide.Right,
                Angle = 0,
            });
            float centre = SticksPlayfield.SIZE / 2;

            Assert.Multiple(() =>
            {
                Assert.That(blue.RailEnd.X - centre, Is.GreaterThan(blue.RailStart.X - centre));
                Assert.That(red.RailEnd.X - centre, Is.LessThan(red.RailStart.X - centre));
                Assert.That(SticksHold.REQUIRED_TRACKING_FRACTION, Is.EqualTo(0.65));
            });

            blue.HitObject.Angle = 225;
            blue.HitObject.Duration = 250;
            osuTK.Vector2 centrePoint = new osuTK.Vector2(centre);
            osuTK.Vector2 startOffset = blue.RailStart - centrePoint;
            osuTK.Vector2 endOffset = blue.RailEnd - centrePoint;
            float crossProduct = startOffset.X * endOffset.Y - startOffset.Y * endOffset.X;

            Assert.Multiple(() =>
            {
                Assert.That(crossProduct, Is.EqualTo(0).Within(0.01), "The refreshed hold rail must remain radial through the playfield centre.");
                Assert.That(endOffset.Length, Is.GreaterThan(startOffset.Length));
                Assert.That(blue.RailStart.X, Is.LessThan(centre));
                Assert.That(blue.RailStart.Y, Is.LessThan(centre));
            });
        }

        [Test]
        public void TestSliderHeadIndicatesInitialDirection()
        {
            var clockwise = new SticksSlider { Angle = 30, ArcAngle = 90, PrimaryHitAngle = 20 };
            var counterClockwise = new SticksSlider { Angle = 30, ArcAngle = -90, PrimaryHitAngle = 20 };
            var clockwiseMarker = new SticksSliderHeadMarker(clockwise.Side, clockwise.InitialDirection, Color4.White);
            var counterClockwiseMarker = new SticksSliderHeadMarker(counterClockwise.Side, counterClockwise.InitialDirection, Color4.White);

            Assert.Multiple(() =>
            {
                Assert.That(clockwise.InitialDirection, Is.EqualTo(1));
                Assert.That(counterClockwise.InitialDirection, Is.EqualTo(-1));
                Assert.That(clockwiseMarker.Direction, Is.EqualTo(1));
                Assert.That(counterClockwiseMarker.Direction, Is.EqualTo(-1));
            });
        }

        [Test]
        public void TestSliderRepeatDrawableUsesPostReversalDirection()
        {
            var repeat = new SticksSliderRepeat
            {
                Angle = 45,
                DirectionAfter = -1,
                PrimaryHitAngle = 20,
                SecondaryHitAngle = 20,
            };

            Assert.Multiple(() =>
            {
                Assert.That(repeat.DirectionAfter, Is.EqualTo(-1));
                Assert.That(() => new DrawableSticksSliderRepeat(repeat), Throws.Nothing);
                Assert.That(SticksSliderRepeat.IsAngleInRange(20, repeat.PrimaryHitAngle, repeat.SecondaryHitAngle), Is.True);
            });
        }

        [Test]
        public void TestSliderExtensionDrawableUsesContinuingDirection()
        {
            var extension = new SticksSliderExtension
            {
                StartTime = 2000,
                SliderStartTime = 1000,
                LoopDuration = 1000,
                Direction = 1,
                Side = StickSide.Left,
                Angle = 30,
            };

            Assert.Multiple(() =>
            {
                Assert.That(extension.PreemptDuration, Is.EqualTo(1850));
                Assert.That(() => new DrawableSticksSliderExtension(extension), Throws.Nothing);
            });
        }

        [Test]
        public void TestNotesSortAboveSliderBodies()
        {
            var container = new TestSticksHitObjectContainer();
            var slider = new DrawableSticksSlider(new SticksSlider { StartTime = 1000, Duration = 1000 });
            var flick = new DrawableSticksFlick(new SticksFlick { StartTime = 1500 });

            Assert.Multiple(() =>
            {
                Assert.That(container.CompareForTest(flick, slider), Is.GreaterThan(0));
                Assert.That(container.CompareForTest(slider, flick), Is.LessThan(0));
            });
        }

        [TestCase(0, HitResult.Great)]
        [TestCase(10, HitResult.Great)]
        [TestCase(10.01f, HitResult.Ok)]
        [TestCase(20, HitResult.Ok)]
        [TestCase(20.01f, HitResult.Miss)]
        public void TestArcAngleGrading(float error, HitResult expected)
        {
            Assert.That(SticksHitObject.ResultForAngleError(error), Is.EqualTo(expected));
        }

        [Test]
        public void TestApproachGrowthAcceleratesTowardsHitTime()
        {
            double earlyGrowth = SticksHitObject.ApproachGrowthProgress(0.25) - SticksHitObject.ApproachGrowthProgress(0);
            double lateGrowth = SticksHitObject.ApproachGrowthProgress(1) - SticksHitObject.ApproachGrowthProgress(0.75);

            Assert.Multiple(() =>
            {
                Assert.That(SticksHitObject.ApproachGrowthProgress(-1), Is.EqualTo(0));
                Assert.That(SticksHitObject.ApproachGrowthProgress(0.5), Is.EqualTo(0.125));
                Assert.That(SticksHitObject.ApproachGrowthProgress(2), Is.EqualTo(1));
                Assert.That(lateGrowth, Is.GreaterThan(earlyGrowth));
            });
        }

        [Test]
        public void TestAnimatedArcUsesTrueAngularSpan()
        {
            var marker = new SticksArcMarker(StickSide.Left, Color4.White, true) { Span = 4 };
            var sliderHead = new SticksSliderHeadMarker(StickSide.Left, 1, Color4.White, true) { Span = 4 };
            Assert.That(marker.Span, Is.EqualTo(4));
            Assert.That(sliderHead.Span, Is.EqualTo(4));
            marker.Span = 20;
            sliderHead.Span = 20;

            Assert.Multiple(() =>
            {
                Assert.That(marker.Span, Is.EqualTo(20));
                Assert.That(sliderHead.Span, Is.EqualTo(20));
            });
        }

        [Test]
        public void TestArcProgressMarkersSupportContinuousGrowth()
        {
            var marker = new SticksArcMarker(StickSide.Left, Color4.White) { Span = 10.01f };
            var sliderHead = new SticksSliderHeadMarker(StickSide.Left, 1, Color4.White) { Span = 10.01f };

            Assert.Multiple(() =>
            {
                Assert.That(marker.Span, Is.EqualTo(10.01f));
                Assert.That(sliderHead.Span, Is.EqualTo(10.01f));
            });
        }

        [Test]
        public void TestSliderTrackingWindowBeginsAtHeadHit()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            };

            Assert.Multiple(() =>
            {
                Assert.That(SticksSlider.REQUIRED_TRACKING_FRACTION, Is.EqualTo(0.5));
                Assert.That(slider.AvailableTrackingDuration(900), Is.EqualTo(1000));
                Assert.That(slider.AvailableTrackingDuration(1200), Is.EqualTo(800));
                Assert.That(new SticksSliderTailJudgement().MaxResult, Is.EqualTo(HitResult.Great));
                Assert.That(new SticksSliderTailJudgement().MinResult, Is.EqualTo(HitResult.IgnoreMiss));
                Assert.That(new SticksSliderTickJudgement().MaxResult, Is.EqualTo(HitResult.LargeTickHit));
                Assert.That(new SticksSliderTickJudgement().MinResult, Is.EqualTo(HitResult.LargeTickMiss));
                Assert.That(new SticksSliderRepeatJudgement().MaxResult, Is.EqualTo(HitResult.LargeTickHit));
                Assert.That(new SticksSliderRepeatJudgement().MinResult, Is.EqualTo(HitResult.LargeTickMiss));
                Assert.That(SticksSliderRepeat.IsAngleInRange(SticksHitObject.PRECISE_HALF_ANGLE + 1), Is.True);
                Assert.That(SticksSliderRepeat.IsAngleInRange(SticksHitObject.LENIENT_HALF_ANGLE), Is.True);
                Assert.That(SticksSliderRepeat.IsAngleInRange(SticksHitObject.LENIENT_HALF_ANGLE + 0.01f), Is.False);
            });
        }

        [Test]
        public void TestEditorSliderHeadSampleCrossing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DrawableSticksSlider.CrossedStartTime(999, 1000, 1000), Is.True);
                Assert.That(DrawableSticksSlider.CrossedStartTime(990, 1010, 1000), Is.True);
                Assert.That(DrawableSticksSlider.CrossedStartTime(1000, 1010, 1000), Is.False);
                Assert.That(DrawableSticksSlider.CrossedStartTime(1010, 990, 1000), Is.False);
                Assert.That(DrawableSticksSlider.CrossedStartTime(double.NaN, 1000, 1000), Is.False);
            });
        }

        [Test]
        public void TestMissingSliderHeadDoesNotResolveSliderEarly()
        {
            var drawable = new DrawableSticksSlider(new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            });

            Type drawableType = typeof(DrawableSticksSlider);
            drawableType.GetMethod("MarkHeadMiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawable, null);

            bool headJudged = (bool)drawableType.GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
            bool headHit = (bool)drawableType.GetProperty("HeadHit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
            bool hasResult = (bool)drawableType.GetProperty("HasResult", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;

            Assert.Multiple(() =>
            {
                Assert.That(headJudged, Is.True);
                Assert.That(headHit, Is.False);
                Assert.That(hasResult, Is.False);
            });
        }

        [Test]
        public void TestSliderRehearsalTracesOnceAtGameplaySpeed()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 500,
            };
            var longSlider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
            };

            Assert.Multiple(() =>
            {
                Assert.That(slider.RehearsalStartTime, Is.EqualTo(500));
                Assert.That(slider.RehearsalProgressAt(499), Is.EqualTo(0).Within(0.001));
                Assert.That(slider.RehearsalProgressAt(750), Is.EqualTo(0.5).Within(0.001));
                Assert.That(slider.RehearsalProgressAt(999), Is.EqualTo(0.998).Within(0.001));
                Assert.That(slider.RehearsalProgressAt(1000), Is.EqualTo(1).Within(0.001));
                Assert.That(slider.RehearsalProgressAt(1250), Is.EqualTo(1).Within(0.001));
                Assert.That(longSlider.RehearsalStartTime, Is.EqualTo(150));
                Assert.That(longSlider.RehearsalProgressAt(1000), Is.EqualTo(0.425).Within(0.001));
            });
        }

        [Test]
        public void TestCompletedSliderPathClearsCurrentSegment()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
                RepeatCount = 1,
            };

            Assert.Multiple(() =>
            {
                Assert.That(slider.RemainingPathRangeAt(1000), Is.EqualTo((0d, 1d)));
                Assert.That(slider.RemainingPathRangeAt(1500), Is.EqualTo((0.5d, 1d)));
                Assert.That(slider.RemainingPathRangeAt(2000), Is.EqualTo((0d, 1d)));
                Assert.That(slider.RemainingPathRangeAt(2500), Is.EqualTo((0.5d, 1d)));
                Assert.That(slider.RemainingPathRangeAt(3000), Is.EqualTo((1d, 1d)));
                Assert.That(slider.CurrentSpanEndsWithReversal(1500), Is.True);
                Assert.That(slider.CurrentSpanEndsWithReversal(2500), Is.False);
            });
        }

        [Test]
        public void TestSegmentedSliderKeepsOneAngularSpeed()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 3500,
                Angle = 10,
            };
            slider.SetCustomSegments(new[] { 90f, -180f, 45f });

            Assert.Multiple(() =>
            {
                Assert.That(slider.TotalAngularDistance, Is.EqualTo(315));
                Assert.That(slider.SegmentDurationAt(0), Is.EqualTo(1000).Within(0.001));
                Assert.That(slider.SegmentDurationAt(1), Is.EqualTo(2000).Within(0.001));
                Assert.That(slider.SegmentDurationAt(2), Is.EqualTo(500).Within(0.001));
                Assert.That(slider.AngleAt(2000), Is.EqualTo(100).Within(0.001));
                Assert.That(slider.AngleAt(4000), Is.EqualTo(-80).Within(0.001));
                Assert.That(slider.AngleAt(4500), Is.EqualTo(-35).Within(0.001));
            });

            double speed = slider.TotalAngularDistance / slider.Duration;
            slider.AppendSegmentAtConstantSpeed(-90);
            Assert.Multiple(() =>
            {
                Assert.That(slider.Duration, Is.EqualTo(4500).Within(0.001));
                Assert.That(slider.TotalAngularDistance / slider.Duration, Is.EqualTo(speed).Within(0.000001));
                Assert.That(slider.RemoveFinalSegmentAtConstantSpeed(), Is.True);
                Assert.That(slider.Duration, Is.EqualTo(3500).Within(0.001));
                Assert.That(slider.TotalAngularDistance / slider.Duration, Is.EqualTo(speed).Within(0.000001));
            });
        }

        private partial class TestSticksHitObjectContainer : SticksHitObjectContainer
        {
            public int CompareForTest(Drawable x, Drawable y) => Compare(x, y);
        }

        private partial class TestSticksSelectionBlueprint : SticksSelectionBlueprint
        {
            public bool ForcesRenderingOutsideLifetime => AlwaysShowWhenSelected;

            public TestSticksSelectionBlueprint(SticksHitObject hitObject)
                : base(hitObject)
            {
            }
        }
    }
}
