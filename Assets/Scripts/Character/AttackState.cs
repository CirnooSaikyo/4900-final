using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Attack state: stays active for the entire combo (no idle flicker between hits).
/// Body-layer anim segments are swapped internally via cross-fade.
/// Dash can interrupt at any time via ForceSetState.
/// </summary>
public class AttackState : CharacterState
{
    [SerializeField] private BatonAttackDriver _attackDriver;
    [SerializeField] private BatonTargetFinder _targetFinder;

    [Header("Attack Data (must be AttackData assets)")]
    [SerializeField] private AttackData _nearAttack;
    [SerializeField] private AttackData _farAttack;

    [Header("Safety")]
    [Tooltip("Max lock duration for the entire combo (seconds), prevents infinite lock on baton error")]
    [SerializeField] private float _comboSafetyTimeout = 6f;

    private CancellationTokenSource _cts;
    private bool _comboFinished;
    private AttackData _currentAttack;
    private int _lastBodySegment = -1;

    public override bool CanEnterState =>
        _attackDriver == null || !_attackDriver.IsModeSwitching;

    public override bool CanExitState => _comboFinished;

    public override void OnEnterState()
    {
        _comboFinished = false;
        _lastBodySegment = -1;
        Brain.Motor.StopMovement();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        _currentAttack = ResolveCurrentAttackData();
        int segmentIndex = 0;
        if (_attackDriver != null && _currentAttack != null)
            segmentIndex = _attackDriver.PeekNextComboIndex(_currentAttack);

        PlayBodyForSegment(segmentIndex);

        if (_attackDriver != null && _currentAttack != null)
        {
            Camera cam = Camera.main;
            Transform target = null;
            if (_targetFinder != null)
            {
                target = _targetFinder.FindTarget(
                    Brain.transform.position,
                    _currentAttack.targetSearchRange,
                    cam);
            }

            Vector3? targetPos = target != null ? target.position : null;
            Vector3 fallback = cam != null
                ? BatonTargetFinder.GetFallbackDirection(cam)
                : Brain.transform.forward;

            _attackDriver.RequestAttack(_currentAttack, targetPos, fallback);
        }

        WaitForComboEnd(_cts.Token).Forget();
    }

    private void PlayBodyForSegment(int segmentIndex)
    {
        if (segmentIndex == _lastBodySegment)
            return;

        _lastBodySegment = segmentIndex;
        Brain.CharacterAnimancer?.PlayBodyAttack(_currentAttack, segmentIndex);
    }

    /// <summary>
    /// Combo continuation: buffers baton input and cross-fades to next body segment
    /// without returning to idle. Called by CharacterBrain on attack press during this state.
    /// </summary>
    public void HandleComboInput()
    {
        _attackDriver?.BufferNextAttack();

        if (_currentAttack != null && _attackDriver != null)
        {
            int nextIndex = _attackDriver.PeekNextComboIndex(_currentAttack);
            PlayBodyForSegment(nextIndex);
        }
    }

    private async UniTaskVoid WaitForComboEnd(CancellationToken ct)
    {
        // wait one frame so baton attack loop sets _isExecuting = true
        await UniTask.Yield(ct);

        float startTime = Time.time;
        await UniTask.WaitUntil(
            () => (_attackDriver == null || !_attackDriver.IsExecuting)
                  || Time.time - startTime > _comboSafetyTimeout,
            cancellationToken: ct);

        _comboFinished = true;
        if (Brain.StateMachine.CurrentState == this)
            Brain.StateMachine.TrySetDefaultState();
    }

    private AttackData ResolveCurrentAttackData()
    {
        BatonModeManager mode = Brain.BatonModeManager;
        return mode != null && mode.IsNearMode ? _nearAttack : _farAttack;
    }

    public override void OnExitState()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
