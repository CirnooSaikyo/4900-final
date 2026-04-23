/// <summary>pure-function damage calculator, no MonoBehaviour dependency</summary>
public static class DamageCalculator
{
    public struct DamageResult
    {
        public float FinalDamage;
        public bool IsCritical;
    }

    public static DamageResult Calculate(in DamageInfo raw, float defense = 0f)
    {
        float final = UnityEngine.Mathf.Max(0f, raw.BaseDamage - defense);
        return new DamageResult { FinalDamage = final, IsCritical = false };
    }
}
