#version 460

// Bork2d built-in sprite pipeline vertex shader.
//
// The instance record is the 176-byte std430 ABI (11 x vec4). The vertex stage has no vertex
// buffer: gl_VertexIndex selects one of the four generated quad corners and gl_InstanceIndex
// selects the instance record.
//
// FlipX/FlipY are applied by the planner as the sign of size.x/size.y. That is algebraically
// identical to the approved formula
//   local = ((c.x - pivot.x) * size.x * sx, (pivot.y - c.y) * size.y * sy)
// and keeps the reserved `flags` field zero in M8.

struct Instance
{
    vec4 transform0; // M11, M12, M21, M22
    vec4 transform1; // M31, M32, sizeX, sizeY (size signs carry flips)
    vec4 uv0;        // uTL, vTL, uTR, vTR
    vec4 uv1;        // uBR, vBR, uBL, vBL
    vec4 tint;       // linear RGBA
    vec4 misc;       // pivotX, pivotY, mainTextureIndex, flags (zero)
    vec4 extraTextureIndices; // 4 additional bindless indices as floats
    vec4 instanceParams0;
    vec4 instanceParams1;
    vec4 instanceParams2;
    vec4 instanceParams3;
};

layout(std430, set = 0, binding = 0) readonly buffer InstanceBuffer
{
    Instance instances[];
};

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

void main()
{
    Instance instance = instances[gl_InstanceIndex];

    // Triangle-strip order TL, TR, BL, BR with a top-left origin. The strip's two triangles are
    // TL-TR-BL and TR-BL-BR, which tile the quad along the TR-BL diagonal. (TL, TR, BR, BL would
    // overlap on the right and leave the local left triangle uncovered.)
    vec2 corners[4] = vec2[4](
        vec2(0.0, 0.0),
        vec2(1.0, 0.0),
        vec2(0.0, 1.0),
        vec2(1.0, 1.0));
    vec2 corner = corners[gl_VertexIndex];

    vec2 pivot = instance.misc.xy;
    vec2 size = instance.transform1.zw;

    // Pivot is top-left normalized, so +Y points down in local corner space.
    vec2 local = vec2(
        (corner.x - pivot.x) * size.x,
        (pivot.y - corner.y) * size.y);

    vec3 world = vec3(
        instance.transform0.x * local.x + instance.transform0.z * local.y + instance.transform1.x,
        instance.transform0.y * local.x + instance.transform0.w * local.y + instance.transform1.y,
        0.0);

    gl_Position = pc.viewProjection * vec4(world, 1.0);

    // Match the TL, TR, BL, BR strip order: uv0 is TL,TR and uv1 is BR,BL.
    vec2 uv;
    if (gl_VertexIndex == 0)
    {
        uv = instance.uv0.xy; // TL
    }
    else if (gl_VertexIndex == 1)
    {
        uv = instance.uv0.zw; // TR
    }
    else if (gl_VertexIndex == 2)
    {
        uv = instance.uv1.zw; // BL
    }
    else
    {
        uv = instance.uv1.xy; // BR
    }

    vUv = uv;
    vTint = instance.tint;
    vTextureIndex = uint(instance.misc.z);
    vWorldPosition = world.xy;
    vLocalPosition = local;
    vExtraTextureIndices = uint[4](
        uint(instance.extraTextureIndices.x),
        uint(instance.extraTextureIndices.y),
        uint(instance.extraTextureIndices.z),
        uint(instance.extraTextureIndices.w));
    vInstanceParams = vec4[4](
        instance.instanceParams0,
        instance.instanceParams1,
        instance.instanceParams2,
        instance.instanceParams3);
}
