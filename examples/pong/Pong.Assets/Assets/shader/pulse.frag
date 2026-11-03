void main()
{
    float p = min(1.0, (BORK2D_TIME - lastHit) * 3.0);
    vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;
    color.rgb *= 1.0 - p * 0.6;
    outColor = color;
}
