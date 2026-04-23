using System;
using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float _maxHealth = 100f;
    [SerializeField] private float _defense = 0f;

    private float _currentHealth;

    public bool IsAlive => _currentHealth > 0f;
    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;

    /// <summary>set to 1.5 during resonance mark, revert when mark expires</summary>
    public float DamageTakenMultiplier { get; set; } = 1f;

    public event Action<DamageInfo, DamageCalculator.DamageResult> OnDamaged;
    public event Action OnDied;

    private void Awake()
    {
        _currentHealth = _maxHealth;
    }

    public void TakeDamage(in DamageInfo info)
    {
        if (!IsAlive)
            return;

        DamageCalculator.DamageResult result = DamageCalculator.Calculate(in info, _defense);
        result.FinalDamage *= DamageTakenMultiplier;
        if (result.FinalDamage <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - result.FinalDamage);

        Debug.Log(
            $"[EnemyHealth] {name} took {result.FinalDamage:F1}, HP {_currentHealth:F1}/{_maxHealth:F1}",
            gameObject);

        OnDamaged?.Invoke(info, result);

        if (!IsAlive)
            OnDied?.Invoke();
    }

    public void ResetHealth()
    {
        _currentHealth = _maxHealth;
        DamageTakenMultiplier = 1f;
    }
}
