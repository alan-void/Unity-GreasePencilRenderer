using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lineart.Pipeline
{
    public static class ViewCameraResolver
    {
        /// <summary>Explicit camera, else (editor only) the last active scene view camera, else Camera.main.</summary>
        public static Camera Resolve(Camera explicitCamera)
        {
            if (explicitCamera != null) return explicitCamera;
#if UNITY_EDITOR
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null) return sceneView.camera;
#endif
            return Camera.main;
        }
    }
}
