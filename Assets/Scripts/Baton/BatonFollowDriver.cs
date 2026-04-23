using UnityEngine;

[DisallowMultipleComponent]
public class BatonFollowDriver : MonoBehaviour
{
    private const bool EnableOrbitAndSelfRotation = false;

    // disabled: conflicts with CharacterController/Dash and baton collider;
    // re-enable after hitbox/trigger + follow clamping are validated
    private const bool EnableLateralSideBypass = false;

    public enum BatonFollowAnchorMode
    {
        [Tooltip("Player pos + followOffset (camera-relative right/up/forward)")]
        OffsetFromPlayerInViewFrame = 0,

        [Tooltip("Crosshair: heightAbovePlayer + distanceFromPlayer toward screen center ray")]
        ViewportCenterRayAtHeight = 1,
    }

    [SerializeField] private Transform _owner;

    [Header("Anchor")]
    [Tooltip("ViewportCenterRay for crosshair + radius; OffsetFromPlayer for legacy side-front")]
    [SerializeField] private BatonFollowAnchorMode _anchorMode = BatonFollowAnchorMode.ViewportCenterRayAtHeight;

    [Tooltip("Falls back to Camera.main if empty")]
    [SerializeField] private Camera _viewCamera;

    private Vector3 _currentOffset;
    private float _currentHeightAbovePlayer = 1.2f;
    private float _currentDistanceFromPlayer = 0.8f;
    private float _followSmoothTime = 0.22f;
    private float _modeSwitchPositionSmoothTime = 0.1f;
    private float _currentAimResponsiveness = 4f;
    private float _currentMinHorizontalRadius = 0.55f;
    private float _currentLateralBypassWeight = 0.72f;
    private float _currentLateralBackDotThreshold = -0.14f;
    private float _currentLateralFrontDotThreshold = 0.36f;
    private float _currentLateralSideBlendCap = 0.88f;
    private float _stickyLateralSideSign = 1f;
    private float _currentHoverAmplitude;
    private float _currentHoverFrequency = 2f;
    private float _currentOrbitSpeed;
    private float _currentSelfRotSpeed = 90f;

    private Vector3 _smoothVelocity;
    private float _orbitAngle;
    private bool _isOverridden;
    private bool _blendingModeSwitch;
    private float _additiveHeightOffset;
    private float _overrideSmoothTime = -1f;
    private bool _faceCamera;

    private Vector3 _smoothedAimDirXZ;
    private bool _smoothedAimInitialized;

    public void SetOverride(bool active) => _isOverridden = active;

    public void SetAdditiveHeightOffset(float offset) => _additiveHeightOffset = offset;

    /// <summary>Override follow damp time; pass negative to restore default</summary>
    public void SetSmoothTimeOverride(float time) => _overrideSmoothTime = time;

    /// <summary>Aligns baton rotation to camera horizontal facing each frame</summary>
    public void SetFaceCamera(bool enabled) => _faceCamera = enabled;

    public void BeginModeSwitchBlend() => _blendingModeSwitch = true;

    public void EndModeSwitchBlend() => _blendingModeSwitch = false;

    public void ApplyConfig(BatonModeConfig config)
    {
        if (config == null)
            return;

        _currentOffset = config.followOffset;
        _currentHeightAbovePlayer = config.heightAbovePlayer;
        _currentDistanceFromPlayer = Mathf.Max(0f, config.distanceFromPlayer);
        _followSmoothTime = Mathf.Max(0.01f, config.followSmoothTime);
        _modeSwitchPositionSmoothTime = Mathf.Max(0.01f, config.modeSwitchPositionSmoothTime);
        _currentAimResponsiveness = Mathf.Max(0.01f, config.aimHorizontalResponsiveness);
        _currentMinHorizontalRadius = Mathf.Max(0f, config.minHorizontalRadiusFromPlayer);
        _currentLateralBypassWeight = Mathf.Clamp01(config.lateralBypassWeight);
        _currentLateralBackDotThreshold = Mathf.Clamp(config.lateralBackDotThreshold, -1f, 0.05f);
        _currentLateralFrontDotThreshold = Mathf.Clamp(config.lateralFrontDotThreshold, 0f, 1f);
        _currentLateralSideBlendCap = Mathf.Clamp01(config.lateralSideBlendCap);
        _currentHoverAmplitude = config.hoverAmplitude;
        _currentHoverFrequency = config.hoverFrequency;
        _currentOrbitSpeed = config.orbitSpeed;
        _currentSelfRotSpeed = config.selfRotationSpeed;
    }

