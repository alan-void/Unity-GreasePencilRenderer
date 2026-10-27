using UnityEngine;

namespace NPR
{
    /// <summary>
    /// Port of Blender's "NPR • Light Rig": publishes key / rim light direction and colour as shader globals
    /// for NPR/Toon. Directions come from the light transforms; colours are authored here in linear space so
    /// they match Blender's values exactly. The key light should be URP's main (shadow casting) directional light.
    /// </summary>
    [ExecuteAlways]
    public class NprLightRig : MonoBehaviour
    {
        static readonly int KeyDirection = Shader.PropertyToID("_NprKeyDirection");
        static readonly int KeyColor = Shader.PropertyToID("_NprKeyColor");
        static readonly int RimDirection = Shader.PropertyToID("_NprRimDirection");
        static readonly int RimColor = Shader.PropertyToID("_NprRimColor");

        [Header("Key")]
        public Transform keyLight;
        [ColorUsage(false, true)] public Color keyColor = new(1f, 0.96f, 0.9f);
        public float keyStrength = 1f;

        [Header("Rim")]
        public Transform rimLight;
        [ColorUsage(false, true)] public Color rimColor = new(0.75f, 0.88f, 1f);
        public float rimStrength = 1f;

        void OnEnable() => Publish();
        void LateUpdate() => Publish();
        void OnValidate() => Publish();

        /// <summary>Colours are linear values; Color fields hold them verbatim.</summary>
        public void Publish()
        {
            Shader.SetGlobalVector(KeyDirection, TowardsLight(keyLight));
            Shader.SetGlobalVector(KeyColor, (Vector4)(keyColor * keyStrength));
            Shader.SetGlobalVector(RimDirection, TowardsLight(rimLight));
            Shader.SetGlobalVector(RimColor, (Vector4)(rimColor * rimStrength));
        }

        static Vector3 TowardsLight(Transform light) => light != null ? -light.forward : Vector3.up;
    }
}
