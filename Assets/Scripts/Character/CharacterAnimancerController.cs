using Animancer;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class CharacterAnimancerController : MonoBehaviour
{
    [SerializeField] private AnimancerComponent _animancer;

    [Header("Locomotion")]
    [SerializeField] private ClipTransition _idle;
    [FormerlySerializedAs("_run")]
    [SerializeField] private ClipTransition _walk;
    [Tooltip("Hold-shift sprint clip; falls back to walk if empty")]
    [SerializeField] private ClipTransition _sprint;
    [SerializeField] private ClipTransition _dash;

    [Tooltip("Crossfade for idle/move/sprint transitions")]
    [SerializeField] private float _locomotionFadeDuration = 0.12f;

    [Tooltip("Longer fade from sprint to walk to soften the transition")]
    [SerializeField] private float _sprintToWalkFadeDuration = 0.24f;

    [Tooltip("Dash enter fade")]
    [SerializeField] private float _dashFadeDuration = 0.08f;

    [Tooltip("Fade after dash into idle/walk; longer than normal to ease the hard stop")]
    [SerializeField] private float _dashToLocomotionFadeDuration = 0.28f;

    [Header("Attack (body layer, synced with BatonAttackDriver segment index)")]
    [Tooltip("Fallback clip when bodyComboSequence slot is empty")]
    [SerializeField] private ClipTransition _attackDefault;

    [Tooltip("Body attack crossfade")]
    [SerializeField] private float _attackFadeDuration = 0.1f;

    [Header("Mode Switch Gesture (optional, empty = instant)")]
    [Tooltip("Near-to-far body gesture")]
    [SerializeField] private ClipTransition _modeSwitchNearToFarClip;
    [Tooltip("Far-to-near body gesture")]
    [SerializeField] private ClipTransition _modeSwitchFarToNearClip;

    [Header("Locomotion Speed")]
    [FormerlySerializedAs("_runSpeedMinMultiplier")]
    [SerializeField] private float _locomotionSpeedMinMultiplier = 0.35f;
    [FormerlySerializedAs("_runSpeedMaxMultiplier")]
    [SerializeField] private float _locomotionSpeedMaxMultiplier = 1f;

    [Header("Movement")]
    [SerializeField] private bool _disableAnimatorRootMotion = true;

    [Header("Debug")]
    [Tooltip("Log once per missing clip to help track bad FBX references")]
    [SerializeField] private bool _logSkippedPlaybackOnce = true;

    private float _locomotionSpeed01 = 1f;

    private bool _loggedIdleSkip;
    private bool _loggedWalkSkip;
    private bool _loggedSprintSkip;
    private bool _loggedSprintFallbackWalk;
    private bool _loggedDashSkip;
    private bool _loggedAttackSkip;
    private bool _loggedNoAnimancer;
    private bool _loggedSelfDisabled;

    public AnimancerComponent Animancer => _animancer;

    // stacking: each buff instance adds an entry, getter multiplies them all
    private readonly System.Collections.Generic.List<float> _attackSpeedMultipliers = new();

    public float AttackSpeedMultiplier
    {
        get
        {
            float r = 1f;
            foreach (var m in _attackSpeedMultipliers) r *= m;
            return r;
        }
    }

    public void AddAttackSpeedMultiplier(float multiplier) => _attackSpeedMultipliers.Add(multiplier);
    public void RemoveAttackSpeedMultiplier(float multiplier) => _attackSpeedMultipliers.Remove(multiplier);

    private void Awake()
    {
        EnsureRootMotionDisabled();
    }

    public void PlayIdle()
    {
        if (!CanPlay())
            return;

        if (_idle == null || _idle.Clip == null)
        {
            LogSkipOnce(
                ref _loggedIdleSkip,
                "CharacterAnimancerController: Idle ClipTransition not set or Clip is null (expand the Transition and drag in the sub-AnimationClip, not the parent FBX)");
            return;
        }

        EnsureRootMotionDisabled();
        float fade = ResolveLocomotionFadeAfterDash();
        AnimancerState st = _animancer.Play(_idle, fade);
        st.Speed = 1f;
    }

    public void PlayMove()
    {
        if (!CanPlay())
            return;

        if (_walk == null || _walk.Clip == null)
        {
            LogSkipOnce(
                ref _loggedWalkSkip,
                "CharacterAnimancerController: Walk clip is null, drag a sub-AnimationClip into the Walk slot");
            return;
        }

        EnsureRootMotionDisabled();
        float fade = ResolveFadeForPlayMove();
        AnimancerState st = _animancer.Play(_walk, fade);
        ApplyLocomotionSpeedToState(st);
    }

    /// falls back to walk clip if sprint is not configured
    public void PlaySprint()
    {
        if (!CanPlay())
            return;

        ClipTransition use = _sprint != null && _sprint.Clip != null ? _sprint : _walk;
        if (use == null || use.Clip == null)
        {
            LogSkipOnce(
                ref _loggedSprintSkip,
                "CharacterAnimancerController: both Sprint and Walk clips are null");
            return;
        }

        if (use == _walk && (_sprint == null || _sprint.Clip == null))
        {
            LogSkipOnce(
                ref _loggedSprintFallbackWalk,
                "CharacterAnimancerController: Sprint clip not set, using Walk as fallback (speed still differs via Motor)");
        }

        EnsureRootMotionDisabled();
        AnimancerState st = _animancer.Play(use, _locomotionFadeDuration);
        ApplyLocomotionSpeedToState(st);
    }

    public void PlayDash()
    {
        if (!CanPlay())
            return;

        if (_dash == null || _dash.Clip == null)
        {
            LogSkipOnce(
                ref _loggedDashSkip,
                "CharacterAnimancerController: Dash clip not set, falling back to Idle");
            PlayIdle();
            return;
        }

        EnsureRootMotionDisabled();
        AnimancerState st = _animancer.Play(_dash, _dashFadeDuration);
        st.Speed = 1f;
    }

    /// plays bodyComboSequence[segmentIndex], falls back to _attackDefault
    public AnimancerState PlayBodyAttack(AttackData attackData, int segmentIndex)
    {
        if (!CanPlay())
            return null;

        ClipTransition clip = null;
        if (attackData != null && attackData.bodyComboSequence != null &&
            attackData.bodyComboSequence.Length > 0 && segmentIndex >= 0)
            clip = attackData.bodyComboSequence[segmentIndex % attackData.bodyComboSequence.Length];

        if (clip == null || clip.Clip == null)
        {
            LogSkipOnce(
                ref _loggedAttackSkip,
                "CharacterAnimancerController: bodyComboSequence slot empty for this segment, skipping");
            return null;
        }

        EnsureRootMotionDisabled();
        AnimancerState st = _animancer.Play(clip, _attackFadeDuration);
        st.Speed = Mathf.Max(0.01f, AttackSpeedMultiplier);
        return st;
    }

    /// returns null if no gesture clip is assigned (ModeSwitchState treats null as instant)
    public AnimancerState PlayModeSwitchGesture(bool toFarMode)
    {
        if (!CanPlay())
            return null;

        ClipTransition clip = toFarMode ? _modeSwitchNearToFarClip : _modeSwitchFarToNearClip;
        if (clip == null || clip.Clip == null)
            return null;

        EnsureRootMotionDisabled();
        AnimancerState st = _animancer.Play(clip, _attackFadeDuration);
        st.Speed = 1f;
        return st;
    }

    public void PlayAttack(ClipTransition clip, float fadeDuration = float.NaN)
    {
        if (!CanPlay() || clip == null || clip.Clip == null)
            return;

        EnsureRootMotionDisabled();
        float fade = float.IsNaN(fadeDuration) ? _attackFadeDuration : fadeDuration;
        AnimancerState st = _animancer.Play(clip, fade);
        st.Speed = 1f;
    }

    public void SetLocomotionSpeed(float speed01)
    {
        _locomotionSpeed01 = Mathf.Clamp01(speed01);
        if (_animancer == null || !_animancer.IsGraphInitialized)
            return;

        AnimancerLayer layer = _animancer.Layers[0];
        AnimancerState st = layer.CurrentState;
        if (st == null || !IsCurrentLocomotionClipState(st))
            return;

        ApplyLocomotionSpeedToState(st);
    }

    private bool IsLayer0PlayingSprintClip()
    {
        if (_animancer == null || !_animancer.IsGraphInitialized || _sprint == null || _sprint.Clip == null)
            return false;

        AnimancerState st = _animancer.Layers[0].CurrentState;
        return st is ClipState cs && cs.Clip == _sprint.Clip;
    }

    private bool IsLayer0PlayingDashClip()
    {
        if (_animancer == null || !_animancer.IsGraphInitialized || _dash == null || _dash.Clip == null)
            return false;

        AnimancerState st = _animancer.Layers[0].CurrentState;
        return st is ClipState cs && cs.Clip == _dash.Clip;
    }

    private float ResolveLocomotionFadeAfterDash()
    {
        return IsLayer0PlayingDashClip() ? _dashToLocomotionFadeDuration : _locomotionFadeDuration;
    }

    private float ResolveFadeForPlayMove()
    {
        if (IsLayer0PlayingDashClip())
            return _dashToLocomotionFadeDuration;
        if (IsLayer0PlayingSprintClip())
            return _sprintToWalkFadeDuration;
        return _locomotionFadeDuration;
    }

    private bool IsCurrentLocomotionClipState(AnimancerState st)
    {
        if (st is not ClipState cs)
            return false;
        if (_walk != null && cs.Clip == _walk.Clip)
            return true;
        if (_sprint != null && _sprint.Clip != null && cs.Clip == _sprint.Clip)
            return true;
        return false;
    }

    private void ApplyLocomotionSpeedToState(AnimancerState st)
    {
        float k = Mathf.Lerp(_locomotionSpeedMinMultiplier, _locomotionSpeedMaxMultiplier, _locomotionSpeed01);
        st.Speed = Mathf.Max(0.01f, k);
    }

    private void EnsureRootMotionDisabled()
    {
        if (!_disableAnimatorRootMotion || _animancer == null || _animancer.Animator == null)
            return;

        _animancer.Animator.applyRootMotion = false;
    }

    private bool CanPlay()
    {
        if (_animancer == null)
        {
            LogSkipOnce(ref _loggedNoAnimancer, "CharacterAnimancerController: AnimancerComponent not assigned");
            return false;
        }

        if (!isActiveAndEnabled)
        {
            LogSkipOnce(
                ref _loggedSelfDisabled,
                "CharacterAnimancerController: component or GameObject disabled, skipping playback");
            return false;
        }

        return true;
    }

    private void LogSkipOnce(ref bool flag, string message)
    {
        if (!_logSkippedPlaybackOnce || flag)
            return;

        flag = true;
        Debug.LogWarning(message, this);
    }
}
