using UnityEngine;

public class RunState : CharacterState
{
    public override void OnEnterState()
    {
        if (Brain.CharacterAnimancer != null)
            Brain.CharacterAnimancer.PlaySprint();
    }
}