    public void LerpConfig(BatonModeConfig from, BatonModeConfig to, float t)
    {
        if (to == null)
            return;

        if (from == null)
        {
            ApplyConfig(to);
            return;
        }

        t = Mathf.Clamp01(t);
        _currentOffset = Vector3.Lerp(from.followOffset, to.followOffset, t);
        _currentHeightAbovePlayer = Mathf.Lerp(from.heightAbovePlayer, to.heightAbovePlayer, t);
        _currentDistanceFromPlayer = Mathf.Lerp(
            Mathf.Max(0f, from.distanceFromPlayer),
            Mathf.Max(0f, to.distanceFromPlayer),
            t);
        // follow damp not blended during mode switch; uses dedicated smooth time
        _modeSwitchPositionSmoothTime = Mathf.Lerp(
            Mathf.Max(0.01f, from.modeSwitchPositionSmoothTime),
            Mathf.Max(0.01f, to.modeSwitchPositionSmoothTime),
            t);
        _currentAimResponsiveness = Mathf.Lerp(
            Mathf.Max(0.01f, from.aimHorizontalResponsiveness),
            Mathf.Max(0.01f, to.aimHorizontalResponsiveness),
            t);
        _currentMinHorizontalRadius = Mathf.Lerp(
            Mathf.Max(0f, from.minHorizontalRadiusFromPlayer),
            Mathf.Max(0f, to.minHorizontalRadiusFromPlayer),
            t);
        _currentLateralBypassWeight = Mathf.Lerp(
            Mathf.Clamp01(from.lateralBypassWeight),
            Mathf.Clamp01(to.lateralBypassWeight),
            t);
        _currentLateralBackDotThreshold = Mathf.Lerp(
            Mathf.Clamp(from.lateralBackDotThreshold, -1f, 0.05f),
            Mathf.Clamp(to.lateralBackDotThreshold, -1f, 0.05f),
            t);
        _currentLateralFrontDotThreshold = Mathf.Lerp(
            Mathf.Clamp(from.lateralFrontDotThreshold, 0f, 1f),
            Mathf.Clamp(to.lateralFrontDotThreshold, 0f, 1f),
            t);
        _currentLateralSideBlendCap = Mathf.Lerp(
            Mathf.Clamp01(from.lateralSideBlendCap),
            Mathf.Clamp01(to.lateralSideBlendCap),
            t);
        _currentHoverAmplitude = Mathf.Lerp(from.hoverAmplitude, to.hoverAmplitude, t);
        _currentHoverFrequency = Mathf.Lerp(from.hoverFrequency, to.hoverFrequency, t);
        _currentOrbitSpeed = Mathf.Lerp(from.orbitSpeed, to.orbitSpeed, t);
        _currentSelfRotSpeed = Mathf.Lerp(from.selfRotationSpeed, to.selfRotationSpeed, t);
    }

    private Camera ResolveCamera() => _viewCamera != null ? _viewCamera : Camera.main;

    private void LateUpdate()
    {
        if (_isOverridden || _owner == null)
            return;

        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        float hover = Mathf.Sin(Time.time * _currentHoverFrequency) * _currentHoverAmplitude;

        Vector3 targetPos = _anchorMode == BatonFollowAnchorMode.ViewportCenterRayAtHeight
            ? ComputeTargetCrosshairRadius(cam, hover)
            : ComputeTargetOffsetFromPlayer(cam, hover);

        float dampTime = _overrideSmoothTime >= 0f
            ? _overrideSmoothTime
            : _blendingModeSwitch
                ? _modeSwitchPositionSmoothTime
                : _followSmoothTime;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref _smoothVelocity,
            dampTime);

        if (_faceCamera)
        {
            Vector3 camFwd = cam.transform.forward;
            camFwd.y = 0f;
            if (camFwd.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(camFwd, Vector3.up);
        }
        else if (EnableOrbitAndSelfRotation)
        {
            transform.Rotate(Vector3.up, _currentSelfRotSpeed * Time.deltaTime, Space.Self);
        }
    }

    private void OnDisable()
    {
        _smoothedAimInitialized = false;
        _blendingModeSwitch = false;
        _stickyLateralSideSign = 1f;
    }

