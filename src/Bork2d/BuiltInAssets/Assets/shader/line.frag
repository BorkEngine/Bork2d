void main()
{
    vec4 color = vColor;
    if (vLineFlags > 0.5)
    {
        // vLineUv.x is 0 at one stroke edge and 1 at the other, so the distance to the nearest edge
        // is the smaller of the two. fwidth gives this coordinate's change per pixel, which turns
        // that distance into a screen-space coverage ramp regardless of the world-space width.
        float edge = min(vLineUv.x, 1.0 - vLineUv.x);
        float aaWidth = fwidth(vLineUv.x);
        float coverage = aaWidth > 0.0 ? smoothstep(0.0, aaWidth, edge) : 1.0;
        color.a *= coverage;
    }

    outColor = color;
}
