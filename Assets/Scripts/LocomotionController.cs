using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Quest locomotion controller.
///   Right joystick  → move forward/back/strafe  (relative to camera look direction)
///   Left joystick   → smooth yaw rotation of the XR Rig
///   A button        → jump (simple kinematic arc, lands back at Y=0)
///
/// Unity setup:
///   - Attach to any persistent GameObject (e.g. the XR Rig root).
///   - xrRig     → drag the XR Rig root transform.
///   - cameraTransform → drag the Main Camera (inside CameraOffset).
/// </summary>
public class LocomotionController : MonoBehaviour
{
    [Header("Scene references")]
    public Transform xrRig;
    public Transform cameraTransform;

    [Header("Movement")]
    public float moveSpeed = 1.5f;

    [Header("Camera rotation (left joystick)")]
    public float turnSpeed = 60f;

    [Header("Jump (A button)")]
    public float jumpForce   = 3f;
    public float gravity     = -9.8f;

    // -------------------------------------------------------------------------
    private InputDevice _right, _left;
    private bool _rightFound, _leftFound;
    private float _nextSearch;

    private bool  _aWasPressed;
    private float _verticalVelocity;
    private bool  _isGrounded = true;
    private float _groundY;             // world Y of the floor at startup

    private void Start()
    {
        if (xrRig == null)            xrRig            = transform;
        if (cameraTransform == null)  cameraTransform  = Camera.main ? Camera.main.transform : transform;
        _groundY = xrRig.position.y;   // record ground level once
    }

    private void Update()
    {
        TryFindControllers();
        if (AppStateManager.Instance != null && AppStateManager.Instance.CurrentState == AppState.Splash)
            return;
        Move();
        Rotate();
        Jump();
        ClampToGround();
    }

    // -------------------------------------------------------------------------

    private static bool AnyMenuOpen => RadialMenu.IsOpen || OptionSelectionMenu.IsOpen;

    private void Move()
    {
        if (!_rightFound) return;
        if (AnyMenuOpen) return;
        _right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        if (stick.sqrMagnitude < 0.04f) return;

        Vector3 fwd   = cameraTransform.forward; fwd.y = 0f;   fwd.Normalize();
        Vector3 right = cameraTransform.right;   right.y = 0f; right.Normalize();

        Vector3 delta = (fwd * stick.y + right * stick.x) * (moveSpeed * Time.deltaTime);
        xrRig.position += delta;
    }

    private void Rotate()
    {
        if (!_leftFound) return;
        if (AnyMenuOpen) return;
        _left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        if (Mathf.Abs(stick.x) < 0.1f) return;

        xrRig.Rotate(Vector3.up, stick.x * turnSpeed * Time.deltaTime, Space.World);
    }

    private void Jump()
    {
        if (!_rightFound) return;
        if (AnyMenuOpen) return;
        _right.TryGetFeatureValue(CommonUsages.primaryButton, out bool aNow);

        if (aNow && !_aWasPressed && _isGrounded)
        {
            _groundY = xrRig.position.y;   // re-sample floor Y at jump time, not at Start
            _verticalVelocity = jumpForce;
            _isGrounded = false;
        }

        if (!_isGrounded)
        {
            _verticalVelocity += gravity * Time.deltaTime;
            Vector3 p = xrRig.position;
            p.y += _verticalVelocity * Time.deltaTime;
            if (p.y <= _groundY)
            {
                p.y = _groundY;
                _verticalVelocity = 0f;
                _isGrounded = true;
            }
            xrRig.position = p;
            // When grounded we intentionally do NOT touch xrRig.y —
            // letting the Quest tracking system own the floor level prevents
            // the per-frame Y-clamping fight that caused post-jump wobble.
        }

        _aWasPressed = aNow;
    }

    private void ClampToGround()
    {
        // Only catch "fell off world edge" — don't pin Y every frame while grounded
        // (that was fighting Quest tracking and causing wobble).
        if (!_isGrounded && xrRig.position.y < _groundY - 0.05f)
        {
            Vector3 p = xrRig.position;
            p.y = _groundY;
            xrRig.position = p;
            _verticalVelocity = 0f;
            _isGrounded = true;
        }
    }

    // -------------------------------------------------------------------------

    private void TryFindControllers()
    {
        if (_rightFound && _leftFound) return;
        if (Time.time < _nextSearch) return;
        _nextSearch = Time.time + 2f;

        var list = new System.Collections.Generic.List<InputDevice>();

        if (!_rightFound)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, list);
            if (list.Count > 0) { _right = list[0]; _rightFound = true; list.Clear(); }
        }

        if (!_leftFound)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, list);
            if (list.Count > 0) { _left = list[0]; _leftFound = true; }
        }
    }
}
