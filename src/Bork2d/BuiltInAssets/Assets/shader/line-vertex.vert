#version 460

// Bork2d built-in line pipeline vertex shader.
//
// The renderer expands a polyline into triangles on the CPU and writes one 112-byte vertex record
// per triangle vertex: inPosition (world space), inLineUv, inColor, inMisc, and the four vec4 values
// of the line's 64-byte instance-uniform block. There is no instance buffer; every value is a vertex
// input.
//
// The stage writes the complete version-3 fragment interface. A line has no main texture and no
// additional textures, so it writes the placeholder index 0 and zero extra indices; the
// sprite-compatible aliases (vUv, vTint, vLocalPosition) let a sprite shader run on a line unchanged.

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inLineUv;
layout(location = 2) in vec4 inColor;
layout(location = 3) in vec4 inMisc;
layout(location = 4) in vec4 inParams0;
layout(location = 5) in vec4 inParams1;
layout(location = 6) in vec4 inParams2;
layout(location = 7) in vec4 inParams3;

layout(push_constant) uniform PushConstants
{
    mat4 viewProjection;
} pc;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec4 vTint;
layout(location = 2) flat out uint vTextureIndex;
layout(location = 3) out vec2 vWorldPosition;
layout(location = 4) out vec2 vLocalPosition;
layout(location = 5) flat out uint vExtraTextureIndices[4];
layout(location = 9) out vec4 vInstanceParams[4];
layout(location = 13) out vec2 vLineUv;
layout(location = 14) out vec4 vColor;
layout(location = 15) flat out float vLineFlags;

void main()
{
    gl_Position = pc.viewProjection * vec4(inPosition, 0.0, 1.0);

    vWorldPosition = inPosition;
    vLineUv = inLineUv;
    vColor = inColor;
    vLineFlags = inMisc.x;

    // Sprite-compatible aliases for a shader shared between sprite and line renderables.
    vUv = inLineUv;
    vTint = inColor;
    vTextureIndex = 0u; // lines have no main texture; index 0 is the placeholder
    vLocalPosition = inLineUv;
    vExtraTextureIndices = uint[4](0u, 0u, 0u, 0u);
    vInstanceParams = vec4[4](inParams0, inParams1, inParams2, inParams3);
}
