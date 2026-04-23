using UnityEngine;

/// <summary>
/// Mode switch transition params (melee/ranged). Data-only ScriptableObject.
/// </summary>
[CreateAssetMenu(menuName = "Conductor/ModeSwitchTransitionData", fileName = "ModeSwitchTransitionData")]
public class ModeSwitchTransitionData : ScriptableObject
{
    [Header("Melee to Ranged: Phase 1 - Tilt to Horizontal")]
    [Tooltip("Target horizontal local euler angles")]
    public Vector3 nearToFarHorizontalRotation = new Vector3(90f, 0f, 0f);
    [Tooltip("Tilt duration (seconds)")]
    public float nearToFarTiltDuration = 0.15f;

    [Header("Melee to Ranged: Phase 2 - Spin Fly-Out")]
    [Tooltip("Y-axis spin revolutions during fly-out")]
    public float nearToFarSpinCount = 2f;
    [Tooltip("Fly-out duration (seconds)")]
    public float nearToFarFlyDuration = 0.4f;
    [Tooltip("Knockback detection radius on arrival (world space)")]
    public float nearToFarDamageRadius = 2.5f;
    [Tooltip("Knockback impulse force on hit")]
    public float nearToFarKnockbackForce = 8f;
    [Tooltip("Base damage for melee-to-ranged switch hit")]
    public float nearToFarDamage = 20f;

    [Header("Ranged to Melee: Return Pose")]
    [Tooltip("Target local euler angles when returning to melee")]
    public Vector3 farToNearReturnRotation = new Vector3(0f, 0f, 0f);
    [Tooltip("Tilt duration (seconds, kept for reference)")]
    public float farToNearTiltDuration = 0.12f;

    [Header("Ranged to Melee: Phase 2 - Rush Back")]
    [Tooltip("Rush return duration (seconds)")]
    public float farToNearRushDuration = 0.25f;
    [Tooltip("Damage check interval during rush (seconds)")]
    public float farToNearCheckInterval = 0.06f;
    [Tooltip("Sphere cast radius per check (world space)")]
    public float farToNearDamageRadius = 1.0f;
    [Tooltip("Base damage for ranged-to-melee switch hit")]
    public float farToNearDamage = 15f;
    [Tooltip("Knockback impulse force on hit")]
    public float farToNearKnockbackForce = 4f;

    [Header("Shared")]
    [Tooltip("Hit detection layer mask (typically enemy layer)")]
    public LayerMask enemyLayer;
}
