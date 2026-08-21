using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    internal partial class SticksControllerTestScreen : OsuScreen
    {
        public override string Title => "Sticks controller test";

        public override bool HideOverlaysOnEnter => true;

        public override float BackgroundParallaxAmount => 0;

        private readonly float activationThreshold;
        private readonly SticksSpeedMeasurement leftMeasurement;
        private readonly SticksSpeedMeasurement rightMeasurement;
        private readonly StickResultPanel leftPanel;
        private readonly StickResultPanel rightPanel;
        private readonly StatisticsPanel statisticsPanel;

        private float leftX;
        private float leftY;
        private float rightX;
        private float rightY;

        public SticksControllerTestScreen(float activationThreshold, Colour4 leftColour, Colour4 rightColour)
        {
            this.activationThreshold = Math.Clamp(activationThreshold,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD);

            leftMeasurement = new SticksSpeedMeasurement(this.activationThreshold);
            rightMeasurement = new SticksSpeedMeasurement(this.activationThreshold);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Colour4.FromHex("101218"),
                },
                new OsuScrollContainer(Direction.Vertical)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 18),
                        Padding = new MarginPadding { Vertical = 24 },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "CONTROLLER SPEED TEST",
                                Font = OsuFont.Torus.With(size: 34, weight: FontWeight.Bold),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = $"Flick outward and release  •  return {this.activationThreshold * 100:0}% → 5%  •  press rest → {this.activationThreshold * 100:0}%",
                                Font = OsuFont.Default.With(size: 20),
                                Colour = Colour4.FromHex("B8BEC9"),
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(28, 0),
                                Children = new Drawable[]
                                {
                                    leftPanel = new StickResultPanel("LEFT STICK", leftColour),
                                    rightPanel = new StickResultPanel("RIGHT STICK", rightColour),
                                },
                            },
                            statisticsPanel = new StatisticsPanel(leftColour, rightColour),
                        },
                    },
                },
                new RoundedButton
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    X = -20,
                    Y = -20,
                    Width = 200,
                    Height = 46,
                    Text = "Reset measurements",
                    Action = resetMeasurements,
                },
            };
        }

        protected override bool OnJoystickAxisMove(JoystickAxisMoveEvent e)
        {
            switch (e.Axis.Source)
            {
                case JoystickAxisSource.GamePadLeftStickX:
                    leftX = e.Axis.Value;
                    return true;

                case JoystickAxisSource.GamePadLeftStickY:
                    leftY = e.Axis.Value;
                    return true;

                case JoystickAxisSource.GamePadRightStickX:
                    rightX = e.Axis.Value;
                    return true;

                case JoystickAxisSource.GamePadRightStickY:
                    rightY = e.Axis.Value;
                    return true;

                default:
                    return base.OnJoystickAxisMove(e);
            }
        }

        protected override void Update()
        {
            base.Update();

            Vector2 left = clampStick(new Vector2(leftX, leftY));
            Vector2 right = clampStick(new Vector2(rightX, rightY));

            leftMeasurement.Update(left, Time.Current);
            rightMeasurement.Update(right, Time.Current);
            leftPanel.UpdateDisplay(left, leftMeasurement);
            rightPanel.UpdateDisplay(right, rightMeasurement);
            statisticsPanel.UpdateDisplay(leftMeasurement, rightMeasurement);
        }

        private void resetMeasurements()
        {
            leftMeasurement.Reset(clampStick(new Vector2(leftX, leftY)), Time.Current);
            rightMeasurement.Reset(clampStick(new Vector2(rightX, rightY)), Time.Current);
        }

        private static Vector2 clampStick(Vector2 value) => value.LengthSquared > 1 ? value.Normalized() : value;

        private partial class StickResultPanel : CircularContainer
        {
            private const float gauge_size = 170;

            private readonly CircularContainer cursor;
            private readonly OsuSpriteText magnitudeText;
            private readonly OsuSpriteText returnText;
            private readonly OsuSpriteText pressText;

            public StickResultPanel(string title, Colour4 colour)
            {
                Width = 390;
                Height = 390;
                CornerRadius = 16;
                Masking = true;
                BorderThickness = 2;
                BorderColour = colour.Opacity(0.55f);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Colour4.FromHex("1B1E27"),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 24,
                        Text = title,
                        Font = OsuFont.Torus.With(size: 24, weight: FontWeight.SemiBold),
                        Colour = colour,
                    },
                    new CircularContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 70,
                        Size = new Vector2(gauge_size),
                        Masking = true,
                        BorderThickness = 3,
                        BorderColour = Colour4.FromHex("697181"),
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Colour4.FromHex("11131A"),
                            },
                            cursor = new CircularContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(22),
                                Masking = true,
                                BorderThickness = 3,
                                BorderColour = Colour4.White,
                                Child = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colour,
                                },
                            },
                        },
                    },
                    magnitudeText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 250,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = Colour4.FromHex("9EA6B5"),
                    },
                    returnText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 286,
                        Font = OsuFont.Torus.With(size: 23, weight: FontWeight.SemiBold),
                    },
                    pressText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 326,
                        Font = OsuFont.Default.With(size: 19),
                        Colour = Colour4.FromHex("C5CAD3"),
                    },
                };
            }

            public void UpdateDisplay(Vector2 value, SticksSpeedMeasurement measurement)
            {
                cursor.Position = value * ((gauge_size - cursor.Width) / 2);
                magnitudeText.Text = $"{value.Length * 100:0}% outward";
                returnText.Text = measurement.LatestReturnTime == null
                    ? "RETURN  —"
                    : $"RETURN  {measurement.LatestReturnTime:0.0} ms   avg {measurement.AverageReturnTime:0.0}";
                pressText.Text = measurement.LatestPressTime == null
                    ? "PRESS  —"
                    : $"PRESS  {measurement.LatestPressTime:0.0} ms   avg {measurement.AveragePressTime:0.0}";
            }
        }

        private partial class StatisticsPanel : CircularContainer
        {
            private readonly OsuSpriteText leftReturnText;
            private readonly OsuSpriteText leftPressText;
            private readonly OsuSpriteText rightReturnText;
            private readonly OsuSpriteText rightPressText;
            private readonly OsuSpriteText leftPaceText;
            private readonly OsuSpriteText rightPaceText;

            public StatisticsPanel(Colour4 leftColour, Colour4 rightColour)
            {
                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                Width = 808;
                Height = 220;
                CornerRadius = 12;
                Masking = true;
                BorderThickness = 2;
                BorderColour = Colour4.FromHex("464C59");

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Colour4.FromHex("171A22"),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 12,
                        Text = "MEASURED TIMES  •  5TH / MEDIAN / 95TH PERCENTILE",
                        Font = OsuFont.Torus.With(size: 16, weight: FontWeight.SemiBold),
                        Colour = Colour4.FromHex("AEB5C1"),
                    },
                    leftReturnText = new OsuSpriteText
                    {
                        Origin = Anchor.TopCentre,
                        X = 202,
                        Y = 43,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = leftColour,
                    },
                    leftPressText = new OsuSpriteText
                    {
                        Origin = Anchor.TopCentre,
                        X = 202,
                        Y = 77,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = leftColour,
                    },
                    rightReturnText = new OsuSpriteText
                    {
                        Origin = Anchor.TopCentre,
                        X = 606,
                        Y = 43,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = rightColour,
                    },
                    rightPressText = new OsuSpriteText
                    {
                        Origin = Anchor.TopCentre,
                        X = 606,
                        Y = 77,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = rightColour,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 118,
                        Text = "ESTIMATED REPEATED-FLICK PACE",
                        Font = OsuFont.Torus.With(size: 16, weight: FontWeight.SemiBold),
                        Colour = Colour4.FromHex("929AA8"),
                    },
                    leftPaceText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 150,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = leftColour,
                    },
                    rightPaceText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 177,
                        Font = OsuFont.Default.With(size: 17),
                        Colour = rightColour,
                    },
                };
            }

            public void UpdateDisplay(SticksSpeedMeasurement left, SticksSpeedMeasurement right)
            {
                leftReturnText.Text = format("LEFT RETURN", left.TryGetReturnPercentiles, left.ReturnCount);
                leftPressText.Text = format("LEFT PRESS", left.TryGetPressPercentiles, left.PressCount);
                rightReturnText.Text = format("RIGHT RETURN", right.TryGetReturnPercentiles, right.ReturnCount);
                rightPressText.Text = format("RIGHT PRESS", right.TryGetPressPercentiles, right.PressCount);
                leftPaceText.Text = formatPace("LEFT", left);
                rightPaceText.Text = formatPace("RIGHT", right);
            }

            private delegate bool PercentileProvider(out double percentile5, out double median, out double percentile95);

            private static string format(string label, PercentileProvider provider, int count)
            {
                return provider(out double percentile5, out double median, out double percentile95)
                    ? $"{label}  {percentile5:0.0} / {median:0.0} / {percentile95:0.0} ms  (n={count})"
                    : $"{label}  —";
            }

            private static string formatPace(string label, SticksSpeedMeasurement measurement)
            {
                if (measurement.AveragePressTime == null || measurement.AverageReturnTime == null)
                    return $"{label}   —";

                double averageCycle = measurement.AveragePressTime.Value + measurement.AverageReturnTime.Value;

                if (averageCycle <= 0)
                    return $"{label}   —";

                double flicksPerSecond = 1000 / averageCycle;
                double quarterBeatBpm = flicksPerSecond * 15;
                return $"{label}   avg {averageCycle:0.0} ms   ·   {flicksPerSecond:0.0}/s   ·   {quarterBeatBpm:0} BPM (1/4)";
            }
        }

    }
}
