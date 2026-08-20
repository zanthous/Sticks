#define HIGH_PRECISION_VERTEX

#include "sh_Utils.h"
#include "sh_Masking.h"
#include "sh_TextureWrapping.h"

layout(location = 2) in mediump vec2 v_TexCoord;

layout(set = 0, binding = 0) uniform lowp texture2D m_Texture;
layout(set = 0, binding = 1) uniform lowp sampler m_Sampler;

layout(location = 0) out vec4 o_Colour;

void main(void)
{
    mediump vec2 wrappedCoord = wrap(v_TexCoord, v_TexRect);
    lowp vec4 source = wrappedSampler(wrappedCoord, v_TexRect, m_Texture, m_Sampler, -0.9);

    // The authored blue/red sprites provide only the soft glow shape. Their baked hue must not
    // contaminate the configured stick colour supplied by the trail drawable's vertex colour.
    lowp float coverage = source.a * max(source.r, max(source.g, source.b));
    o_Colour = getRoundedColor(vec4(1.0, 1.0, 1.0, coverage), wrappedCoord);
}
