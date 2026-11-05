float sdfMedian(vec3 value)
{
    return max(min(value.r, value.g), min(max(value.r, value.g), value.b));
}

void main()
{
    vec3 sampleValue = texture(BORK2D_MAIN_TEXTURE, vUv).rgb;
    float distance = sdfMedian(sampleValue);
    float width = max(fwidth(distance) * uSoftness, 1e-4);
    float coverage = smoothstep(uThreshold - width, uThreshold + width, distance);
    outColor = vec4(vTint.rgb, vTint.a * coverage);
}