    private Vector3 ComputeTargetOffsetFromPlayer(Camera cam, float hover)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 1e-6f)
            camForward = Vector3.forward;
        else
            camForward.Normalize();

        Vector3 camRight = cam.transform.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 1e-6f)
            camRight = Vector3.right;
        else
            camRight.Normalize();

        Vector3 worldOffset = camRight * _currentOffset.x
            + Vector3.up * _currentOffset.y
            + camForward * _currentOffset.z;

        if (EnableOrbitAndSelfRotation)
        {
            _orbitAngle += _currentOrbitSpeed * Time.deltaTime;
            worldOffset = Quaternion.Euler(0f, _orbitAngle, 0f) * worldOffset;
        }

        Vector3 raw = _owner.position + worldOffset + Vector3.up * (hover + _additiveHeightOffset);
        return EnforceMinHorizontalRadiusFromFeet(_owner.position, raw);
    }

    private Vector3 ComputeTargetCrosshairRadius(Camera cam, float hover)
    {
        Vector3 feet = _owner.position;

        Vector3 toAim = cam.transform.forward;
        toAim.y = 0f;
        if (toAim.sqrMagnitude < 1e-6f)
            toAim = Vector3.forward;
        else
            toAim.Normalize();

        toAim = SmoothAimHorizontal(toAim);
        if (EnableLateralSideBypass)
            toAim = ApplyLateralSideBias(cam, toAim);

        float radial = Mathf.Max(_currentDistanceFromPlayer, _currentMinHorizontalRadius);
        Vector3 horizontal = toAim * radial;

        if (EnableOrbitAndSelfRotation)
        {
            _orbitAngle += _currentOrbitSpeed * Time.deltaTime;
            horizontal = Quaternion.Euler(0f, _orbitAngle, 0f) * horizontal;
        }

        return new Vector3(
            feet.x + horizontal.x,
            feet.y + _currentHeightAbovePlayer + hover + _additiveHeightOffset,
            feet.z + horizontal.z);
    }

    /// <summary>Dampens per-frame aim jitter caused by A/D strafing</summary>
    private Vector3 SmoothAimHorizontal(Vector3 rawUnitXZ)
    {
        if (!_smoothedAimInitialized)
        {
            _smoothedAimDirXZ = rawUnitXZ;
            _smoothedAimInitialized = true;
            return rawUnitXZ;
        }

        float k = 1f - Mathf.Exp(-_currentAimResponsiveness * Time.deltaTime);
        _smoothedAimDirXZ = Vector3.Slerp(_smoothedAimDirXZ, rawUnitXZ, k);
        if (_smoothedAimDirXZ.sqrMagnitude < 1e-6f)
            _smoothedAimDirXZ = rawUnitXZ;
        else
            _smoothedAimDirXZ.Normalize();

        return _smoothedAimDirXZ;
    }

    /// <summary>
    /// Slerps aim toward camera left/right when it would clip through chest/back;
    /// side sign uses cross product with hysteresis. Gated by EnableLateralSideBypass.
    /// </summary>
    private Vector3 ApplyLateralSideBias(Camera cam, Vector3 dirXZ)
    {
        if (_currentLateralBypassWeight <= 1e-4f || _owner == null)
            return dirXZ;

        Vector3 flatForward = _owner.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f)
            return dirXZ;
        flatForward.Normalize();

        float facingDot = Vector3.Dot(dirXZ, flatForward);

        float badness = 0f;
        if (facingDot < _currentLateralBackDotThreshold)
            badness = Mathf.Max(badness, Mathf.Clamp01(Mathf.InverseLerp(_currentLateralBackDotThreshold, -1f, facingDot)));
        if (facingDot > _currentLateralFrontDotThreshold)
            badness = Mathf.Max(badness, Mathf.Clamp01(Mathf.InverseLerp(_currentLateralFrontDotThreshold, 1f, facingDot)));

        if (badness <= 1e-4f)
            return dirXZ;

        Vector3 camRight = cam.transform.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 1e-6f)
            return dirXZ;
        camRight.Normalize();

        float crossY = Vector3.Cross(flatForward, dirXZ).y;
        float sideSign;
        if (Mathf.Abs(crossY) > 0.02f)
        {
            sideSign = crossY >= 0f ? 1f : -1f;
            _stickyLateralSideSign = sideSign;
        }
        else
            sideSign = _stickyLateralSideSign;

        Vector3 sideDir = (camRight * sideSign).normalized;
        float blend = badness * _currentLateralSideBlendCap * _currentLateralBypassWeight;
        Vector3 result = Vector3.Slerp(dirXZ, sideDir, blend);
        return result.sqrMagnitude > 1e-6f ? result.normalized : sideDir;
    }

    /// <summary>Pushes target outward on XZ if closer than min horizontal radius to feet</summary>
    private Vector3 EnforceMinHorizontalRadiusFromFeet(Vector3 feet, Vector3 worldPos)
    {
        Vector3 delta = worldPos - feet;
        float y = delta.y;
        delta.y = 0f;
        float mag = delta.magnitude;
        if (mag >= _currentMinHorizontalRadius - 1e-5f)
            return worldPos;

        if (mag < 1e-5f)
        {
            Camera cam = ResolveCamera();
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f)
                f = Vector3.forward;
            delta = f.normalized * _currentMinHorizontalRadius;
        }
        else
            delta *= _currentMinHorizontalRadius / mag;

        return feet + new Vector3(delta.x, y, delta.z);
    }
}
