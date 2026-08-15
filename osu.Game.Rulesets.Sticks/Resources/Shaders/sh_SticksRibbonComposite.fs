#define HIGH_PRECISION_VERTEX

#include "sh_Utils.h"
#include "sh_Masking.h"
#include "sh_TextureWrapping.h"

layout(location = 2) in highp vec2 v_TexCoord;

layout(set = 0, binding = 0) uniform lowp texture2D m_Texture;
layout(set = 0, binding = 1) uniform lowp sampler m_Sampler;

layout(location = 0) out vec4 o_Colour;

void main(void)
{
    highp vec2 wrappedCoord = wrap(v_TexCoord, v_TexRect);
    lowp vec4 mask = wrappedSampler(wrappedCoord, v_TexRect, m_Texture, m_Sampler, -0.9);

    // R stores red-lane coverage, G stores blue-lane coverage, and B stores rail coverage.
    // The paths use component-wise maximum blending, so crossings retain both lane masks
    // regardless of draw order and can be mapped to a deliberate violet instead of a pale sum.
    mediump float redPresence = smoothstep(0.02, 0.55, mask.r);
    mediump float bluePresence = smoothstep(0.02, 0.55, mask.g);
    mediump float presenceSum = max(redPresence + bluePresence, 0.001);
    mediump float redShare = redPresence / presenceSum;
    mediump float overlap = min(redPresence, bluePresence);
    // B now carries a normalised distance field across a narrow band inside each ribbon edge.
    // Turn that one gradient into a soft bloom with a concentrated bright core.
    mediump float edgeFalloff = smoothstep(0.0, 1.0, clamp(mask.b, 0.0, 1.0));
    mediump float edgeCore = smoothstep(0.72, 0.98, mask.b);
    mediump float edgeLighting = clamp(0.435 * edgeFalloff + 0.565 * edgeCore, 0.0, 1.0);

    const lowp vec3 BLUE_FILL = vec3(0.2, 0.62, 1.0);
    const lowp vec3 RED_FILL = vec3(1.0, 0.25, 0.3);
    const lowp vec3 BLUE_OUTLINE = vec3(0.439, 0.839, 1.0);   // #70D6FF
    const lowp vec3 RED_OUTLINE = vec3(1.0, 0.408, 0.498);    // #FF687F
    const lowp vec3 OVERLAP_FILL = vec3(0.722, 0.278, 1.0);    // #B847FF
    const lowp vec3 OVERLAP_OUTLINE = vec3(0.914, 0.576, 1.0); // #E993FF

    lowp vec3 singleFill = mix(BLUE_FILL, RED_FILL, redShare);
    lowp vec3 singleOutline = mix(BLUE_OUTLINE, RED_OUTLINE, redShare);
    lowp vec3 singleColour = mix(singleFill, singleOutline, edgeLighting);
    lowp vec3 overlapColour = mix(OVERLAP_FILL, OVERLAP_OUTLINE, edgeLighting);
    lowp vec3 mappedColour = mix(singleColour, overlapColour, overlap);

    // Tracking raises the interior mask alpha from .82 to .90 in time with the local beat.
    // Apply the existing pulse after edge lighting while protecting the coloured boundary itself.
    mediump float pulse = clamp((mask.a - 0.82) / 0.08, 0.0, 1.0)
                           * (1.0 - edgeLighting) * (1.0 - overlap);
    mappedColour = mix(mappedColour, vec3(1.0), 0.252 * pulse);

    lowp float outputAlpha = mix(mask.a, 1.0, edgeLighting);
    lowp vec4 texel = vec4(mappedColour, outputAlpha);
    o_Colour = getRoundedColor(texel, wrappedCoord);
}
