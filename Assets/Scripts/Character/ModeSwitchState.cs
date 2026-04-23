using UnityEngine;

/// <summary>
/// Mode switch state (Q key): toggles Near/Far and fires baton transition (fire-and-forget),
/// no body lock, immediately returns to locomotion.
/// </summary>
public class ModeSwitchState : CharacterState
{
    [SerializeField] private BatonModeManager _batonModeManager;
    [SerializeField] private BatonAttackDriver _attackDriver;

    [Header("Transition Data")]
    [SerializeField] private ModeSwitchTransitionData _transitionData;

    public override bool CanEnterState => true;

    public override bool CanExitState => true;

    public override void OnEnterState()
    {
        bool toFar = _batonModeManager != null && _batonModeManager.IsNearMode;

        _batonModeManager?.ToggleMode();
        _attackDriver?.RequestModeSwitchAttack(toFar, _transitionData);

        Brain.StateMachine.TrySetDefaultState();
    }

    public override void OnExitState() { }
}
