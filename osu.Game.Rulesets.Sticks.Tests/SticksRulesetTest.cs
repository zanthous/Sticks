// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Effects;
using osu.Framework.Input.StateChanges;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Replays;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
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
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osuTK;
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
                    HitResult.Meh,
                    HitResult.Miss,
                    HitResult.LargeTickHit,
                    HitResult.LargeTickMiss,
                    HitResult.SliderTailHit,
                    HitResult.IgnoreHit,
                    HitResult.IgnoreMiss,
                }));
            });
        }

        [Test]
        public void TestCursorTrailSpritesAreEmbedded()
        {
            using var resources = new SticksRuleset().CreateResourceStore();

            Assert.Multiple(() =>
            {
                Assert.That(resources.Get("Textures/Cursors/blue.png"), Is.Not.Null);
                Assert.That(resources.Get("Textures/Cursors/red.png"), Is.Not.Null);
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

                Assert.That(SticksSelectionBlueprint.IsAtSliderEndTime(1500, 1500), Is.True);
                Assert.That(SticksSelectionBlueprint.IsAtSliderEndTime(1499.5, 1500), Is.True);
                Assert.That(SticksSelectionBlueprint.IsAtSliderEndTime(1499.49, 1500), Is.False);
                Assert.That(SticksSelectionBlueprint.IsAtSliderEndTime(1500.51, 1500), Is.False);
            });
        }

        [Test]
        public void TestTimedSliderContinuationPreservesSpeedAndExistingReversalTime()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                ArcAngle = 90,
            };

            Assert.Multiple(() =>
            {
                Assert.That(slider.ContinuationArcAt(2000), Is.Zero);
                Assert.That(slider.ContinuationArcAt(2500), Is.EqualTo(-45).Within(0.001));
                Assert.That(slider.AppendTimedSegmentAtConstantSpeed(2000), Is.False);
                Assert.That(slider.SegmentCount, Is.EqualTo(1), "An invalid preview must not mutate the slider.");
            });

            Assert.That(slider.AppendTimedSegmentAtConstantSpeed(2500), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(slider.SegmentCount, Is.EqualTo(2));
                Assert.That(slider.SegmentArcAngleAt(1), Is.EqualTo(-45).Within(0.001));
                Assert.That(slider.EndTime, Is.EqualTo(2500).Within(0.001));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(2000).Within(0.001));
                Assert.That(slider.SegmentEndTimeAt(1), Is.EqualTo(2500).Within(0.001));
                Assert.That(Math.Abs(slider.SegmentArcAngleAt(0)) / slider.SegmentDurationAt(0),
                    Is.EqualTo(Math.Abs(slider.SegmentArcAngleAt(1)) / slider.SegmentDurationAt(1)).Within(0.000001));
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

            var mod = new SticksModDifficultyAdjust
            {
                PrimaryHitAngle = { Value = 30 },
                SecondaryHitAngle = { Value = 10 },
                ShowCursorTrails = { Value = true },
                UseEightyPercentStickTravel = { Value = true },
                SpeedChange = { Value = 1.25 },
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
                Assert.That(mod.ApplyToRate(0), Is.EqualTo(1.25));
                Assert.That(mod.ApplyToRate(0, 0.8), Is.EqualTo(1));
                Assert.That(mod.IncompatibleMods, Does.Contain(typeof(ModRateAdjust)));
                Assert.That(mod.IncompatibleMods, Does.Contain(typeof(ModTimeRamp)));
                Assert.That(mod.IncompatibleMods, Does.Contain(typeof(ModAdaptiveSpeed)));
                Assert.That(slider.NestedHitObjects.Cast<SticksHitObject>(), Has.All.Matches<SticksHitObject>(nested =>
                    nested.PrimaryHitAngle == 30 && nested.SecondaryHitAngle == 10));
                Assert.That(((SticksPlayfield)drawableRuleset.Playfield).ShowCursorTrails, Is.True);
                Assert.That(((SticksPlayfield)drawableRuleset.Playfield).PhysicalStickDistanceAtGameEdge, Is.EqualTo(0.8f));
            });

            var centreLink = new SticksSyncedNoteLink(StickSide.Left, 0, StickSide.Right, 180);
            var sharedAngleLink = new SticksSyncedNoteLink(StickSide.Left, 0, StickSide.Right, 360);
            Assert.Multiple(() =>
            {
                Assert.That(SticksSyncedNoteLink.ColourFor(StickSide.Left), Is.EqualTo(SticksPlayfield.LEFT_COLOUR));
                Assert.That(SticksSyncedNoteLink.ColourFor(StickSide.Right), Is.EqualTo(SticksPlayfield.RIGHT_COLOUR));
                Assert.That(SticksSyncedNoteLink.AlphaAtGrowth(0), Is.EqualTo(0.45f).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtGrowth(1), Is.EqualTo(0.8f).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtHeadCue(999, 1000, 0.5), Is.EqualTo(SticksSyncedNoteLink.AlphaAtGrowth(0.5)).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtHeadCue(1000, 1000, 1), Is.EqualTo(0.8f).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtHeadCue(1060, 1000, 1), Is.EqualTo(0.4f).Within(0.001));
                Assert.That(SticksSyncedNoteLink.AlphaAtHeadCue(1120, 1000, 1), Is.Zero);
                Assert.That(centreLink.UsesAlternatingDashes, Is.False);
                Assert.That(sharedAngleLink.UsesAlternatingDashes, Is.True);
                Assert.That(SticksSyncedNoteLink.IsSharedAngle(15, 15.5f), Is.True);
                Assert.That(SticksSyncedNoteLink.IsSharedAngle(15, 15.51f), Is.False);
            });
        }

        [Test]
        public void TestEightyPercentStickDistanceMapping()
        {
            Vector2 half = SticksPlayfield.MapStickDistance(new Vector2(0.4f, 0), 0.8f);
            Vector2 atEdge = SticksPlayfield.MapStickDistance(new Vector2(0.8f, 0), 0.8f);
            Vector2 beyondEdge = SticksPlayfield.MapStickDistance(Vector2.UnitX, 0.8f);
            Vector2 unchanged = SticksPlayfield.MapStickDistance(new Vector2(0.6f, 0.8f), 1);

            Assert.Multiple(() =>
            {
                Assert.That(SticksPlayfield.MapStickDistance(Vector2.Zero, 0.8f), Is.EqualTo(Vector2.Zero));
                Assert.That((half - new Vector2(0.5f, 0)).Length, Is.LessThan(0.0001f));
                Assert.That((atEdge - Vector2.UnitX).Length, Is.LessThan(0.0001f));
                Assert.That((beyondEdge - Vector2.UnitX).Length, Is.LessThan(0.0001f));
                Assert.That((unchanged - new Vector2(0.6f, 0.8f)).Length, Is.LessThan(0.0001f));
            });
        }

        [Test]
        public void TestFlickTargetRequiresSuccessfulTiming()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksPlayfield.IsEligibleFlickTarget(HitResult.Great, 0, 20), Is.True);
                Assert.That(SticksPlayfield.IsEligibleFlickTarget(HitResult.Meh, 20, 20), Is.True);
                Assert.That(SticksPlayfield.IsEligibleFlickTarget(HitResult.Miss, 0, 20), Is.False, "A preempted note outside a successful timing window must not steal the flick.");
                Assert.That(SticksPlayfield.IsEligibleFlickTarget(HitResult.Great, 20.01f, 20), Is.False, "A wrong-angle note must not steal the flick.");
            });
        }

        [Test]
        public void TestEveryObjectTypeUsesItsScoredHeadTimingWindow()
        {
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var controlPoints = new ControlPointInfo();
            SticksHitObject[] objects =
            {
                new SticksFlick { StartTime = 1000, Angle = 45 },
                new SticksSlider { StartTime = 1000, Duration = 1000, Angle = 45, ArcAngle = 90 },
                new SticksHold { StartTime = 1000, Duration = 1000, Angle = 45 },
            };

            foreach (SticksHitObject hitObject in objects)
                hitObject.ApplyDefaults(controlPoints, difficulty);

            Assert.Multiple(() =>
            {
                foreach (SticksHitObject hitObject in objects)
                {
                    Assert.That(SticksPlayfield.HeadTimingResultFor(hitObject, 20).IsHit(), Is.True,
                        $"{hitObject.GetType().Name} must accept a human input slightly off the exact beat.");
                    Assert.That(SticksPlayfield.HeadTimingResultFor(hitObject, 250).IsHit(), Is.False,
                        $"{hitObject.GetType().Name} must still respect its scored head timing window.");
                }
            });
        }

        [Test]
        public void TestFlickTargetPrefersMatchingAngleBeforeTiming()
        {
            var closerWrongAngle = new SticksPlayfield.FlickTarget(1000, 180, 20);
            var intendedAngle = new SticksPlayfield.FlickTarget(1200, 0, 20);

            Assert.Multiple(() =>
            {
                Assert.That(SticksPlayfield.IsBetterFlickTarget(intendedAngle, closerWrongAngle, 1050, 0), Is.True);
                Assert.That(SticksPlayfield.IsBetterFlickTarget(closerWrongAngle, intendedAngle, 1050, 0), Is.False);
            });
        }

        [Test]
        public void TestFlickTargetUsesClosestTimingWhenAnglesBothMatch()
        {
            var earlierNote = new SticksPlayfield.FlickTarget(1000, 0, 20);
            var closerLaterNote = new SticksPlayfield.FlickTarget(1200, 10, 20);

            Assert.Multiple(() =>
            {
                Assert.That(SticksPlayfield.IsBetterFlickTarget(closerLaterNote, earlierNote, 1175, 5), Is.True);
                Assert.That(SticksPlayfield.IsBetterFlickTarget(earlierNote, closerLaterNote, 1175, 5), Is.False);
                Assert.That(SticksPlayfield.IsBetterFlickTarget(earlierNote, closerLaterNote, 1025, 5), Is.True, "Drawable update order must not override the closest note.");
            });
        }

        [Test]
        public void TestDifficultyAdjustLeavesCircleSizeAnglesUntouchedByDefault()
        {
            var flick = new SticksFlick();
            flick.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = 3 });

            var mod = new SticksModDifficultyAdjust
            {
                SpeedChange = { Value = 1.25 },
            };

            mod.ApplyToHitObject(flick);

            Assert.Multiple(() =>
            {
                Assert.That(mod.PrimaryHitAngle.Value, Is.Null);
                Assert.That(mod.SecondaryHitAngle.Value, Is.Null);
                Assert.That(flick.PrimaryHitAngle, Is.EqualTo(30));
                Assert.That(flick.SecondaryHitAngle, Is.EqualTo(30));
            });
        }

        [Test]
        public void TestCircleSizeControlsDefaultHitAngles()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksHitObject.HitAngleForCircleSize(0), Is.EqualTo(30));
                Assert.That(SticksHitObject.HitAngleForCircleSize(3), Is.EqualTo(30));
                Assert.That(SticksHitObject.HitAngleForCircleSize(4), Is.EqualTo(20).Within(0.0001f));
                Assert.That(SticksHitObject.HitAngleForCircleSize(5.4f), Is.EqualTo(15));
                Assert.That(SticksHitObject.HitAngleForCircleSize(10), Is.EqualTo(15));
            });

            float previous = SticksHitObject.HitAngleForCircleSize(3);

            for (float circleSize = 3.05f; circleSize <= 5.4f; circleSize += 0.05f)
            {
                float current = SticksHitObject.HitAngleForCircleSize(circleSize);
                Assert.That(current, Is.LessThan(previous), $"CS {circleSize} should produce a tighter angle than the preceding step.");
                previous = current;
            }

            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            };

            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty
            {
                CircleSize = 4,
                SliderTickRate = 1,
            });

            Assert.Multiple(() =>
            {
                Assert.That(slider.PrimaryHitAngle, Is.EqualTo(20).Within(0.0001f));
                Assert.That(slider.SecondaryHitAngle, Is.EqualTo(20).Within(0.0001f));
                Assert.That(slider.NestedHitObjects.Cast<SticksHitObject>(), Has.All.Matches<SticksHitObject>(nested =>
                    Math.Abs(nested.PrimaryHitAngle - 20) < 0.0001f && Math.Abs(nested.SecondaryHitAngle - 20) < 0.0001f));
            });
        }

        [Test]
        public void TestEasyUsesDifficultyValuesWithoutChangingApproachRate()
        {
            var difficulty = new BeatmapDifficulty
            {
                CircleSize = 5,
                OverallDifficulty = 8,
                DrainRate = 6,
                ApproachRate = 7,
            };

            new SticksModEasy().ApplyToDifficulty(difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(difficulty.CircleSize, Is.EqualTo(2.5f));
                Assert.That(difficulty.OverallDifficulty, Is.EqualTo(4));
                Assert.That(difficulty.DrainRate, Is.EqualTo(3));
                Assert.That(difficulty.ApproachRate, Is.EqualTo(7));
                Assert.That(SticksHitObject.HitAngleForCircleSize(difficulty.CircleSize), Is.EqualTo(30));
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
            Assert.Multiple(() =>
            {
                Assert.That(SticksHitObject.ApproachDurationFor(0), Is.EqualTo(1800));
                Assert.That(SticksHitObject.ApproachDurationFor(5), Is.EqualTo(1200));
                Assert.That(SticksHitObject.ApproachDurationFor(10), Is.EqualTo(450));
                Assert.That(SticksHitObject.ApproachDurationFor(11), Is.EqualTo(300));
                Assert.That(SticksHitObject.ApproachDurationFor(12), Is.EqualTo(150));
            });

            config.SetValue(SticksRulesetSetting.ApproachRate, 20f);
            Assert.That(config.Get<float>(SticksRulesetSetting.ApproachRate), Is.EqualTo(12), "The player AR control should expose values through AR12 and clamp there.");

            config.SetValue(SticksRulesetSetting.ApproachRate, 8f);
            slider.ApplyPlayerApproachRate(config.Get<float>(SticksRulesetSetting.ApproachRate));

            Assert.Multiple(() =>
            {
                Assert.That(config.Get<float>(SticksRulesetSetting.ApproachRate), Is.EqualTo(8));
                Assert.That(slider.ApproachDuration, Is.EqualTo(750).Within(0.001));
                Assert.That(slider.NestedHitObjects.Cast<SticksHitObject>(), Has.All.Matches<SticksHitObject>(nested =>
                    Math.Abs(nested.ApproachDuration - 750) < 0.001));
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

            var hold = new SticksHold
            {
                StartTime = 4000,
                Duration = 1000,
                Side = StickSide.Right,
                Angle = 180,
            };
            hold.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            beatmap.HitObjects.Add(hold);

            SticksReplayFrame[] frames = new SticksAutoGenerator(beatmap).Generate().Frames.Cast<SticksReplayFrame>().ToArray();
            SticksReplayFrame beforeChord = frames.Single(frame => frame.Time == 999);
            SticksReplayFrame chord = frames.Single(frame => frame.Time == 1000);
            SticksReplayFrame beforeSlider = frames.Single(frame => frame.Time == 1999);
            SticksReplayFrame sliderHead = frames.Single(frame => frame.Time == 2000);
            SticksReplayFrame middleTick = frames.Single(frame => frame.Time == 2500);
            SticksReplayFrame beforeHold = frames.Single(frame => frame.Time == 3999);
            SticksReplayFrame holdHead = frames.Single(frame => frame.Time == 4000);
            SticksReplayFrame holdTail = frames.Single(frame => frame.Time == 5000);
            SticksReplayFrame afterHold = frames.Single(frame => frame.Time == 5030);

            Assert.Multiple(() =>
            {
                Assert.That(beforeChord.LeftStick, Is.EqualTo(osuTK.Vector2.Zero));
                Assert.That(beforeChord.RightStick, Is.EqualTo(osuTK.Vector2.Zero));
                Assert.That(chord.LeftStick.X, Is.EqualTo(1).Within(0.001));
                Assert.That(chord.LeftStick.Y, Is.EqualTo(0).Within(0.001));
                Assert.That(chord.RightStick.X, Is.EqualTo(0).Within(0.001));
                Assert.That(chord.RightStick.Y, Is.EqualTo(1).Within(0.001));
                Assert.That(beforeSlider.LeftStick, Is.EqualTo(osuTK.Vector2.Zero));
                Assert.That(sliderHead.LeftStick.X, Is.EqualTo(1).Within(0.001));
                Assert.That(middleTick.LeftStick.Length, Is.EqualTo(1).Within(0.001));
                Assert.That(System.Math.Atan2(middleTick.LeftStick.Y, middleTick.LeftStick.X) * 180 / System.Math.PI, Is.EqualTo(45).Within(0.1));
                Assert.That(beforeHold.RightStick, Is.EqualTo(Vector2.Zero));
                Assert.That(holdHead.RightStick.X, Is.EqualTo(-1).Within(0.001));
                Assert.That(holdHead.RightStick.Y, Is.EqualTo(0).Within(0.001));
                Assert.That(holdTail.RightStick.X, Is.EqualTo(-1).Within(0.001));
                Assert.That(afterHold.RightStick, Is.EqualTo(Vector2.Zero));
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
        public void TestReplayInputProviderDoesNotRetainAutoplayPositionAfterDeactivation()
        {
            var provider = new SticksReplayInputProvider();
            provider.Update(Vector2.UnitX, -Vector2.UnitY);

            provider.Deactivate();
            (Vector2 left, Vector2 right) = provider.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(provider.Active, Is.False);
                Assert.That(left, Is.EqualTo(Vector2.Zero));
                Assert.That(right, Is.EqualTo(Vector2.Zero));
            });

            provider.Update(Vector2.UnitY, Vector2.UnitX);
            Assert.That(provider.Active, Is.True, "A subsequently attached replay must still be able to provide input.");
        }

        [Test]
        public void TestReplayRecorderCapturesBothPhysicalSticksWithoutDistanceRemapping()
        {
            var playfield = new SticksPlayfield
            {
                PhysicalStickDistanceAtGameEdge = 0.8f,
            };
            setPrivateField(playfield, "leftX", 0.8f);
            setPrivateField(playfield, "leftY", -0.25f);
            setPrivateField(playfield, "rightX", -0.6f);
            setPrivateField(playfield, "rightY", 0.75f);

            var recorder = new SticksReplayRecorder(new Score(), playfield);
            var frame = (SticksReplayFrame)typeof(SticksReplayRecorder)
                                               .GetMethod("captureFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                                               .Invoke(recorder, new object[] { 1234d })!;

            Assert.Multiple(() =>
            {
                Assert.That(frame.Time, Is.EqualTo(1234));
                Assert.That(frame.LeftStick, Is.EqualTo(new Vector2(0.8f, -0.25f)),
                    "Replay capture must store raw travel; playback applies the 80% distance mapping once.");
                Assert.That(frame.RightStick, Is.EqualTo(new Vector2(-0.6f, 0.75f)));
                Assert.That(playfield.PhysicalStickVector(StickSide.Left), Is.EqualTo(frame.LeftStick));
                Assert.That(playfield.PhysicalStickVector(StickSide.Right), Is.EqualTo(frame.RightStick));
            });
        }

        [Test]
        public void TestReplayLegacyBridgeRoundTripsAllFourAxes()
        {
            const float q15_tolerance = 1f / short.MaxValue + 0.000001f;
            var beatmap = new Beatmap();
            var positions = new[]
            {
                (Left: Vector2.Zero, Right: Vector2.Zero),
                (Left: new Vector2(-1, 1), Right: new Vector2(1, -1)),
                (Left: new Vector2(0.8f, -0.25f), Right: new Vector2(-0.61234f, 0.73456f)),
            };

            foreach ((Vector2 left, Vector2 right) in positions)
            {
                var original = new SticksReplayFrame(1234, left, right);
                LegacyReplayFrame legacy = original.ToLegacy(beatmap);
                var restored = new SticksReplayFrame();
                restored.FromLegacy(legacy, beatmap);

                Assert.Multiple(() =>
                {
                    Assert.That(legacy.ButtonState, Is.Not.EqualTo((ReplayButtonState)int.MinValue),
                        "The legacy parser intentionally rejects int.MinValue.");
                    Assert.That(restored.LeftStick, Is.EqualTo(left));
                    Assert.That(restored.RightStick.X, Is.EqualTo(right.X).Within(q15_tolerance));
                    Assert.That(restored.RightStick.Y, Is.EqualTo(right.Y).Within(q15_tolerance));
                });
            }

            var oldNeutralFrame = new LegacyReplayFrame(2000, 0.25f, -0.5f, ReplayButtonState.None);
            var decodedOldFrame = new SticksReplayFrame();
            decodedOldFrame.FromLegacy(oldNeutralFrame, beatmap);
            Assert.That(decodedOldFrame.RightStick, Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void TestReplayPlaybackHoldsCompleteSamplesUntilTheirRecordedTime()
        {
            var replay = new Replay
            {
                Frames = new List<ReplayFrame>
                {
                    new SticksReplayFrame(0, new Vector2(-1, 0.2f), new Vector2(0.4f, -0.8f)),
                    new SticksReplayFrame(1000, new Vector2(1, -0.6f), new Vector2(-0.4f, 0.8f)),
                },
            };
            var provider = new SticksReplayInputProvider();
            var handler = new SticksFramedReplayInputHandler(replay, provider);

            Assert.That(handler.SetFrameFromTime(-100), Is.EqualTo(-100));
            handler.CollectPendingInputs(new List<IInput>());
            Assert.That(provider.Snapshot(), Is.EqualTo((Vector2.Zero, Vector2.Zero)),
                "The first controller sample must not be anticipated before its timestamp.");

            Assert.That(handler.SetFrameFromTime(0), Is.EqualTo(0));
            Assert.That(handler.SetFrameFromTime(250), Is.EqualTo(250));
            handler.CollectPendingInputs(new List<IInput>());
            (Vector2 left, Vector2 right) = provider.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(new Vector2(-1, 0.2f)));
                Assert.That(right, Is.EqualTo(new Vector2(0.4f, -0.8f)));
            });

            Assert.That(handler.SetFrameFromTime(1000), Is.EqualTo(1000));
            handler.CollectPendingInputs(new List<IInput>());
            (left, right) = provider.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(new Vector2(1, -0.6f)));
                Assert.That(right, Is.EqualTo(new Vector2(-0.4f, 0.8f)));
            });
        }

        [Test]
        public void TestPhysicalAxisChangesArePublishedAsOneCompleteReplaySample()
        {
            var playfield = new SticksPlayfield();
            int notifications = 0;
            Vector2 reportedLeft = Vector2.Zero;
            Vector2 reportedRight = Vector2.Zero;

            playfield.PhysicalStickInputChanged += () =>
            {
                notifications++;
                reportedLeft = playfield.PhysicalStickVector(StickSide.Left);
                reportedRight = playfield.PhysicalStickVector(StickSide.Right);
            };

            // Framework joystick axes are delivered independently. Neither assignment may
            // publish a half-updated vector; publication happens once in the playfield update.
            setPrivateField(playfield, "leftX", 0.8f);
            setPrivateField(playfield, "leftY", -0.45f);
            setPrivateField(playfield, "rightX", -0.65f);
            setPrivateField(playfield, "rightY", 0.35f);
            Assert.That(notifications, Is.Zero);

            typeof(SticksPlayfield).GetMethod("reportPhysicalStickInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                                  .Invoke(playfield, new object[]
                                  {
                                      playfield.PhysicalStickVector(StickSide.Left),
                                      playfield.PhysicalStickVector(StickSide.Right),
                                  });

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(reportedLeft, Is.EqualTo(new Vector2(0.8f, -0.45f)));
                Assert.That(reportedRight, Is.EqualTo(new Vector2(-0.65f, 0.35f)));
            });

            typeof(SticksPlayfield).GetMethod("reportPhysicalStickInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                                  .Invoke(playfield, new object[] { reportedLeft, reportedRight });
            Assert.That(notifications, Is.EqualTo(1), "An unchanged controller sample must not generate a duplicate frame.");
        }

        private static void setPrivateField<T>(object target, string name, T value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

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

            Assert.That(impossibleStars, Is.GreaterThan(ordinaryStars));
        }

        [Test]
        public void TestDifficultyAccountsForAngularReadingComplexity()
        {
            var crossStickClustered = new List<SticksHitObject>();
            var crossStickWide = new List<SticksHitObject>();
            var sameStickClustered = new List<SticksHitObject>();
            var sameStickWide = new List<SticksHitObject>();

            for (int i = 0; i < 40; i++)
            {
                double time = 1000 + i * 200;
                StickSide alternatingSide = i % 2 == 0 ? StickSide.Left : StickSide.Right;
                float wideAngle = i % 2 == 0 ? 0 : 180;

                crossStickClustered.Add(new SticksFlick { StartTime = time, Side = alternatingSide, Angle = 0 });
                crossStickWide.Add(new SticksFlick { StartTime = time, Side = alternatingSide, Angle = wideAngle });
                sameStickClustered.Add(new SticksFlick { StartTime = time, Side = StickSide.Left, Angle = 0 });
                sameStickWide.Add(new SticksFlick { StartTime = time, Side = StickSide.Left, Angle = wideAngle });
            }

            double crossClusteredStars = SticksDifficultyCalculator.CalculateStarRating(crossStickClustered);
            double crossWideStars = SticksDifficultyCalculator.CalculateStarRating(crossStickWide);
            double sameClusteredStars = SticksDifficultyCalculator.CalculateStarRating(sameStickClustered);
            double sameWideStars = SticksDifficultyCalculator.CalculateStarRating(sameStickWide);

            Assert.Multiple(() =>
            {
                Assert.That(crossWideStars, Is.GreaterThan(crossClusteredStars));
                Assert.That(sameWideStars, Is.GreaterThan(sameClusteredStars));
            });
        }

        [Test]
        public void TestDifficultyDoesNotTreatIsolatedChordSpreadAsPhysicalTravel()
        {
            var stackedChord = new SticksHitObject[]
            {
                new SticksFlick { StartTime = 1000, Side = StickSide.Left, Angle = 0 },
                new SticksFlick { StartTime = 1000, Side = StickSide.Right, Angle = 0 },
            };
            var wideChord = new SticksHitObject[]
            {
                new SticksFlick { StartTime = 1000, Side = StickSide.Left, Angle = 0 },
                new SticksFlick { StartTime = 1000, Side = StickSide.Right, Angle = 180 },
            };

            Assert.That(SticksDifficultyCalculator.CalculateStarRating(wideChord),
                Is.EqualTo(SticksDifficultyCalculator.CalculateStarRating(stackedChord)).Within(0.0000001));
        }

        [Test]
        public void TestVeryFastSliderRemainsHighDifficultyAfterCalibration()
        {
            double fastStars = SticksDifficultyCalculator.CalculateStarRating(new[]
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
            double slowStars = SticksDifficultyCalculator.CalculateStarRating(new[]
            {
                new SticksSlider
                {
                    StartTime = 1000,
                    Duration = 2000,
                    Side = StickSide.Left,
                    Angle = 0,
                    ArcAngle = 270,
                },
            });

            Assert.That(fastStars, Is.GreaterThan(slowStars));
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
        public void TestHoldHeadRemainsVisibleForEntireDuration()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DrawableSticksHold.HeadMarkerAlphaAt(900, 2000), Is.EqualTo(1));
                Assert.That(DrawableSticksHold.HeadMarkerAlphaAt(1000, 2000), Is.EqualTo(1));
                Assert.That(DrawableSticksHold.HeadMarkerAlphaAt(1121, 2000), Is.EqualTo(1), "Hitting the head must not remove the hold's base arc.");
                Assert.That(DrawableSticksHold.HeadMarkerAlphaAt(1999, 2000), Is.EqualTo(1));
                Assert.That(DrawableSticksHold.HeadMarkerAlphaAt(2001, 2000), Is.Zero);
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
                Assert.That(extension.PreemptDuration, Is.EqualTo(2200));
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
        public void TestSliderUsesIndependentStandardStyleCheckpoints()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
            };

            Assert.Multiple(() =>
            {
                Assert.That(slider.CreateJudgement().MaxResult, Is.EqualTo(HitResult.IgnoreHit));
                Assert.That(new SticksSliderTailJudgement().MaxResult, Is.EqualTo(HitResult.SliderTailHit));
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
        public void TestEditorHoldRewindClearsCustomGameplayAndAudioState()
        {
            var drawable = new DrawableSticksHold(new SticksHold
            {
                StartTime = 1000,
                Duration = 1000,
            });

            typeof(DrawableSticksHold).GetMethod("MarkHeadMiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawable, null);
            typeof(DrawableSticksHold).GetField("headHit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(drawable, true);
            typeof(DrawableSticksHold).GetField("headSamplePlayed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(drawable, true);
            var eligibility = (SticksTrackingEligibility)typeof(DrawableSticksHold)
                .GetField("trackingEligibility", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
            eligibility.Authorise();

            typeof(DrawableSticksHold).GetMethod("ResetEditorPreviewState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(drawable, new object[] { 7L });

            bool headJudged = (bool)typeof(DrawableSticksHold)
                .GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;

            Assert.Multiple(() =>
            {
                Assert.That(headJudged, Is.False);
                Assert.That(typeof(DrawableSticksHold).GetField("headHit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable), Is.False);
                Assert.That(typeof(DrawableSticksHold).GetField("headSamplePlayed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable), Is.False);
                Assert.That(drawable.TrackingAuthorised, Is.False);
            });
        }

        [Test]
        public void TestMissingDurationHeadsDoNotResolveParentsEarly()
        {
            var slider = new DrawableSticksSlider(new SticksSlider
            {
                StartTime = 1000,
                Duration = 100,
            });
            var hold = new DrawableSticksHold(new SticksHold
            {
                StartTime = 1000,
                Duration = 100,
            });

            typeof(DrawableSticksSlider).GetMethod("MarkHeadMiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slider, null);
            typeof(DrawableSticksHold).GetMethod("MarkHeadMiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hold, null);

            bool sliderHeadJudged = (bool)typeof(DrawableSticksSlider).GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slider)!;
            bool sliderHeadHit = (bool)typeof(DrawableSticksSlider).GetProperty("HeadHit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slider)!;
            bool sliderHasResult = (bool)typeof(DrawableSticksSlider).GetProperty("HasResult", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slider)!;
            bool holdHeadJudged = (bool)typeof(DrawableSticksHold).GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hold)!;

            Assert.Multiple(() =>
            {
                Assert.That(100, Is.LessThan(SticksHitWindows.MISS_WINDOW), "The regression requires a duration shorter than the open head miss window.");
                Assert.That(sliderHeadJudged, Is.True);
                Assert.That(sliderHeadHit, Is.False);
                Assert.That(sliderHasResult, Is.False);
                Assert.That(holdHeadJudged, Is.True);
                Assert.That(hold.Judged, Is.False);
            });
        }

        [Test]
        public void TestShortDurationParentsCloseUnresolvedHeadsAtEnd()
        {
            var slider = new TestEndpointSlider(new SticksSlider
            {
                StartTime = 1000,
                Duration = 100,
            });
            var hold = new TestEndpointHold(new SticksHold
            {
                StartTime = 1000,
                Duration = 100,
            });

            slider.ResolveAt(1100);
            hold.ResolveAt(1100);

            Assert.Multiple(() =>
            {
                Assert.That(slider.HeadWasJudged, Is.True);
                Assert.That(slider.Judged, Is.True);
                Assert.That(hold.HeadWasJudged, Is.True);
                Assert.That(hold.Judged, Is.True);
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
                Assert.That(longSlider.RehearsalStartTime, Is.EqualTo(-200));
                Assert.That(longSlider.RehearsalProgressAt(1000), Is.EqualTo(0.6).Within(0.001));
            });
        }

        [Test]
        public void TestReversalSliderSnakesOnlyItsImmediatelyUpcomingSpan()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 3000,
                Angle = 0,
            };
            slider.SetCustomSegments(new[] { 90f, -90f, 90f });

            Assert.Multiple(() =>
            {
                Assert.That(slider.UpcomingSegmentIndexAt(999), Is.EqualTo(-1));
                Assert.That(slider.UpcomingSegmentIndexAt(1000), Is.EqualTo(1));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1000), Is.Zero);
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1001), Is.GreaterThan(0));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1500), Is.EqualTo(0.125).Within(0.0001));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1999), Is.GreaterThan(0.99));
                Assert.That(slider.UpcomingSegmentEndsWithReversalAt(1500), Is.True,
                    "The preview of a middle segment needs the same white reversal cue as the first segment.");
                Assert.That(slider.UpcomingSegmentIndexAt(2000), Is.EqualTo(2));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(2000), Is.Zero);
                Assert.That(slider.UpcomingSegmentEndsWithReversalAt(2500), Is.False,
                    "The final segment must not imply another reversal.");
                Assert.That(slider.UpcomingSegmentIndexAt(3000), Is.EqualTo(-1));
            });
        }

        [Test]
        public void TestShortReversalSpanUsesAvailableLeadTimeWithoutSkippingAhead()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                RepeatCount = 1,
                ArcAngle = 90,
            };

            Assert.Multiple(() =>
            {
                Assert.That(slider.UpcomingSegmentIndexAt(1000), Is.EqualTo(1));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1000), Is.Zero);
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1250), Is.EqualTo(0.125).Within(0.0001));
                Assert.That(slider.UpcomingSegmentPreviewProgressAt(1499), Is.GreaterThan(0.99));
                Assert.That(slider.UpcomingSegmentIndexAt(1500), Is.EqualTo(-1));
                Assert.That(DrawableSticksSlider.REVERSAL_PREVIEW_ALPHA, Is.LessThan(1));
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

        [Test]
        public void TestStandardDifficultyModsAreAvailable()
        {
            Mod[] reductions = new SticksRuleset().GetModsFor(ModType.DifficultyReduction).ToArray();
            Mod[] increases = new SticksRuleset().GetModsFor(ModType.DifficultyIncrease).ToArray();
            var failModes = (MultiMod)increases.Single(mod => mod is MultiMod);

            Assert.Multiple(() =>
            {
                Assert.That(reductions.Any(mod => mod is SticksModEasy), Is.True);
                Assert.That(reductions.Any(mod => mod is SticksModNoFail), Is.True);
                Assert.That(reductions.Any(mod => mod is SticksModHalfTime), Is.True);
                Assert.That(increases.Any(mod => mod is SticksModHardRock), Is.True);
                Assert.That(increases.Any(mod => mod is SticksModDoubleTime), Is.True);
                Assert.That(failModes.Mods.Any(mod => mod is SticksModSuddenDeath), Is.True);
                Assert.That(failModes.Mods.Any(mod => mod is SticksModPerfect), Is.True);
                Assert.That(new SticksRuleset().CreateHealthProcessor(1234), Is.TypeOf<SticksHealthProcessor>());
            });
        }

        [Test]
        public void TestHardRockDoesNotChangePlayerApproachRate()
        {
            var difficulty = new BeatmapDifficulty
            {
                ApproachRate = 7,
                CircleSize = 4,
                DrainRate = 5,
                OverallDifficulty = 5,
            };

            new SticksModHardRock().ApplyToDifficulty(difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(difficulty.ApproachRate, Is.EqualTo(7));
                Assert.That(difficulty.CircleSize, Is.EqualTo(5.2).Within(0.001));
                Assert.That(difficulty.DrainRate, Is.EqualTo(7));
                Assert.That(difficulty.OverallDifficulty, Is.EqualTo(7));
            });
        }

        [Test]
        public void TestStandardScoreMultipliers()
        {
            var calculator = new SticksScoreMultiplierCalculator(new ScoreMultiplierContext(new BeatmapDifficulty()));

            Assert.Multiple(() =>
            {
                Assert.That(calculator.CalculateFor(new Mod[] { new SticksModEasy() }), Is.EqualTo(0.8));
                Assert.That(calculator.CalculateFor(new Mod[] { new SticksModNoFail() }), Is.EqualTo(0.5));
                Assert.That(calculator.CalculateFor(new Mod[] { new SticksModHalfTime() }), Is.EqualTo(0.55).Within(0.001));
                Assert.That(calculator.CalculateFor(new Mod[] { new SticksModHardRock() }), Is.EqualTo(1.09));
                Assert.That(calculator.CalculateFor(new Mod[] { new SticksModDoubleTime() }), Is.EqualTo(1.23).Within(0.001));
            });
        }

        [Test]
        public void TestNoFailSuppressesUnactionableLowHealthWarning()
        {
            var mod = new SticksModNoFail();
            var overlay = new HUDOverlay(null, Array.Empty<Mod>(), new PlayerConfiguration());
            var healthProcessor = new SticksHealthProcessor(0);
            overlay.ShowHealthBar.Value = true;

            ((IApplicableToHUD)mod).ApplyToHUD(overlay);
            ((IApplicableToHealthProcessor)mod).ApplyToHealthProcessor(healthProcessor);
            healthProcessor.Health.Value -= 0.75;

            Assert.Multiple(() =>
            {
                Assert.That(overlay.ShowHealthBar.Value, Is.False);
                Assert.That(healthProcessor.Health.MinValue, Is.EqualTo(1));
                Assert.That(healthProcessor.Health.Value, Is.EqualTo(1), "No Fail must reject every attempted health subtraction.");
                Assert.That(typeof(SticksModNoFail).GetInterfaceMap(typeof(IApplicableToHUD)).TargetMethods,
                    Has.Some.Property("DeclaringType").EqualTo(typeof(SticksModNoFail)),
                    "Sticks must replace lazer's configurable No Fail HUD binding, not inherit it.");
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

        private partial class TestEndpointSlider : DrawableSticksSlider
        {
            public bool HeadWasJudged =>
                (bool)typeof(DrawableSticksSlider).GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;

            public TestEndpointSlider(SticksSlider hitObject)
                : base(hitObject)
            {
            }

            public void ResolveAt(double time)
            {
                setClock(time);
                base.CheckForResult(false, time - HitObject.EndTime);
            }

            private void setClock(double time)
            {
                var framedClock = new FramedClock(new ManualClock { CurrentTime = time });
                framedClock.ProcessFrame();
                Clock = framedClock;
            }
        }

        private partial class TestEndpointHold : DrawableSticksHold
        {
            public bool HeadWasJudged =>
                (bool)typeof(DrawableSticksHold).GetProperty("HeadJudged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;

            public TestEndpointHold(SticksHold hitObject)
                : base(hitObject)
            {
            }

            public void ResolveAt(double time)
            {
                var framedClock = new FramedClock(new ManualClock { CurrentTime = time });
                framedClock.ProcessFrame();
                Clock = framedClock;
                base.CheckForResult(false, time - HitObject.EndTime);
            }
        }
    }
}
