using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksRadialTimelinePathTest
    {
        private const float outer_radius = SticksPlayfield.GUIDE_RADIUS;
        private const float tail_radius = outer_radius * 0.8f;

        [TestCase(45, 175)]
        [TestCase(90, 155)]
        public void TestWideHoldStopsAtItsCurvedTail(float span, float unoccupiedRadius)
        {
            // A short visible hold occupies the outer fifth of the playfield.
            // A single quad across the full width incorrectly extends its tail
            // inward to tailRadius * cos(halfSpan), producing a straight cutout.
            List<Quad> mesh = createRibbon(span, _ => 45);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(contains(mesh, SticksPlayfield.PointAt(45, 205)), Is.True,
                    "The intended hold body remains filled.");
                Assert.That(contains(mesh, SticksPlayfield.PointAt(45, unoccupiedRadius)), Is.False,
                    "The fill must stop at the tail radius throughout the angular window.");
            });
        }

        [TestCase(45, -45, 45)]
        [TestCase(90, -45, 45)]
        [TestCase(90, 45, -45)]
        [TestCase(90, 315, 405)]
        [TestCase(90, 405, 315)]
        public void TestTurningRibbonFollowsTheAngleWindowAtEachRadius(float span, float startAngle, float endAngle)
        {
            float angleAt(float progress) => startAngle + (endAngle - startAngle) * progress;
            List<Quad> mesh = createRibbon(span, angleAt);

            for (float radius = tail_radius + 4; radius <= outer_radius - 4; radius += 5)
            {
                float progress = (outer_radius - radius) / (outer_radius - tail_radius);
                float angle = angleAt(progress);

                foreach (float offset in new[] { -span / 2 + 4, -span / 4, 0, span / 4, span / 2 - 4 })
                {
                    Assert.That(contains(mesh, SticksPlayfield.PointAt(angle + offset, radius)), Is.True,
                        $"Unfilled area inside the angle window at radius {radius}, offset {offset}.");
                }

                foreach (float offset in new[] { -span / 2 - 4, span / 2 + 4 })
                {
                    Assert.That(contains(mesh, SticksPlayfield.PointAt(angle + offset, radius)), Is.False,
                        $"Fill escapes the angle window at radius {radius}, offset {offset}.");
                }
            }
        }

        [Test]
        public void TestWideLeadingEdgeDoesNotFillAcrossTheChangingWindow()
        {
            List<Quad> mesh = createRibbon(90, progress => -45 + 90 * progress);

            // At radius 200 the source centre has already turned to about 14°.
            // The old leading annulus still painted this point at -70°, outside
            // that row's window, even though it lies inside the initial head arc.
            Assert.That(contains(mesh, SticksPlayfield.PointAt(-70, 200)), Is.False);
        }

        [TestCase(27.5f)]
        [TestCase(45)]
        [TestCase(90)]
        public void TestBoundaryFacetingStaysBelowQuarterPixel(float span)
        {
            var quads = new SticksRibbonQuad[SticksRibbonGeometry.MAX_ANGULAR_SEGMENTS];
            int count = SticksRibbonGeometry.FillStrip(
                new SticksRibbonPoint(outer_radius, 45, span / 2),
                new SticksRibbonPoint(tail_radius, 45, span / 2),
                outer_radius, quads);

            Assert.That(count, Is.InRange(1, quads.Length));
            int checkedEdges = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2[] vertices = verticesOf(quads[i].Geometry);

                for (int j = 0; j < vertices.Length; j++)
                {
                    Vector2 first = vertices[j];
                    Vector2 second = vertices[(j + 1) % vertices.Length];

                    if (Math.Abs(radiusOf(first) - outer_radius) > 0.001f
                        || Math.Abs(radiusOf(second) - outer_radius) > 0.001f)
                        continue;

                    Assert.That(radiusOf((first + second) / 2), Is.InRange(outer_radius - 0.25f, outer_radius + 0.001f),
                        "The shared fill mesh must follow the curved edge without a visible chord.");
                    checkedEdges++;
                }
            }

            Assert.That(checkedEdges, Is.GreaterThan(0));
        }

        [TestCase(0)]
        [TestCase(0.001f)]
        [TestCase(1)]
        public void TestCentreEndpointHasFiniteBoundedGeometryAndGlow(float endRadius)
        {
            var quads = new SticksRibbonQuad[SticksRibbonGeometry.MAX_ANGULAR_SEGMENTS];
            int count = SticksRibbonGeometry.FillStrip(
                new SticksRibbonPoint(4, 315, 45),
                new SticksRibbonPoint(endRadius, 330, 45),
                outer_radius, quads);

            Assert.That(count, Is.InRange(1, quads.Length));

            for (int i = 0; i < count; i++)
            {
                foreach (Vector2 vertex in verticesOf(quads[i].Geometry))
                {
                    Assert.That(float.IsFinite(vertex.X) && float.IsFinite(vertex.Y), Is.True);
                    Assert.That(radiusOf(vertex), Is.LessThanOrEqualTo(4.001f));
                }

                Vector4 glow = quads[i].EdgeGlow;
                foreach (float channel in new[] { glow.X, glow.Y, glow.Z, glow.W })
                {
                    Assert.That(float.IsFinite(channel), Is.True);
                    Assert.That(channel, Is.InRange(0, 1));
                }
            }
        }

        private static List<Quad> createRibbon(float span, Func<float, float> angleAt)
        {
            // Twelve temporal strips correspond to roughly 18 ms samples across
            // a 200 ms visible section. Use the exact fill helper called by Draw.
            const int temporal_segments = 12;
            var mesh = new List<Quad>();
            var buffer = new SticksRibbonQuad[SticksRibbonGeometry.MAX_ANGULAR_SEGMENTS];

            SticksRibbonPoint pointAt(float progress) => new SticksRibbonPoint(
                outer_radius + (tail_radius - outer_radius) * progress, angleAt(progress), span / 2);

            for (int i = 0; i < temporal_segments; i++)
            {
                int count = SticksRibbonGeometry.FillStrip(
                    pointAt(i / (float)temporal_segments), pointAt((i + 1) / (float)temporal_segments), outer_radius, buffer);

                for (int j = 0; j < count; j++)
                    mesh.Add(buffer[j].Geometry);
            }

            return mesh;
        }

        private static bool contains(List<Quad> mesh, Vector2 point)
        {
            foreach (Quad quad in mesh)
            {
                if (triangleContains(quad.TopLeft, quad.TopRight, quad.BottomLeft, point)
                    || triangleContains(quad.BottomLeft, quad.TopRight, quad.BottomRight, point))
                    return true;
            }

            return false;
        }

        private static bool triangleContains(Vector2 a, Vector2 b, Vector2 c, Vector2 point)
        {
            float cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

            if (Math.Abs(cross(b - a, c - a)) < 0.00001f)
                return false;

            float first = cross(b - a, point - a);
            float second = cross(c - b, point - b);
            float third = cross(a - c, point - c);

            return !(first < -0.0001f || second < -0.0001f || third < -0.0001f)
                   || !(first > 0.0001f || second > 0.0001f || third > 0.0001f);
        }

        private static Vector2[] verticesOf(Quad quad) => new[] { quad.TopLeft, quad.TopRight, quad.BottomRight, quad.BottomLeft };

        private static float radiusOf(Vector2 point) => (point - new Vector2(SticksPlayfield.SIZE / 2)).Length;
    }
}
