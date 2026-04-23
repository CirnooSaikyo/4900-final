using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Camera pivot independent of player rotation: follows player position,
/// yaw + partial pitch on self rotation (orbit displacement),
/// remaining pitch via CinemachinePanTilt (pure look rotation).
/// </summary>
[DisallowMultipleComponent]
public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform _followTarget;

    [Header("Cinemachine")]
    [Tooltip("PanTilt on the main game camera; tilt range controlled by TiltAxis.Range")]
    [SerializeField] private CinemachinePanTilt _panTilt;
    [Tooltip("PanTilt on UltimateCam (synced to prevent rotation jump on switch). Optional.")]
    [SerializeField] private CinemachinePanTilt _ultimatePanTilt;

    [Header("Sensitivity")]
    [SerializeField] private float _sensitivity = 0.15f;

    [Header("Position Offset")]
    [Tooltip("Height offset from player, roughly chest/shoulder level")]
    [SerializeField] private float _heightOffset = 1.5f;

    [Header("Pitch Orbit")]
    [Tooltip("Fraction of pitch driving orbit displacement (0=pure look, 1=full orbit). ~0.25-0.4 recommended.")]
    [Range(0f, 1f)]
    [SerializeField] private float _orbitPitchFraction = 0.3f;

    [Header("Cursor")]
    [SerializeField] private bool _lockCursorInPlayMode = true;

    private float _yaw;
    private float _pitch;
    private Vector2 _lookInput;
    private bool _warnedNoPanTilt;

    public void SetLookInput(Vector2 delta) => _lookInput = delta;

    private void OnEnable()
    {
        if (_lockCursorInPlayMode && Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Start()
    {
        if (_followTarget != null)
            _yaw = _followTarget.eulerAngles.y;
    }

    private void LateUpdate()
    {
        Vector2 delta = _lookInput;
        _yaw += delta.x * _sensitivity;
        _pitch -= delta.y * _sensitivity;

        if (_followTarget != null)
            transform.position = _followTarget.position + Vector3.up * _heightOffset;

        if (_panTilt != null)
        {
            Vector2 range = _panTilt.TiltAxis.Range;
            _pitch = Mathf.Clamp(_pitch, range.x, range.y);

            float orbitPitch = _pitch * _orbitPitchFraction;
            float tiltPitch = _pitch - orbitPitch;

            transform.rotation = Quaternion.Euler(orbitPitch, _yaw, 0f);
            _panTilt.TiltAxis.Value = tiltPitch;
            _panTilt.PanAxis.Value = 0f;

            if (_ultimatePanTilt != null)
            {
                _ultimatePanTilt.TiltAxis.Value = tiltPitch;
                _ultimatePanTilt.PanAxis.Value = 0f;
            }
        }
        else
        {
            _pitch = Mathf.Clamp(_pitch, -90f, 90f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            if (!_warnedNoPanTilt)
            {
                _warnedNoPanTilt = true;
                Debug.LogWarning(
                    "[CameraController] No PanTilt assigned; mouse pitch won't work. Drag the CinemachineCamera's PanTilt here.",
                    this);
            }
        }

        _lookInput = Vector2.zero;
    }
}
