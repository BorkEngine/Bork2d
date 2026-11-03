void main()
{
    vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;
    float pulse = 0.91 + 0.09 * sin(BORK2D_TIME * 2.0 + uPhase);
    color.rgb *= pulse;
    outColor = color;
}
