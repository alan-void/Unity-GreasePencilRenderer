#ifndef GREASEPENCIL_DENSE_STROKE_LIB_HH_INCLUDED
#define GREASEPENCIL_DENSE_STROKE_LIB_HH_INCLUDED

// Helpers for walking the dense Grease Pencil point layout produced by StrokesToGreasePencil.
// Layout per stroke (offset = stroke_id): [pad][p0 .. pN-1][dup p0 if cyclic][pad]
// Requires `_denseArray` (RWStructuredBuffer<GreasePencilStrokeVert>) and `_DenseCount` to be declared.

int GetPrevPointIndex(int i, GreasePencilStrokeVert v)
{
    int prev = i - 1;
    if (prev < 0)
    {
        return -1;
    }

    GreasePencilStrokeVert vp = _denseArray[prev];
    if (vp.mat >= 0 && vp.stroke_id == v.stroke_id) return prev;

    if (is_cyclic(v))
    {
        int last = vp.stroke_id - 2;
        if (last >= 0)
        {
            GreasePencilStrokeVert vl = _denseArray[last];
            if (vl.mat >= 0 && vl.stroke_id == v.stroke_id) return last;
        }
    }

    return -1;
}

int GetNextPointIndex(int i, GreasePencilStrokeVert v)
{
    int next = i + 1;
    if (next < (int)_DenseCount)
    {
        GreasePencilStrokeVert vn = _denseArray[next];
        if (vn.mat >= 0 && vn.stroke_id == v.stroke_id) return next;
    }

    if (is_cyclic(v))
    {
        int first = v.stroke_id + 2;
        if (first < (int)_DenseCount)
        {
            GreasePencilStrokeVert vf = _denseArray[first];
            if (vf.mat >= 0 && vf.stroke_id == v.stroke_id) return first;
        }
    }

    return -1;
}

float2 safe_normalize_2d(float2 v)
{
    float len_sq = dot(v, v);
    if (len_sq <= 1e-12f) return float2(0.0f, 0.0f);
    return v * rsqrt(len_sq);
}

float2 perpendicular_ccw(float2 v)
{
    return float2(-v.y, v.x);
}

uint unit_float_to_uchar_clamp(float val)
{
    return (uint)round(saturate(val) * 255.0);
}

uint pack_rotation_aspect_hardness_miter(
    const float rot,
    const float asp,
    const float softness,
    const float miter_angle)
{
    uint packed = 0;
    /* Aspect uses 9 bits */
    float asp_normalized = (asp > 1.0f) ? (1.0f / asp) : asp;
    /* Use the default aspect ratio of 1 when the value is outside of the valid range. */
    if (asp_normalized <= 0.0f) {
        asp_normalized = 1.0f;
    }
    packed |= uint(unit_float_to_uchar_clamp(asp_normalized));
    /* Store if inverted in the 9th bit. */
    if (asp > 1.0f) {
        packed |= 1 << 8;
    }
    /* Rotation uses 9 bits */
    /* Rotation are in [-90..90] degree range, so we can encode the sign of the angle + the cosine
     * because the cosine will always be positive. */
    packed |= unit_float_to_uchar_clamp(cos(rot)) << 9;
    /* Store sine sign in 9th bit. */
    if (rot < 0.0f) {
        packed |= 1 << 17;
    }
    /* Hardness uses 8 bits */
    packed |= unit_float_to_uchar_clamp(1.0f - softness) << 18;
    

    /* Miter Angle uses the last 6 bits */
    if (miter_angle <= GP_STROKE_MITER_ANGLE_ROUND) {
        packed |= GP_CORNER_TYPE_ROUND_BITS << 26;
    }
    else if (miter_angle >= GP_STROKE_MITER_ANGLE_BEVEL) {
        packed |= GP_CORNER_TYPE_BEVEL_BITS << 26;
    }
    else {
        const float miter_norm = (miter_angle / PI);
        packed |= clamp(int(miter_norm * GP_CORNER_TYPE_MITER_NUMBER), 1, GP_CORNER_TYPE_MITER_NUMBER) << 26;
    }

    return packed;
}

#endif // GREASEPENCIL_DENSE_STROKE_LIB_HH_INCLUDED
