using UnityEngine;

/// <summary>readonly damage event payload, struct + in param for zero GC</summary>
public readonly struct DamageInfo
{
    public readonly float BaseDamage;
    public readonly Vector3 HitPoint;
    public readonly Vector3 HitDirection;
    public readonly Transform Attacker;
    public readonly AttackData SourceAttack;
    public readonly int ComboSegment;

    public DamageInfo(
        float baseDamage,
        Vector3 hitPoint,
        Vector3 hitDirection,
        Transform attacker,
        AttackData sourceAttack = null,
        int comboSegment = 0)
    {
        BaseDamage = baseDamage;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        Attacker = attacker;
        SourceAttack = sourceAttack;
        ComboSegment = comboSegment;
    }
}
