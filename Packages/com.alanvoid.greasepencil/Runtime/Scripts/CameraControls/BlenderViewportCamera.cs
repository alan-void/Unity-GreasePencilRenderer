using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraControls
{
    /// <summary>
    /// Blender style viewport navigation for a play mode camera (default Blender keymap):
    ///   MMB drag            orbit (turntable)          Shift + MMB drag     pan
    ///   Ctrl + MMB drag     zoom                        Mouse wheel          zoom
    ///   Numpad 1 / 3 / 7    front / right / top         + Ctrl               back / left / bottom
    ///   Numpad 2 4 6 8      orbit in 15 degree steps    Numpad 9             view from the opposite side
    ///   Numpad 5            toggle ortho / perspective  Numpad . or Home     frame the target
    ///   Numpad 0            back to the authored camera
    /// Optional Alt + LMB emulates the middle mouse button (Blender's "Emulate 3 Button Mouse").
    /// Runs before other scripts so view dependent effects (line art) see this frame's camera.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(Camera))]
    public class BlenderViewportCamera : MonoBehaviour
    {
        [Tooltip("Objects framed by Home / Numpad . (renderer bounds). Also the initial orbit centre.")]
        public Transform frameTarget;

        [Header("Speeds")]
        [Tooltip("Degrees per pixel of mouse movement.")]
        public float orbitSpeed = 0.3f;
        [Tooltip("Zoom factor per wheel notch.")]
        public float wheelZoomFactor = 1.2f;
        [Tooltip("Zoom factor per 100 pixels of Ctrl + MMB drag.")]
        public float dragZoomFactor = 2f;
        [Tooltip("Degrees per Numpad 2/4/6/8 press.")]
        public float orbitStep = 15f;

        [Header("Input")]
        public bool emulateThreeButtonMouse;

        Camera _camera;

        // view state, Blender style: a pivot, a distance to it and a turntable orientation
        Vector3 _pivot;
        float _distance;
        float _yaw;
        float _pitch;

        // authored camera, restored by Numpad 0
        Vector3 _authoredPosition;
        Quaternion _authoredRotation;
        bool _authoredOrthographic;
        float _authoredOrthoSize;
        float _authoredFieldOfView;
        float _authoredFarClip;

        void Start()
        {
            _camera = GetComponent<Camera>();
            _authoredPosition = transform.position;
            _authoredRotation = transform.rotation;
            _authoredOrthographic = _camera.orthographic;
            _authoredOrthoSize = _camera.orthographicSize;
            _authoredFieldOfView = _camera.fieldOfView;
            _authoredFarClip = _camera.farClipPlane;
            ResetToAuthored();
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse != null) HandleMouse(mouse, keyboard);
            if (keyboard != null) HandleKeyboard(keyboard);
            Apply();
        }

        // ------------------------------------------------------------------------------------------
        // Input

        void HandleMouse(Mouse mouse, Keyboard keyboard)
        {
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;
            bool ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
            bool alt = keyboard != null && keyboard.altKey.isPressed;

            bool navigate = mouse.middleButton.isPressed || (emulateThreeButtonMouse && alt && mouse.leftButton.isPressed);
            Vector2 delta = mouse.delta.ReadValue();
            if (navigate && delta != Vector2.zero)
            {
                if (shift) Pan(delta);
                else if (ctrl) Zoom(Mathf.Pow(dragZoomFactor, -delta.y / 100f));
                else Orbit(delta.x * orbitSpeed, -delta.y * orbitSpeed);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f) Zoom(scroll > 0f ? 1f / wheelZoomFactor : wheelZoomFactor);
        }

        void HandleKeyboard(Keyboard keyboard)
        {
            bool ctrl = keyboard.ctrlKey.isPressed;

            if (keyboard.numpad1Key.wasPressedThisFrame) SetView(ctrl ? 180f : 0f, 0f);       // front / back
            if (keyboard.numpad3Key.wasPressedThisFrame) SetView(ctrl ? 90f : -90f, 0f);      // right / left
            if (keyboard.numpad7Key.wasPressedThisFrame) SetView(0f, ctrl ? -90f : 90f);      // top / bottom

            if (keyboard.numpad4Key.wasPressedThisFrame) Orbit(orbitStep, 0f);
            if (keyboard.numpad6Key.wasPressedThisFrame) Orbit(-orbitStep, 0f);
            if (keyboard.numpad8Key.wasPressedThisFrame) Orbit(0f, orbitStep);
            if (keyboard.numpad2Key.wasPressedThisFrame) Orbit(0f, -orbitStep);
            if (keyboard.numpad9Key.wasPressedThisFrame) Orbit(180f, 0f);

            if (keyboard.numpad5Key.wasPressedThisFrame) ToggleProjection();
            if (keyboard.numpadPeriodKey.wasPressedThisFrame || keyboard.homeKey.wasPressedThisFrame) Frame();
            if (keyboard.numpad0Key.wasPressedThisFrame) ResetToAuthored();
        }

        // ------------------------------------------------------------------------------------------
        // Navigation

        void Orbit(float yawDegrees, float pitchDegrees)
        {
            // turntable: yaw around world up, pitch around the view's right axis (no clamping, like Blender)
            _yaw += yawDegrees;
            _pitch += pitchDegrees;
        }

        void Pan(Vector2 pixelDelta)
        {
            float unitsPerPixel = 2f * ViewHalfHeight() / Mathf.Max(_camera.pixelHeight, 1);
            Quaternion rotation = Rotation();
            _pivot -= rotation * new Vector3(pixelDelta.x, pixelDelta.y, 0f) * unitsPerPixel;
        }

        void Zoom(float factor)
        {
            if (_camera.orthographic) _camera.orthographicSize = Mathf.Max(1e-3f, _camera.orthographicSize * factor);
            else _distance = Mathf.Max(1e-3f, _distance * factor);
        }

        void SetView(float yaw, float pitch)
        {
            // Blender front looks along its +Y, i.e. Unity +Z after the FBX axis conversion
            _yaw = yaw;
            _pitch = pitch;
        }

        void ToggleProjection()
        {
            // keep the apparent size at the pivot
            float halfTan = Mathf.Tan(0.5f * _camera.fieldOfView * Mathf.Deg2Rad);
            if (_camera.orthographic)
            {
                _distance = _camera.orthographicSize / halfTan;
                _camera.orthographic = false;
            }
            else
            {
                _camera.orthographicSize = _distance * halfTan;
                _camera.orthographic = true;
            }
        }

        void Frame()
        {
            if (!TryGetTargetBounds(out Bounds bounds)) return;
            _pivot = bounds.center;
            float radius = bounds.extents.magnitude;
            float halfHeight = radius / Mathf.Min(1f, _camera.aspect);
            if (_camera.orthographic)
            {
                _camera.orthographicSize = halfHeight;
                _distance = Mathf.Max(_distance, 2f * radius);
            }
            else
            {
                _distance = halfHeight / Mathf.Sin(0.5f * _camera.fieldOfView * Mathf.Deg2Rad);
            }
        }

        void ResetToAuthored()
        {
            _camera.orthographic = _authoredOrthographic;
            _camera.orthographicSize = _authoredOrthoSize;
            _camera.fieldOfView = _authoredFieldOfView;

            Vector3 forward = _authoredRotation * Vector3.forward;
            _pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            _yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            // orbit around the point of the view axis closest to the target, so the framing does not jump
            Vector3 center = TryGetTargetBounds(out Bounds bounds) ? bounds.center : _authoredPosition + forward * 10f;
            _distance = Mathf.Max(0.1f, Vector3.Dot(center - _authoredPosition, forward));
            _pivot = _authoredPosition + forward * _distance;
        }

        // ------------------------------------------------------------------------------------------

        Quaternion Rotation() => Quaternion.Euler(_pitch, _yaw, 0f);

        float ViewHalfHeight() => _camera.orthographic
            ? _camera.orthographicSize
            : _distance * Mathf.Tan(0.5f * _camera.fieldOfView * Mathf.Deg2Rad);

        void Apply()
        {
            Quaternion rotation = Rotation();
            transform.SetPositionAndRotation(_pivot - rotation * Vector3.forward * _distance, rotation);

            float targetRadius = TryGetTargetBounds(out Bounds bounds) ? bounds.extents.magnitude : 0f;
            _camera.farClipPlane = Mathf.Max(_authoredFarClip, _distance + 2f * targetRadius);
        }

        bool TryGetTargetBounds(out Bounds bounds)
        {
            bounds = default;
            if (frameTarget == null) return false;
            bool any = false;
            foreach (var r in frameTarget.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }
    }
}
