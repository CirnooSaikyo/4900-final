using UnityEngine;

public class IdleState : CharacterState
{
    public override void OnEnterState()
    {
        Brain.Motor.StopMovement();
        if (Brain.CharacterAnimancer != null)
            Brain.CharacterAnimancer.PlayIdle();
    }
}
