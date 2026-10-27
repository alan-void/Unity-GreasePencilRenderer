#ifndef GREASEPENCIL_STROKE_ATTRIBUTES_HH_INCLUDED
#define GREASEPENCIL_STROKE_ATTRIBUTES_HH_INCLUDED

// Per stroke point data that travels next to the Grease Pencil point buffer.
// Mirrors Lineart.Pipeline.StrokePointAttributes (C#). Filled by the edge finders, copied into the dense
// stroke layout by MoveToDenseArray and consumed by the stroke stylize passes.
struct StrokePointAttributes
{
    float3 normal;     // push direction (surface normal for smooth contours, vertex normal for edge lines)
    float weight;      // outline weight of the point (1 on hard silhouettes)
    float miter;       // 1 / |mean outward direction|, 1 on straight lines
    float radiusVar;   // baked radius variation factor
    float pushRadius;  // radius used by the push pass (set by the stylizer)
    float meanWeight;  // mean outline weight over all incident edges
};

StrokePointAttributes DefaultStrokePointAttributes(float3 normal)
{
    StrokePointAttributes a;
    a.normal = normal;
    a.weight = 1.0f;
    a.miter = 1.0f;
    a.radiusVar = 1.0f;
    a.pushRadius = 0.0f;
    a.meanWeight = 1.0f;
    return a;
}

#endif // GREASEPENCIL_STROKE_ATTRIBUTES_HH_INCLUDED
