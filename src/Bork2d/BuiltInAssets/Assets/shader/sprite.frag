void main()
{
    vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;

    // The engine-owned vertex stage always writes the custom-shader interface. The default shader
    // does not use those inputs, but an unconsumed vertex output makes validation warn, so consume
    // them in a branch that cannot be reached for valid engine data: every bindless index is below
    // the engine's 1,048,576-entry cap (0x00100000), and an OR of the four extra indices above
    // 0x003FFFFF is impossible. The branch body never runs, so no valid frame changes.
    uint extra = vExtraTextureIndices[0] | vExtraTextureIndices[1]
        | vExtraTextureIndices[2] | vExtraTextureIndices[3];
    if (extra > 0x003FFFFFu)
    {
        color = texture(bork2dTextures[nonuniformEXT(vExtraTextureIndices[0])], vUv) * vTint;
        color += vec4(vWorldPosition.x, vLocalPosition.x, 0.0, 0.0);
        color += vInstanceParams[0] + vInstanceParams[1] + vInstanceParams[2] + vInstanceParams[3];
    }

    outColor = color;
}
