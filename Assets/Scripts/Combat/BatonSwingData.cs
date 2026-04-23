using UnityEngine;

[CreateAssetMenu(fileName = "BatonSwingData", menuName = "Conductor/BatonSwingData")]
public class BatonSwingData : ScriptableObject
{
    [Tooltip("Total duration (seconds); keyframe normalizedTime * this = actual time")]
    public float totalDuration = 0.4f;

    public SwingKeyframe[] keyframes = new SwingKeyframe[]
    {
        new() { normalizedTime = 0f },
        new() { normalizedTime = 1f }
    };

    [Header("Damped Spring (procedural secondary motion)")]
    [Tooltip("Spring stiffness: higher = tighter tracking. Melee ~200+, ranged/conduct ~60-80")]
    public float springStiffness = 120f;
    [Tooltip("Damping: lower = more overshoot. Melee ~20+, ranged ~8-12")]
    public float springDamping = 14f;

    [Header("Impact Feedback")]
    [Tooltip("Shake frequency on impact frame")]
    public float impactShakeFrequency = 25f;
    [Tooltip("Shake decay rate (higher = faster falloff)")]
    public float impactShakeDecay = 8f;
    [Tooltip("Max shake amplitude (local units)")]
    public float impactShakeAmplitude = 0.03f;
}

[System.Serializable]
public struct SwingKeyframe
{
    [Range(0f, 1f)]
    public float normalizedTime;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;

    [Tooltip("Position ease curve to next keyframe")]
    public AnimationCurve positionEase;
    [Tooltip("Rotation ease curve to next keyframe")]
    public AnimationCurve rotationEase;

    [Header("Impact")]
    public bool isImpactFrame;
    [Tooltip("Swing progress freeze duration (seconds) - hitstop")]
    public float hitstopDuration;
    [Tooltip("Shake duration (seconds), fires with hitstop; can outlast it")]
    public float shakeDuration;
}
