using System.Threading;
using Animancer;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public class BatonAttackDriver : MonoBehaviour
{
    [SerializeField] private BatonFollowDriver _followDriver;
    [SerializeField] private AnimancerComponent _visualAnimancer;
    [SerializeField] private Transform _visualRoot;
    [SerializeField] private HitboxTrigger _hitboxTrigger;

    [Header("Idle Orbit")]
    [SerializeField] private float _idleCycleDuration = 4.2f;
    [SerializeField] private float _idleEnterDuration = 0.18f;
    [SerializeField] private float _idleRadiusX = 0.22f;
    [SerializeField] private float _idleRadiusZ = 0.28f;
    [SerializeField] private float _idleForwardOffset = 0.38f;
    [SerializeField] private float _idleBaseHeight = 0.12f;
    [SerializeField] private float _idleHeightAmplitude = 0.10f;
    [SerializeField] private float _idlePitchBase = 14f;
    [SerializeField] private float _idlePitchAmplitude = 5f;
    [SerializeField] private float _idleYawAmplitude = 8f;
    [SerializeField] private float _idleRollAmplitude = 6f;

    [SerializeField] private float _clipPoseBridgeDuration = 0.10f;
    [SerializeField] private Ease _clipPoseBridgeEase = Ease.InOutSine;

    private int _comboIndex;
    private float _lastAttackEndTime = -999f;
    private bool _isExecuting;
    private Sequence _activeSeq;
    private Sequence _idleSeq;
    private float _idleOrbitAngle;
    private CancellationTokenSource _internalCts;
    private bool _hasBufferedInput;
    private bool _hasBufferedRequest;
    private AttackRequest _bufferedRequest;

    private CancellationTokenSource _modeSwitchCts;

    private Vector3 _springPos;
    private Vector3 _springVel;
    private Vector3 _springRotVel;
    private Quaternion _springRot;

    private float _impactShakeTimer;
    private float _impactShakeInitial;
    private float _hitstopTimer;

    // shared with PollHitboxSwingAsync to avoid 1-frame drift
    private float _swingNormalizedTime;

    public bool IsExecuting => _isExecuting;

    /// <summary>exposed for ultimate effects to tweak damage multiplier</summary>
    public HitboxTrigger HitboxTrigger => _hitboxTrigger;

    /// <summary>used by VFX for glow effects</summary>
    public Transform VisualRoot => _visualRoot;

    public void PauseIdleLoop() => StopIdleLoop();

    public void ResumeIdleLoop() => StartIdleLoopIfNeeded();

    private struct AttackRequest
    {
        public AttackData AttackData;
        public Vector3? TargetPosition;
        public Vector3 FallbackDirection;

        public AttackRequest(AttackData attackData, Vector3? targetPosition, Vector3 fallbackDirection)
        {
            AttackData = attackData;
            TargetPosition = targetPosition;
            FallbackDirection = fallbackDirection;
        }
    }

    private void Start()
    {
        StartIdleLoopIfNeeded();
    }

    /// <summary>queues an attack; buffers it if one is already running</summary>
    public void RequestAttack(AttackData attackData, Vector3? targetPosition, Vector3 fallbackDirection)
    {
        if (attackData == null)
            return;

        if (_isExecuting)
        {
            _bufferedRequest = new AttackRequest(attackData, targetPosition, fallbackDirection);
            _hasBufferedRequest = true;
            return;
        }

        StartNewAttack(attackData, targetPosition, fallbackDirection);
    }

    public void BufferNextAttack()
    {
        if (_isExecuting)
            _hasBufferedInput = true;
    }

    /// <summary>peek next combo index without advancing, for body anim sync</summary>
    public int PeekNextComboIndex(AttackData attackData)
    {
        if (attackData == null) return 0;

        int seqLen = 0;
        if (attackData.swingSequence != null && attackData.swingSequence.Length > 0)
            seqLen = attackData.swingSequence.Length;
        else if (attackData.comboSequence != null && attackData.comboSequence.Length > 0)
            seqLen = attackData.comboSequence.Length;
        else
            return 0;

        // don't reset mid-execution just because the timer expired
        if (_isExecuting)
            return _comboIndex < seqLen ? _comboIndex : 0;

        float comboWindow = Mathf.Max(0f, attackData.comboWindowDuration);
        if (Time.time - _lastAttackEndTime > comboWindow || _comboIndex >= seqLen)
            return 0;

        return _comboIndex;
    }

    private void StartNewAttack(AttackData attackData, Vector3? targetPos, Vector3 fallbackDirection)
    {
        CancelAndDisposeInternalCts();
        _internalCts = new CancellationTokenSource();
        RunBatonAttackLoop(attackData, targetPos, fallbackDirection, _internalCts.Token).Forget();
    }

    private async UniTaskVoid RunBatonAttackLoop(
        AttackData attackData,
        Vector3? targetPos,
        Vector3 fallbackDirection,
        CancellationToken ct)
    {
        _isExecuting = true;
        if (_followDriver != null)
            _followDriver.SetOverride(true);

        try
        {
            bool isFirstSegment = true;
            while (true)
            {
                int segmentIndex;
                if (TryPickSwing(attackData, out BatonSwingData swing, out segmentIndex))
                {
                    Debug.Log($"[BatonAttack] {attackData.attackName} seg {segmentIndex} (Swing) start | dmg={attackData.GetSegmentDamage(segmentIndex)}");
                    RotateToward(targetPos, fallbackDirection);
                    StopIdleLoop();

                    if (isFirstSegment && targetPos.HasValue && attackData.batonFlyToTarget)
                        await FlyToTargetAsync(targetPos.Value, attackData, ct);
                    isFirstSegment = false;

                    await ExecuteSwingAsync(swing, attackData, segmentIndex, ct);
                }
                else if (TryPickClip(attackData, out ClipTransition clip, out segmentIndex))
                {
                    Debug.Log($"[BatonAttack] {attackData.attackName} seg {segmentIndex} (Clip) start | dmg={attackData.GetSegmentDamage(segmentIndex)}");
                    RotateToward(targetPos, fallbackDirection);
                    StopIdleLoop();

                    if (isFirstSegment && targetPos.HasValue && attackData.batonFlyToTarget)
                        await FlyToTargetAsync(targetPos.Value, attackData, ct);
                    isFirstSegment = false;

                    await ExecuteClipAsync(clip, attackData, segmentIndex, ct);
                }
                else
                {
                    break;
                }

                // combo window resets per segment
                _lastAttackEndTime = Time.time;

                if (_hasBufferedInput)
                {
                    _hasBufferedInput = false;
                    continue;
                }

                if (_hasBufferedRequest)
                {
                    AttackRequest request = _bufferedRequest;
                    _hasBufferedRequest = false;
                    _hasBufferedInput = false;
                    attackData = request.AttackData;
                    targetPos = request.TargetPosition;
                    fallbackDirection = request.FallbackDirection;
                    isFirstSegment = true;
                    continue;
                }

                break;
            }
        }
        finally
        {
            _hitboxTrigger?.Deactivate();
            _isExecuting = false;
            _lastAttackEndTime = Time.time;
            _hasBufferedInput = false;
            _hasBufferedRequest = false;
            SafeKillActiveSequence();
            _visualAnimancer?.Stop();

            if (_followDriver != null)
                _followDriver.SetOverride(false);

            StartIdleLoopIfNeeded();
        }
    }

    public void ResetCombo()
    {
        _comboIndex = 0;
        _lastAttackEndTime = -999f;
    }

    private bool TryPickSwing(AttackData attackData, out BatonSwingData swing, out int segmentIndexUsed)
    {
        swing = null;
        segmentIndexUsed = -1;
        if (attackData == null || attackData.swingSequence == null || attackData.swingSequence.Length == 0)
            return false;

        float comboWindow = Mathf.Max(0f, attackData.comboWindowDuration);
        if (Time.time - _lastAttackEndTime > comboWindow ||
            _comboIndex >= attackData.swingSequence.Length)
            _comboIndex = 0;

        segmentIndexUsed = _comboIndex;
        swing = attackData.swingSequence[segmentIndexUsed];
        _comboIndex++;

        if (swing == null || swing.keyframes == null || swing.keyframes.Length < 2)
        {
            Debug.LogWarning(
                $"BatonAttackDriver: swingSequence[{segmentIndexUsed}] on '{attackData.attackName}' is null or has < 2 keyframes",
                attackData);
            return false;
        }
        return true;
    }

    private bool TryPickClip(AttackData attackData, out ClipTransition clip, out int segmentIndexUsed)
    {
        clip = null;
        segmentIndexUsed = -1;
        if (_visualAnimancer == null || attackData == null ||
            attackData.comboSequence == null || attackData.comboSequence.Length == 0)
            return false;

        float comboWindow = Mathf.Max(0f, attackData.comboWindowDuration);
        if (Time.time - _lastAttackEndTime > comboWindow ||
            _comboIndex >= attackData.comboSequence.Length)
            _comboIndex = 0;

        segmentIndexUsed = _comboIndex;
        clip = attackData.comboSequence[segmentIndexUsed];
        _comboIndex++;
        if (clip == null || clip.Clip == null)
        {
            Debug.LogWarning(
                $"BatonAttackDriver: comboSequence[{segmentIndexUsed}] on '{attackData.attackName}' is null",
                attackData);
            return false;
        }

        return true;
    }

    private async UniTask ExecuteSwingAsync(
        BatonSwingData swing, AttackData attackData, int segmentIndex, CancellationToken ct)
    {
        Transform visualRoot = GetVisualRoot();
        if (visualRoot == null || swing == null) return;

        SafeKillActiveSequence();
        _visualAnimancer?.Stop();

        float duration = Mathf.Max(0.05f, swing.totalDuration);

        _springPos = visualRoot.localPosition;
        _springVel = Vector3.zero;
        _springRot = visualRoot.localRotation;
        _springRotVel = Vector3.zero;
        _impactShakeTimer = 0f;
        _impactShakeInitial = 0f;
        _hitstopTimer = 0f;
        _swingNormalizedTime = 0f;

        if (_hitboxTrigger != null && attackData != null)
            PollHitboxSwingAsync(attackData, segmentIndex, ct).Forget();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float dt = Time.deltaTime;

            // hitstop: freeze swing progress, spring+shake keep updating
            if (_hitstopTimer > 0f)
                _hitstopTimer -= dt;
            else
                elapsed += dt;

            float prevNorm = _swingNormalizedTime;
            _swingNormalizedTime = Mathf.Clamp01(elapsed / duration);
            DetectImpactTrigger(swing.keyframes, prevNorm, _swingNormalizedTime);

            EvaluateKeyframes(swing.keyframes, _swingNormalizedTime,
                out Vector3 targetPos, out Quaternion targetRot);

            // 3 substeps when fps < 30 for stability
            int substeps = dt > 1f / 30f ? 3 : 1;
            float subDt = dt / substeps;
            for (int s = 0; s < substeps; s++)
            {
                SpringUpdate(ref _springPos, ref _springVel, targetPos,
                    swing.springStiffness, swing.springDamping, subDt);
                SpringUpdateRotation(ref _springRot, ref _springRotVel, targetRot,
                    swing.springStiffness, swing.springDamping, subDt);
            }

            Vector3 shakeOffset = Vector3.zero;
            if (_impactShakeTimer > 0f)
            {
                _impactShakeTimer -= dt;
                float shakeElapsed = _impactShakeInitial - Mathf.Max(0f, _impactShakeTimer);
                float decay = Mathf.Exp(-swing.impactShakeDecay * shakeElapsed);
                float phase = shakeElapsed * swing.impactShakeFrequency * Mathf.PI * 2f;
                shakeOffset = new Vector3(
                    Mathf.Sin(phase),
                    Mathf.Cos(phase * 1.3f),
                    0f) * swing.impactShakeAmplitude * decay;
            }

            visualRoot.localPosition = _springPos + shakeOffset;
            visualRoot.localRotation = _springRot;
        }

        _swingNormalizedTime = 1f;

        Vector3 idlePos = GetIdleOrbitPosition(0f);
        Quaternion idleRot = GetIdleOrbitRotation(0f);
        await BridgePoseAsync(visualRoot, idlePos, idleRot, _clipPoseBridgeDuration, _clipPoseBridgeEase, ct);
    }

    /// <summary>swing driven by spring for conduct mode; no hitbox, visual shake only</summary>
    public async UniTask ExecuteSwingForConductAsync(
        BatonSwingData swing,
        Vector3 returnLocalPos,
        Quaternion returnLocalRot,
        CancellationToken ct)
    {
        Transform visualRoot = GetVisualRoot();
        if (visualRoot == null || swing == null) return;

        SafeKillActiveSequence();
        _visualAnimancer?.Stop();

        float duration = Mathf.Max(0.05f, swing.totalDuration);

        _springPos        = visualRoot.localPosition;
        _springVel        = Vector3.zero;
        _springRot        = visualRoot.localRotation;
        _springRotVel     = Vector3.zero;
        _impactShakeTimer  = 0f;
        _impactShakeInitial = 0f;
        _hitstopTimer      = 0f;
        _swingNormalizedTime = 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float dt = Time.deltaTime;

            if (_hitstopTimer > 0f)
                _hitstopTimer -= dt;
            else
                elapsed += dt;

            float prevNorm = _swingNormalizedTime;
            _swingNormalizedTime = Mathf.Clamp01(elapsed / duration);
            DetectImpactTrigger(swing.keyframes, prevNorm, _swingNormalizedTime);

            EvaluateKeyframes(swing.keyframes, _swingNormalizedTime,
                out Vector3 targetPos, out Quaternion targetRot);

            int substeps = dt > 1f / 30f ? 3 : 1;
            float subDt = dt / substeps;
            for (int s = 0; s < substeps; s++)
            {
                SpringUpdate(ref _springPos, ref _springVel, targetPos,
                    swing.springStiffness, swing.springDamping, subDt);
                SpringUpdateRotation(ref _springRot, ref _springRotVel, targetRot,
                    swing.springStiffness, swing.springDamping, subDt);
            }

            Vector3 shakeOffset = Vector3.zero;
            if (_impactShakeTimer > 0f)
            {
                _impactShakeTimer -= dt;
                float shakeElapsed = _impactShakeInitial - Mathf.Max(0f, _impactShakeTimer);
                float decay = Mathf.Exp(-swing.impactShakeDecay * shakeElapsed);
                float phase = shakeElapsed * swing.impactShakeFrequency * Mathf.PI * 2f;
                shakeOffset = new Vector3(
                    Mathf.Sin(phase),
                    Mathf.Cos(phase * 1.3f),
                    0f) * swing.impactShakeAmplitude * decay;
            }

            visualRoot.localPosition = _springPos + shakeOffset;
            visualRoot.localRotation = _springRot;
        }

        _swingNormalizedTime = 1f;
        await BridgePoseAsync(visualRoot, returnLocalPos, returnLocalRot, _clipPoseBridgeDuration, _clipPoseBridgeEase, ct);

        visualRoot.localPosition = returnLocalPos;
        visualRoot.localRotation = returnLocalRot;
    }

    private static void EvaluateKeyframes(SwingKeyframe[] keys, float t,
        out Vector3 pos, out Quaternion rot)
    {
        int i = 0;
        for (; i < keys.Length - 1; i++)
            if (keys[i + 1].normalizedTime >= t) break;

        SwingKeyframe a = keys[i];
        SwingKeyframe b = keys[Mathf.Min(i + 1, keys.Length - 1)];

        float span = b.normalizedTime - a.normalizedTime;
        float localT = span > 1e-6f ? (t - a.normalizedTime) / span : 1f;

        float posT = (a.positionEase != null && a.positionEase.length > 0)
            ? a.positionEase.Evaluate(localT) : localT;
        float rotT = (a.rotationEase != null && a.rotationEase.length > 0)
            ? a.rotationEase.Evaluate(localT) : localT;

        pos = Vector3.Lerp(a.localPosition, b.localPosition, posT);
        rot = Quaternion.Slerp(
            Quaternion.Euler(a.localEulerAngles),
            Quaternion.Euler(b.localEulerAngles),
            rotT);
    }

    private static void SpringUpdate(ref Vector3 current, ref Vector3 vel,
        Vector3 target, float stiffness, float damping, float dt)
    {
        Vector3 force = (target - current) * stiffness - vel * damping;
        vel += force * dt;
        current += vel * dt;
    }

    // guard against NaN when angular velocity ~0
    private static void SpringUpdateRotation(ref Quaternion current, ref Vector3 angVel,
        Quaternion target, float stiffness, float damping, float dt)
    {
        Quaternion delta = target * Quaternion.Inverse(current);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (axis.sqrMagnitude < 1e-6f) { axis = Vector3.up; angle = 0f; }

        Vector3 torque = axis * angle * stiffness - angVel * damping;
        angVel += torque * dt;

        float mag = angVel.magnitude;
        if (mag > 1e-6f)
            current = Quaternion.AngleAxis(mag * dt, angVel / mag) * current;
        current.Normalize();
    }

    private void DetectImpactTrigger(SwingKeyframe[] keys, float prevNorm, float currNorm)
    {
        foreach (var kf in keys)
        {
            if (!kf.isImpactFrame) continue;
            if (currNorm >= kf.normalizedTime && prevNorm < kf.normalizedTime)
            {
                if (kf.hitstopDuration > 0f)
                    _hitstopTimer = kf.hitstopDuration;
                if (kf.shakeDuration > 0f)
                {
                    _impactShakeTimer = kf.shakeDuration;
                    _impactShakeInitial = kf.shakeDuration;
                }
            }
        }
    }

    private async UniTaskVoid PollHitboxSwingAsync(
        AttackData attackData, int segmentIndex, CancellationToken ct)
    {
        bool activated = false;
        try
        {
            while (!ct.IsCancellationRequested && _swingNormalizedTime < 1f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float t = _swingNormalizedTime;

                if (!activated && t >= attackData.hitboxActiveStart)
                {
                    float dmg = attackData.GetSegmentDamage(segmentIndex);
                    Debug.Log($"[BatonHitbox] {attackData.attackName} seg {segmentIndex} hitbox ON | dmg={dmg} t={t:F2}");
                    var template = new DamageInfo(
                        dmg, Vector3.zero, Vector3.zero,
                        transform, attackData, segmentIndex);
                    _hitboxTrigger.Activate(template);
                    activated = true;
                }

                if (activated && t >= attackData.hitboxActiveEnd)
                    break;
            }
        }
        finally
        {
            if (activated)
                Debug.Log($"[BatonHitbox] {attackData.attackName} seg {segmentIndex} hitbox OFF");
            _hitboxTrigger?.Deactivate();
        }
    }

    private void RotateToward(Vector3? targetPosition, Vector3 fallbackDirection)
    {
        Vector3 dir = fallbackDirection;
        dir.y = 0f;

        if (targetPosition.HasValue)
        {
            Vector3 toTarget = targetPosition.Value - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 1e-6f)
                dir = toTarget.normalized;
        }

        if (dir.sqrMagnitude < 1e-6f)
            dir = Vector3.forward;

        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private void StartIdleLoopIfNeeded()
    {
        Transform visualRoot = GetVisualRoot();
        if (visualRoot == null || _isExecuting || !isActiveAndEnabled)
            return;

        StopIdleLoop();

        float enterDuration = Mathf.Max(0.01f, _idleEnterDuration);
        float cycleDuration = Mathf.Max(0.2f, _idleCycleDuration);
        _idleOrbitAngle = 0f;
        Vector3 startPos = GetIdleOrbitPosition(0f);
        Quaternion startRot = GetIdleOrbitRotation(0f);

        Tween orbitTween = DOTween.To(
                () => _idleOrbitAngle,
                angle =>
                {
                    _idleOrbitAngle = angle;
                    ApplyIdleOrbitPose(visualRoot, angle);
                },
                _idleOrbitAngle + Mathf.PI * 2f,
                cycleDuration)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Incremental);

        _idleSeq = DOTween.Sequence()
            .Append(visualRoot.DOLocalMove(startPos, enterDuration).SetEase(Ease.InOutSine))
            .Join(visualRoot.DOLocalRotateQuaternion(startRot, enterDuration).SetEase(Ease.InOutSine))
            .Append(orbitTween)
            .SetTarget(this)
            .SetUpdate(UpdateType.Late);
    }

    private async UniTask ExecuteClipAsync(
        ClipTransition clip, AttackData attackData, int segmentIndex, CancellationToken ct)
    {
        Transform visualRoot = GetVisualRoot();
        if (_visualAnimancer == null || visualRoot == null || clip == null || clip.Clip == null)
            return;

        // bridge to local zero before clip to avoid snapping between segments
        _visualAnimancer.Stop();
        await BridgePoseAsync(visualRoot, Vector3.zero, Quaternion.identity, _clipPoseBridgeDuration, _clipPoseBridgeEase, ct);

        AnimancerState state = _visualAnimancer.Play(clip);

        if (_hitboxTrigger != null && attackData != null)
            PollHitboxWindowAsync(state, attackData, segmentIndex, ct).Forget();

        var completion = new UniTaskCompletionSource();
        state.Events(this).OnEnd = () => completion.TrySetResult();
        using (ct.Register(() => completion.TrySetCanceled()))
        {
            await completion.Task;
            ct.ThrowIfCancellationRequested();
        }

        _visualAnimancer.Stop();
        Vector3 idlePos = GetIdleOrbitPosition(0f);
        Quaternion idleRot = GetIdleOrbitRotation(0f);
        await BridgePoseAsync(visualRoot, idlePos, idleRot, _clipPoseBridgeDuration, _clipPoseBridgeEase, ct);
    }

    private async UniTask FlyToTargetAsync(Vector3 targetWorldPos, AttackData data, CancellationToken ct)
    {
        float duration = Mathf.Max(0.05f, data.batonFlyDuration);
        float stopOffset = Mathf.Max(0f, data.batonStopOffset);

        Vector3 dir = targetWorldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f)
            return;
        dir.Normalize();

        Vector3 dest = targetWorldPos - dir * stopOffset;
        dest.y = transform.position.y;

        if (Vector3.Distance(transform.position, dest) < 0.05f)
            return;

        SafeKillActiveSequence();
        var tcs = new UniTaskCompletionSource();
        _activeSeq = DOTween.Sequence()
            .Append(transform.DOMove(dest, duration).SetEase(data.batonFlyEase))
            .OnComplete(() => tcs.TrySetResult())
            .OnKill(() => tcs.TrySetResult())
            .SetTarget(transform);

        using (ct.Register(SafeKillActiveSequence))
        {
            _activeSeq.Play();
            await tcs.Task;
            ct.ThrowIfCancellationRequested();
        }
    }

    private async UniTaskVoid PollHitboxWindowAsync(
        AnimancerState state, AttackData attackData, int segmentIndex, CancellationToken ct)
    {
        bool activated = false;
        try
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            // clip too short, skip damage
            if (state == null || state.NormalizedTime >= attackData.hitboxActiveEnd)
                return;

            while (!ct.IsCancellationRequested && state != null && state.IsPlaying)
            {
                float t = state.NormalizedTime;

                if (!activated && t >= attackData.hitboxActiveStart)
                {
                    float dmg = attackData.GetSegmentDamage(segmentIndex);
                    Debug.Log($"[BatonHitbox] {attackData.attackName} seg {segmentIndex} hitbox ON | dmg={dmg} t={t:F2}");
                    var template = new DamageInfo(
                        dmg,
                        Vector3.zero,
                        Vector3.zero,
                        transform,
                        attackData,
                        segmentIndex);
                    _hitboxTrigger.Activate(template);
                    activated = true;
                }

                if (activated && t >= attackData.hitboxActiveEnd)
                    break;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        finally
        {
            if (activated)
                Debug.Log($"[BatonHitbox] {attackData.attackName} seg {segmentIndex} hitbox OFF");
            _hitboxTrigger?.Deactivate();
        }
    }

    private async UniTask BridgePoseAsync(
        Transform visualRoot,
        Vector3 targetLocalPos,
        Quaternion targetLocalRot,
        float duration,
        Ease ease,
        CancellationToken ct)
    {
        float bridgeDuration = Mathf.Max(0.01f, duration);
        float posGap = Vector3.Distance(visualRoot.localPosition, targetLocalPos);
        float rotGap = Quaternion.Angle(visualRoot.localRotation, targetLocalRot);
        if (posGap <= 0.0005f && rotGap <= 0.5f)
            return;

        SafeKillActiveSequence();
        _activeSeq = DOTween.Sequence()
            .Append(visualRoot.DOLocalMove(targetLocalPos, bridgeDuration).SetEase(ease))
            .Join(visualRoot.DOLocalRotateQuaternion(targetLocalRot, bridgeDuration).SetEase(ease))
            .SetTarget(visualRoot);

        var completion = new UniTaskCompletionSource();
        _activeSeq.OnComplete(() => completion.TrySetResult());
        _activeSeq.OnKill(() => completion.TrySetResult());
        using (ct.Register(() => SafeKillActiveSequence()))
        {
            _activeSeq.Play();
            await completion.Task;
            ct.ThrowIfCancellationRequested();
        }
    }

    private Transform GetVisualRoot()
    {
        if (_visualRoot != null)
            return _visualRoot;
        return null;
    }

    private void OnDisable()
    {
        CancelAndDisposeInternalCts();
        CancelAndDisposeModeSwitchCts();
        SafeKillActiveSequence();
        StopIdleLoop();
        _visualAnimancer?.Stop();
        if (_followDriver != null)
            _followDriver.SetOverride(false);
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            StartIdleLoopIfNeeded();
    }

    /// <summary>mode switch transition (fire-and-forget, separate CTS from regular attacks)</summary>
    public bool IsModeSwitching => _modeSwitchCts != null;

    public void RequestModeSwitchAttack(bool toFarMode, ModeSwitchTransitionData config)
    {
        if (config == null)
            return;

        CancelAndDisposeModeSwitchCts();
        _modeSwitchCts = new CancellationTokenSource();
        RunModeSwitchTransitionAsync(toFarMode, config, _modeSwitchCts.Token).Forget();
    }

    private async UniTaskVoid RunModeSwitchTransitionAsync(
        bool toFarMode, ModeSwitchTransitionData config, CancellationToken ct)
    {
        Transform visualRoot = GetVisualRoot();
        if (visualRoot == null)
            return;

        // stop Animancer so PlayableGraph won't fight our rotation writes
        _visualAnimancer?.Stop();
        StopIdleLoop();

        try
        {
            if (toFarMode)
                await RunNearToFarAnimAsync(visualRoot, config, ct);
            else
                await RunFarToNearAnimAsync(visualRoot, config, ct);
        }
        finally
        {
            StartIdleLoopIfNeeded();
            CancelAndDisposeModeSwitchCts();
        }
    }

    private async UniTask RunNearToFarAnimAsync(
        Transform visualRoot, ModeSwitchTransitionData config, CancellationToken ct)
    {
        float tiltDuration = Mathf.Max(0.05f, config.nearToFarTiltDuration);
        float spinDuration = Mathf.Max(0.05f, config.nearToFarFlyDuration);
        float totalSpin    = config.nearToFarSpinCount * 360f;

        // smooth localPos back to zero during tilt to avoid frozen-in-place look
        Vector3    startLocalPos = visualRoot.localPosition;
        Quaternion startWorldRot = visualRoot.rotation;
        Quaternion tiltTarget    = LocalToWorldRot(visualRoot, config.nearToFarHorizontalRotation);

        var hitSet          = new System.Collections.Generic.HashSet<IDamageable>();
        float checkInterval = Mathf.Max(0.02f, config.farToNearCheckInterval);
        float timeSinceCheck = 0f;

        float elapsed = 0f;
        while (elapsed < tiltDuration)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            elapsed        += Time.deltaTime;
            timeSinceCheck += Time.deltaTime;
            float t         = InOutSine(Mathf.Clamp01(elapsed / tiltDuration));
            visualRoot.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, t);
            visualRoot.rotation      = Quaternion.Slerp(startWorldRot, tiltTarget, t);

            if (timeSinceCheck >= checkInterval)
            {
                timeSinceCheck = 0f;
                ApplyNearToFarDamageFiltered(visualRoot.position, config, hitSet);
            }
        }
        visualRoot.localPosition = Vector3.zero;
        visualRoot.rotation      = tiltTarget;

        elapsed = 0f;
        while (elapsed < spinDuration)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            elapsed        += Time.deltaTime;
            timeSinceCheck += Time.deltaTime;
            float t         = InOutSine(Mathf.Clamp01(elapsed / spinDuration));
            visualRoot.rotation =
                Quaternion.AngleAxis(totalSpin * t, Vector3.up) * tiltTarget;

            if (timeSinceCheck >= checkInterval)
            {
                timeSinceCheck = 0f;
                ApplyNearToFarDamageFiltered(visualRoot.position, config, hitSet);
            }
        }
    }

    private async UniTask RunFarToNearAnimAsync(
        Transform visualRoot, ModeSwitchTransitionData config, CancellationToken ct)
    {
        float duration      = Mathf.Max(0.05f, config.farToNearRushDuration);
        float checkInterval = Mathf.Max(0.02f, config.farToNearCheckInterval);

        Vector3    startLocalPos  = visualRoot.localPosition;
        Quaternion startLocalRot  = visualRoot.localRotation;
        Quaternion targetLocalRot = Quaternion.Euler(config.farToNearReturnRotation);

        var hitSet         = new System.Collections.Generic.HashSet<IDamageable>();
        float elapsed      = 0f;
        float timeSinceCheck = 0f;

        while (elapsed < duration)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            elapsed        += Time.deltaTime;
            timeSinceCheck += Time.deltaTime;
            float t         = InOutSine(Mathf.Clamp01(elapsed / duration));
            visualRoot.localPosition = Vector3.Lerp(startLocalPos, Vector3.zero, t);
            visualRoot.localRotation = Quaternion.Slerp(startLocalRot, targetLocalRot, t);

            if (timeSinceCheck >= checkInterval)
            {
                timeSinceCheck = 0f;
                ApplyFarToNearDamageFiltered(visualRoot.position, config, hitSet);
            }
        }
    }

    private void ApplyNearToFarDamageFiltered(
        Vector3 center, ModeSwitchTransitionData config,
        System.Collections.Generic.HashSet<IDamageable> hitSet)
    {
        if (config.enemyLayer.value == 0)
        {
            Debug.LogWarning(
                "[BatonAttack] enemyLayer not set on ModeSwitchTransitionData, near-to-far deals no damage", this);
            return;
        }

        Collider[] hits = Physics.OverlapSphere(center, config.nearToFarDamageRadius, config.enemyLayer);
        foreach (Collider hit in hits)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive || !hitSet.Add(damageable))
                continue;

            Vector3 dir = hit.transform.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            else                           dir.Normalize();

            damageable.TakeDamage(new DamageInfo(
                config.nearToFarDamage, hit.ClosestPoint(center), dir, transform));
            if (hit.attachedRigidbody != null)
                hit.attachedRigidbody.AddForce(dir * config.nearToFarKnockbackForce, ForceMode.Impulse);
        }
    }

    private void ApplyFarToNearDamageFiltered(
        Vector3 center, ModeSwitchTransitionData config,
        System.Collections.Generic.HashSet<IDamageable> hitSet)
    {
        if (config.enemyLayer.value == 0)
            return;

        Collider[] hits = Physics.OverlapSphere(center, config.farToNearDamageRadius, config.enemyLayer);
        foreach (Collider hit in hits)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive || !hitSet.Add(damageable))
                continue;

            Vector3 dir = hit.transform.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            else                           dir.Normalize();

            damageable.TakeDamage(new DamageInfo(
                config.farToNearDamage, hit.ClosestPoint(center), dir, transform));
            if (hit.attachedRigidbody != null)
                hit.attachedRigidbody.AddForce(dir * config.farToNearKnockbackForce, ForceMode.Impulse);
        }
    }

    private static Quaternion LocalToWorldRot(Transform visualRoot, Vector3 localEuler) =>
        LocalToWorldRot(visualRoot, Quaternion.Euler(localEuler));

    private static Quaternion LocalToWorldRot(Transform visualRoot, Quaternion localRot) =>
        visualRoot.parent != null ? visualRoot.parent.rotation * localRot : localRot;

    private static float OutQuad(float t)    => 1f - (1f - t) * (1f - t);
    private static float InOutSine(float t)  => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

    private void CancelAndDisposeModeSwitchCts()
    {
        if (_modeSwitchCts == null)
            return;
        _modeSwitchCts.Cancel();
        _modeSwitchCts.Dispose();
        _modeSwitchCts = null;
    }

    private void OnDestroy()
    {
        CancelAndDisposeInternalCts();
        CancelAndDisposeModeSwitchCts();
    }

    private void CancelAndDisposeInternalCts()
    {
        if (_internalCts == null)
            return;

        _internalCts.Cancel();
        _internalCts.Dispose();
        _internalCts = null;
    }

    private void SafeKillActiveSequence()
    {
        if (_activeSeq == null)
            return;

        try
        {
            if (_activeSeq.IsActive())
                _activeSeq.Kill();
        }
        catch (System.IndexOutOfRangeException)
        {
            // DOTween sometimes throws during domain reload, safe to ignore
        }
        finally
        {
            _activeSeq = null;
        }
    }

    private void StopIdleLoop()
    {
        if (_idleSeq == null)
            return;

        if (_idleSeq.IsActive())
            _idleSeq.Kill();
        _idleSeq = null;
    }

    private void ApplyIdleOrbitPose(Transform visualRoot, float angle)
    {
        float wrappedAngle = Mathf.Repeat(angle, Mathf.PI * 2f);
        visualRoot.localPosition = GetIdleOrbitPosition(wrappedAngle);
        visualRoot.localRotation = GetIdleOrbitRotation(wrappedAngle);
    }

    private Vector3 GetIdleOrbitPosition(float angle)
    {
        float x = Mathf.Cos(angle) * _idleRadiusX;
        float z = _idleForwardOffset + Mathf.Sin(angle) * _idleRadiusZ;
        float y = _idleBaseHeight + Mathf.Sin(angle * 2f) * _idleHeightAmplitude;
        return new Vector3(x, y, z);
    }

    private Quaternion GetIdleOrbitRotation(float angle)
    {
        float pitch = _idlePitchBase + Mathf.Sin(angle * 2f) * _idlePitchAmplitude;
        float yaw = Mathf.Sin(angle) * _idleYawAmplitude;
        float roll = Mathf.Cos(angle) * _idleRollAmplitude;
        return Quaternion.Euler(pitch, yaw, roll);
    }
}
