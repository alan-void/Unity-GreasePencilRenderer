using System.Runtime.InteropServices;
using UnityEngine;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Per stroke point data stored next to the Grease Pencil point buffer.
    /// Mirrors StrokePointAttributes in Resources/Lineart/ComputeShaders/stroke_attributes.hh.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StrokePointAttributes
    {
        public Vector3 normal;
        public float weight;
        public float miter;
        public float radiusVar;
        public float pushRadius;
        public float meanWeight;

        public static int SizeOf => Marshal.SizeOf(typeof(StrokePointAttributes));
    }
}
