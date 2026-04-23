using Animancer;
using DG.Tweening;
using UnityEngine;

[CreateAssetMenu(fileName = "AttackData", menuName = "Conductor/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("General")]
    public string attackName;
    public float damage = 10f;
    [Tooltip("Per-segment damage override by index; falls back to damage if empty or out of range")]
    public float[] segmentDamages;

    [Header("Target Search")]
    [Tooltip("Search radius around the player")]
    public float targetSearchRange = 6f;

    [Header("Baton Swing (takes priority over comboSequence)")]
    [Tooltip("Swing trajectories played in order, one per input. Overrides comboSequence when set")]
    public BatonSwingData[] swingSequence;

    [Header("Combo Animation")]
    [Tooltip("Attack ClipTransitions played in order, one per input")]
    public ClipTransition[] comboSequence;

    [Header("Body Layer (optional, separate from Baton)")]
    [Tooltip("Matched 1:1 with comboSequence; leave a slot empty to only play the baton visual")]
    public ClipTransition[] bodyComboSequence;

    [Header("Combo")]
    [Tooltip("Window after each segment ends where input continues the combo (seconds)")]
    public float comboWindowDuration = 0.4f;

    [Header("Hitbox Window (normalized time)")]
    [Range(0f, 1f)] public float hitboxActiveStart = 0.2f;
    [Range(0f, 1f)] public float hitboxActiveEnd = 0.6f;

    [Header("Baton Lunge")]
    [Tooltip("Fly baton toward target on attack (skipped when no target)")]
    public bool batonFlyToTarget = true;
    [Tooltip("Lunge duration in seconds")]
    public float batonFlyDuration = 0.2f;
    [Tooltip("Stop distance in front of target to avoid clipping")]
    public float batonStopOffset = 0.8f;
    [Tooltip("Lunge easing curve")]
    public Ease batonFlyEase = Ease.InOutQuad;

    [Header("Knockback")]
    public float knockbackForce = 2f;

    [Header("Energy")]
    [Tooltip("Energy gained per hit")]
    public float energyGainPerHit = 10f;

    [Header("Feedback")]
    public GameObject hitVFX;
    public AudioClip hitSFX;
    public float hitPauseDuration;
    public float screenShakeIntensity;

    public float GetSegmentDamage(int segmentIndex) =>
        segmentDamages != null && segmentIndex >= 0 && segmentIndex < segmentDamages.Length
            ? segmentDamages[segmentIndex]
            : damage;
}
