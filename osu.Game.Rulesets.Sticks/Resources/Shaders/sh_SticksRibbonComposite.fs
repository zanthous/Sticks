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
    mediump float outline = smoothstep(0.02, 0.8, mask.b);

    const lowp vec3 BLUE_FILL = vec3(0.2, 0.62, 1.0);
    const lowp vec3 RED_FILL = vec3(1.0, 0.25, 0.3);
    const lowp vec3 BLUE_OUTLINE = vec3(0.48, 0.753, 1.0);
    const lowp vec3 RED_OUTLINE = vec3(1.0, 0.513, 0.556);
    const lowp vec3 OVERLAP_FILL = vec3(0.722, 0.278, 1.0);    // #B847FF
    const lowp vec3 OVERLAP_OUTLINE = vec3(0.914, 0.576, 1.0); // #E993FF

    lowp vec3 singleFill = mix(BLUE_FILL, RED_FILL, redShare);
    lowp vec3 singleOutline = mix(BLUE_OUTLINE, RED_OUTLINE, redShare);
    lowp vec3 singleColour = mix(singleFill, singleOutline, outline);
    lowp vec3 overlapColour = mix(OVERLAP_FILL, OVERLAP_OUTLINE, outline);
    lowp vec3 mappedColour = mix(singleColour, overlapColour, overlap);

    // Tracking raises the interior mask alpha from .82 to .90 in time with the local beat.
    // Keep the authored overlap and rail colours stable; only a single-lane fill pulses.
    mediump float pulse = clamp((mask.a - 0.82) / 0.08, 0.0, 1.0)
                           * (1.0 - outline) * (1.0 - overlap);
    mappedColour = mix(mappedColour, vec3(1.0), 0.252 * pulse);

    lowp vec4 texel = vec4(mappedColour * mask.a, mask.a);
    o_Colour = getRoundedColor(texel, wrappedCoord);
}
