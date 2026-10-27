using System.Runtime.InteropServices;
using UnityEngine;

namespace Lineart.Silhouette
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SilhouetteSourceVertex
    {
        public Vector3 position;
        public Vector3 normal;
    }
}
