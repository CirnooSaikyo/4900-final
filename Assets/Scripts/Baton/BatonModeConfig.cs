using UnityEngine;

[CreateAssetMenu(fileName = "BatonModeConfig", menuName = "Conductor/BatonModeConfig")]
public class BatonModeConfig : ScriptableObject
{
    [Header("Crosshair Anchor (ViewportCenterRay)")]
    [Tooltip("Baton height above player feet (meters)")]
    public float heightAbovePlayer = 1.2f;

    [Tooltip("XZ distance from player along crosshair direction (meters)")]
    public float distanceFromPlayer = 0.8f;

    [Header("Offset Anchor (OffsetFromPlayer only)")]
    [Tooltip("X=camera right, Y=up, Z=camera forward. Ignored in crosshair mode.")]
    public Vector3 followOffset = new(0f, 1.2f, 0.8f);

    [Header("Follow Damping")]
    [Tooltip("SmoothDamp time for normal idle/movement; unrelated to Q switch")]
    public float followSmoothTime = 0.22f;

    [Tooltip("SmoothDamp time during Q mode-switch interpolation (usually shorter = snappier)")]
    public float modeSwitchPositionSmoothTime = 0.1f;

    [Header("Aim Horizontal Smoothing")]
    [Tooltip("Higher = aim tracks faster; lower = less jitter from A/D strafing")]
    public float aimHorizontalResponsiveness = 4f;

    [Header("Horizontal Clearance")]
    [Tooltip("Min XZ distance from player feet (meters); prevents clipping")]
    public float minHorizontalRadiusFromPlayer = 0.55f;

    // lateral bypass disabled in BatonFollowDriver; kept serialized for later
    [HideInInspector]
    public float lateralBypassWeight = 0.72f;

    [HideInInspector]
    public float lateralBackDotThreshold = -0.14f;

    [HideInInspector]
    public float lateralFrontDotThreshold = 0.36f;

    [HideInInspector]
    public float lateralSideBlendCap = 0.88f;

    [Header("Hover")]
    public float hoverAmplitude = 0.1f;
    public float hoverFrequency = 2f;

    [HideInInspector]
    public float orbitSpeed = 30f;

    [HideInInspector]
    public float selfRotationSpeed = 90f;
}
