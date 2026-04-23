using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class BatonConductDriver : MonoBehaviour
{
    [SerializeField] private BatonFollowDriver _followDriver;
    [SerializeField] private BatonAttackDriver _attackDriver;
    [SerializeField] private Transform _visualRoot;

    [Header("Ultimate Height Boost")]
    [Tooltip("Extra Y offset on top of FollowDriver during ultimate")]
    [SerializeField] private float _conductHeightBoost = 2f;

    [Tooltip("Follow damping during ultimate; lower = tighter")]
    [SerializeField] private float _conductFollowSmoothTime = 0.01f;

    [Header("Conduct Idle Pose")]
    [Tooltip("Local position for conduct idle pose")]
    [SerializeField] private Vector3 _conductIdleLocalPos = new(0f, 0.15f, 0f);
    [Tooltip("Local euler for conduct idle (-X = tip up, +X = tip down)")]
    [SerializeField] private Vector3 _conductIdleLocalEuler = new(-40f, 0f, 12f);
    [Tooltip("Transition duration into conduct idle")]
    [SerializeField] private float _conductIdleEnterDuration = 0.35f;
    [SerializeField] private Ease _conductIdleEnterEase = Ease.OutCubic;

    [Header("Tap Animation")]
    [Tooltip("Spring swing data for tap; falls back to simple angle tween if empty")]
    [SerializeField] private BatonSwingData _tapSwingData;
    [SerializeField] private float _tapDownAngle = 35f;
    [SerializeField] private float _tapDuration = 0.12f;
    [SerializeField] private float _tapReturnDuration = 0.18f;

    [Header("Grand Finale Drop")]
    [Tooltip("Rise height in meters")]
    [SerializeField] private float _grandFinaleRiseHeight = 28f;
    [Tooltip("Scale multiplier during rise")]
    [SerializeField] private float _grandFinaleScaleFactor = 30f;
    [Tooltip("Rise duration")]
    [SerializeField] private float _grandFinaleRiseDuration = 0.2f;
    [Tooltip("Fall duration (InExpo ease)")]
    [SerializeField] private float _grandFinaleFallDuration = 0.2f;
    [Tooltip("Duration to restore original scale after landing")]
    [SerializeField] private float _grandFinaleRestoreDuration = 0.4f;
    [Tooltip("World euler during rise (tip up)")]
    [SerializeField] private Vector3 _grandFinaleRiseWorldEuler = new Vector3(-90f, 0f, 0f);
    [Tooltip("World euler during fall (tip down)")]
    [SerializeField] private Vector3 _grandFinaleFallWorldEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("Duration to rotate upright during rise; 0 = instant")]
    [SerializeField] private float _grandFinaleRotateDuration = 0.3f;

    private Tween _activeTween;
    private bool _isInConductMode;
    private CancellationTokenSource _tapCts;

    private Vector3 _grandFinaleOriginalScale;
    private Quaternion _grandFinaleOriginalRootRot;

    public bool IsInConductMode => _isInConductMode;

    public async UniTask EnterConductModeAsync(Transform player, CancellationToken ct)
    {
        _isInConductMode = true;

        // wait for any active attack to finish first
        if (_attackDriver != null && _attackDriver.IsExecuting)
            await UniTask.WaitUntil(() => !_attackDriver.IsExecuting, cancellationToken: ct);

        ct.ThrowIfCancellationRequested();

        if (_followDriver != null)
        {
            _followDriver.SetAdditiveHeightOffset(_conductHeightBoost);
            _followDriver.SetSmoothTimeOverride(_conductFollowSmoothTime);
            _followDriver.SetFaceCamera(true);
        }

        if (_attackDriver != null)
            _attackDriver.PauseIdleLoop();

        if (_visualRoot != null && _conductIdleEnterDuration > 0f)
        {
            KillActiveTween();
            var seq = DOTween.Sequence()
                .Append(_visualRoot.DOLocalMove(_conductIdleLocalPos, _conductIdleEnterDuration).SetEase(_conductIdleEnterEase))
                .Join(_visualRoot.DOLocalRotate(_conductIdleLocalEuler, _conductIdleEnterDuration).SetEase(_conductIdleEnterEase))
                .SetUpdate(true);
            _activeTween = seq;
            await seq.AsyncWaitForCompletion().AsUniTask();
            ct.ThrowIfCancellationRequested();
        }
    }

    public async UniTask PlayTapAsync(CancellationToken ct)
    {
        if (_visualRoot == null) return;

        CancelTapCts();
        _tapCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tapToken = _tapCts.Token;

        if (_tapSwingData != null && _attackDriver != null)
        {
            KillActiveTween();
            Vector3 returnPos = _conductIdleLocalPos;
            Quaternion returnRot = Quaternion.Euler(_conductIdleLocalEuler);
            try
            {
                await _attackDriver.ExecuteSwingForConductAsync(_tapSwingData, returnPos, returnRot, tapToken);
            }
            catch (System.OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // interrupted by next tap, expected
                return;
            }
            ct.ThrowIfCancellationRequested();
            return;
        }

        KillActiveTween();

        Quaternion baseRot = Quaternion.Euler(_conductIdleLocalEuler);
        Vector3 tapEuler = _conductIdleLocalEuler + new Vector3(_tapDownAngle, 0f, 0f);

        var seq = DOTween.Sequence()
            .Append(_visualRoot.DOLocalRotate(tapEuler, _tapDuration).SetEase(Ease.OutQuad))
            .Append(_visualRoot.DOLocalRotateQuaternion(baseRot, _tapReturnDuration).SetEase(Ease.OutBack))
            .SetUpdate(true);

        _activeTween = seq;
        await seq.AsyncWaitForCompletion().AsUniTask();
        ct.ThrowIfCancellationRequested();
    }

    public async UniTask PlayGrandFinaleAsync(Vector3 impactPos, CancellationToken ct)
    {
        if (_followDriver == null || _visualRoot == null) return;

        Transform batonRoot = _followDriver.transform;
        _grandFinaleOriginalScale   = _visualRoot.localScale;
        _grandFinaleOriginalRootRot = batonRoot.rotation;

        _followDriver.SetOverride(true);
        KillActiveTween();

        try
        {
            Vector3 risePos      = batonRoot.position + Vector3.up * _grandFinaleRiseHeight;
            Quaternion riseRot   = Quaternion.Euler(_grandFinaleRiseWorldEuler);
            float rotateDuration = Mathf.Max(_grandFinaleRotateDuration, 0.01f);

            var riseSeq = DOTween.Sequence()
                .Append(batonRoot.DOMove(risePos, _grandFinaleRiseDuration)
                    .SetEase(Ease.OutCubic))
                .Join(batonRoot.DORotateQuaternion(riseRot, rotateDuration)
                    .SetEase(Ease.OutCubic))
                .Join(_visualRoot.DOScale(
                    _grandFinaleOriginalScale * _grandFinaleScaleFactor,
                    _grandFinaleRiseDuration)
                    .SetEase(Ease.OutBack))
                .SetUpdate(UpdateType.Normal, isIndependentUpdate: true);

            _activeTween = riseSeq;
            using (ct.Register(() => riseSeq.Kill()))
                await riseSeq.AsyncWaitForCompletion().AsUniTask();

            ct.ThrowIfCancellationRequested();

            Quaternion fallRot = Quaternion.Euler(_grandFinaleFallWorldEuler);

            var fallSeq = DOTween.Sequence()
                .Append(batonRoot.DOMove(impactPos, _grandFinaleFallDuration)
                    .SetEase(Ease.InExpo))
                // rotate faster than fall for a sharper look
                .Join(batonRoot.DORotateQuaternion(fallRot, _grandFinaleFallDuration * 0.4f)
                    .SetEase(Ease.OutQuad))
                .SetUpdate(UpdateType.Normal, isIndependentUpdate: true);

            _activeTween = fallSeq;
            using (ct.Register(() => fallSeq.Kill()))
                await fallSeq.AsyncWaitForCompletion().AsUniTask();

            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            RestoreGrandFinaleImmediate();
            throw;
        }
    }

    public async UniTask ExitGrandFinaleAsync(CancellationToken ct)
    {
        if (_visualRoot == null)
        {
            _followDriver?.SetOverride(false);
            return;
        }

        KillActiveTween();

        Transform batonRoot = _followDriver != null ? _followDriver.transform : null;

        var restoreSeq = DOTween.Sequence()
            .Append(_visualRoot.DOScale(_grandFinaleOriginalScale, _grandFinaleRestoreDuration)
                .SetEase(Ease.OutCubic));

        if (batonRoot != null)
            restoreSeq.Join(batonRoot.DORotateQuaternion(_grandFinaleOriginalRootRot, _grandFinaleRestoreDuration)
                .SetEase(Ease.OutCubic));

        restoreSeq.SetUpdate(UpdateType.Normal, isIndependentUpdate: true);

        _activeTween = restoreSeq;
        using (ct.Register(() => restoreSeq.Kill()))
            await restoreSeq.AsyncWaitForCompletion().AsUniTask();

        _followDriver?.SetOverride(false);
    }

    private void RestoreGrandFinaleImmediate()
    {
        KillActiveTween();
        if (_visualRoot != null)
            _visualRoot.localScale = _grandFinaleOriginalScale;
        if (_followDriver != null)
        {
            _followDriver.transform.rotation = _grandFinaleOriginalRootRot;
            _followDriver.SetOverride(false);
        }
    }

    public void ExitConductMode()
    {
        CancelTapCts();
        KillActiveTween();
        _isInConductMode = false;

        if (_followDriver != null)
        {
            _followDriver.SetAdditiveHeightOffset(0f);
            _followDriver.SetSmoothTimeOverride(-1f);
            _followDriver.SetFaceCamera(false);
        }

        if (_attackDriver != null)
            _attackDriver.ResumeIdleLoop();
    }

    private void CancelTapCts()
    {
        if (_tapCts == null) return;
        _tapCts.Cancel();
        _tapCts.Dispose();
        _tapCts = null;
    }

    private void KillActiveTween()
    {
        if (_activeTween == null) return;
        if (_activeTween.IsActive())
            _activeTween.Kill();
        _activeTween = null;
    }

    private void OnDisable()
    {
        CancelTapCts();
        if (_isInConductMode)
            ExitConductMode();
    }
}
