#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose.Components;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    /// <summary>
    /// Adds Sticks details to the host's timeline without replacing its selection or drag logic.
    /// The host has no ruleset timeline factory; use its public drawable tree and containers.
    /// </summary>
    internal partial class SticksTimelineMarkerAttachment : CompositeComponent
    {
        [Resolved]
        private EditorScreenWithTimeline editorScreen { get; set; } = null!;

        private BlueprintContainer<HitObject>? timeline;
        private readonly ConditionalWeakTable<TimelineHitObjectBlueprint, SticksTimelineTurnMarkers> attached = new();

        protected override void Update()
        {
            base.Update();
            if (timeline == null || timeline.Parent == null)
                timeline = editorScreen.TimelineArea.ChildrenOfType<BlueprintContainer<HitObject>>().FirstOrDefault();
            if (timeline?.IsLoaded != true)
                return;

            // The flat alive list also catches objects appearing after seeks and undo. Only
            // visible sliders need decoration, and native blueprints own the markers' lifetime.
            foreach (var child in timeline.SelectionBlueprints.AliveChildren)
            {
                if (child is not TimelineHitObjectBlueprint blueprint || blueprint.Item is not SticksSlider slider
                    || !blueprint.IsLoaded || !blueprint.IsPresent || attached.TryGetValue(blueprint, out _))
                    continue;

                // This is the native foreground group (combo text, tail handle, repeat marks).
                // Its contrast colour and width already match the bar's head/tail centres.
                Container? foreground = blueprint.ChildrenOfType<Container>().FirstOrDefault(container =>
                    container.Parent == blueprint && container.Children.Any(drawable => drawable is OsuSpriteText));
                if (foreground == null)
                    continue;

                var markers = new SticksTimelineTurnMarkers(slider);
                foreground.Add(markers);
                attached.Add(blueprint, markers);
            }
        }
    }

    internal partial class SticksTimelineTurnMarkers : CompositeDrawable
    {
        private readonly SticksSlider slider;
        private readonly List<Box> markers = new();

        public SticksTimelineTurnMarkers(SticksSlider slider)
        {
            this.slider = slider;
            RelativeSizeAxes = Axes.Both;
            Depth = -1;
        }

        public override bool HandlePositionalInput => false;
        public override bool HandleNonPositionalInput => false;

        protected override void Update()
        {
            base.Update();
            int used = 0;
            double elapsed = 0;
            float lastVisibleX = float.NegativeInfinity;
            bool validDuration = double.IsFinite(slider.Duration) && slider.Duration > 0;

            for (int segment = 0; validDuration && segment < slider.SegmentCount - 1; segment++)
            {
                elapsed += slider.SegmentDurationAt(segment);
                if (!slider.SegmentEndsWithReversal(segment))
                    continue;

                double fraction = elapsed / slider.Duration;
                if (!double.IsFinite(fraction) || fraction <= 0 || fraction >= 1)
                    continue;

                if (used == markers.Count)
                {
                    var marker = new Box
                    {
                        Name = "Slider turn",
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        RelativePositionAxes = Axes.X,
                        Size = new Vector2(2, 12),
                    };
                    markers.Add(marker);
                    AddInternal(marker);
                }

                Box current = markers[used++];
                current.X = (float)fraction;
                float pixelX = current.X * DrawWidth;
                // Don't turn densely packed points into a solid stripe or cover the end handles.
                bool visible = pixelX >= 4 && DrawWidth - pixelX >= 4 && pixelX - lastVisibleX >= 4;
                current.Alpha = visible ? 0.9f : 0;
                if (visible)
                    lastVisibleX = pixelX;
            }

            for (int i = used; i < markers.Count; i++)
                markers[i].Hide();
        }
    }
}
