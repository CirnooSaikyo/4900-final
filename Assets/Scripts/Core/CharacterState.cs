using Animancer.FSM;
using UnityEngine;

public abstract class CharacterState : MonoBehaviour, IState
{
    protected CharacterBrain Brain { get; private set; }

    public void Init(CharacterBrain brain) => Brain = brain;

    public virtual bool CanEnterState => true;

    public virtual bool CanExitState => true;

    public virtual void OnEnterState() { }

    public virtual void OnExitState() { }
}
