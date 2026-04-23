using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Dash: cooldown prevents re-entry; can't be interrupted until Motor.IsDashing ends.
/// </summary>
public class DashState : CharacterState
{
    [SerializeField] private float _cooldown = 0.8f;

    private float _lastDashTime = -999f;
    private CancellationTokenSource _cts;

    public override bool CanEnterState =>
        Time.time - _lastDashTime >= _cooldown &&
        !Brain.Motor.IsDashing &&
        (Brain.StaminaSystem == null || Brain.StaminaSystem.CanDash);

    public override bool CanExitState => !Brain.Motor.IsDashing;

    public override void OnEnterState()
    {
        _lastDashTime = Time.time;
        Brain.StaminaSystem?.TryConsumeDash();

        if (Brain.CharacterAnimancer != null)
            Brain.CharacterAnimancer.PlayDash();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunDash(_cts.Token).Forget();
    }

    private async UniTaskVoid RunDash(CancellationToken cancellationToken)
    {
        await Brain.Motor.DashAsync(cancellationToken);
        Brain.EnterLocomotionAfterDash();
    }

    public override void OnExitState()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
