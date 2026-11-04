void main()
{
    // A slow sine glow keeps the court alive without competing with the ball. Multiplying only rgb
    // leaves the anti-aliasing coverage in alpha intact.
    float glow = 0.9 + 0.1 * sin(BORK2D_TIME * 1.5);
    vec4 color = vColor;
    color.rgb *= glow;

    // vLineUv.y is the distance along the line in world units, so the dash pattern does not change
    // with the screen size. The border leaves the dash uniform at 0 and draws solid.
    if (dash > 0.5 && fract(vLineUv.y * 1.75) >= 0.5)
    {
        discard;
    }

    if (vLineFlags > 0.5)
    {
        // Same screen-space edge ramp as the built-in line shader: min() is the distance to the
        // nearer stroke edge and fwidth converts it to this pixel's coverage width.
        float edge = min(vLineUv.x, 1.0 - vLineUv.x);
        float aaWidth = fwidth(vLineUv.x);
        float coverage = aaWidth > 0.0 ? smoothstep(0.0, aaWidth, edge) : 1.0;
        color.a *= coverage;
    }

    outColor = color;
}
