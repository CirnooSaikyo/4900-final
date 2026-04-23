using UnityEngine;

public class MoveState : CharacterState
{
    public override void OnEnterState()
    {
        if (Brain.CharacterAnimancer != null)
            Brain.CharacterAnimancer.PlayMove();
    }
}
