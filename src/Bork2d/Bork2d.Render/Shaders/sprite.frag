#version 460
#extension GL_EXT_nonuniform_qualifier : require

// Bork2d sprite pipeline fragment shader. Samples the bindless texture page selected by the
// instance record and multiplies the linear tint.

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec4 vTint;
layout(location = 2) flat in uint vTextureIndex;

layout(set = 0, binding = 1) uniform sampler2D textures[];

layout(location = 0) out vec4 outColor;

void main()
{
    outColor = texture(textures[nonuniformEXT(vTextureIndex)], vUv) * vTint;
}
