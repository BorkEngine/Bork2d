void main()
{
    vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;
    float pulse = 0.5 + 0.5 * sin(BORK2D_TIME * 2.0 + uPhase);
    color.rgb *= pulse;
    outColor = color;
}
