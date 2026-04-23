using System;
using UnityEngine;

public class EnergySystem : MonoBehaviour
{
    [SerializeField] private float _maxEnergy = 200f;
    [SerializeField] private float _startEnergy = 0f;
    [Tooltip("HitboxTrigger on the baton visual; auto-gains energy on hit")]
    [SerializeField] private HitboxTrigger _hitboxTrigger;

    public event Action<float, float> OnEnergyChanged;

    public float CurrentEnergy { get; private set; }
    public float MaxEnergy => _maxEnergy;

    private void Awake()
    {
        CurrentEnergy = Mathf.Clamp(_startEnergy, 0f, _maxEnergy);
    }

    private void OnEnable()
    {
        if (_hitboxTrigger != null)
            _hitboxTrigger.OnHitDealt += HandleHitDealt;
    }

    private void OnDisable()
    {
        if (_hitboxTrigger != null)
            _hitboxTrigger.OnHitDealt -= HandleHitDealt;
    }

    public bool HasEnergy(float amount) => CurrentEnergy >= amount;

    /// <summary>tries to spend energy; returns false if insufficient</summary>
    public bool TryConsume(float amount)
    {
        if (CurrentEnergy < amount)
            return false;

        CurrentEnergy = Mathf.Max(0f, CurrentEnergy - amount);
        OnEnergyChanged?.Invoke(CurrentEnergy, _maxEnergy);
        return true;
    }

    public void AddEnergy(float amount)
    {
        if (amount <= 0f)
            return;

        CurrentEnergy = Mathf.Min(_maxEnergy, CurrentEnergy + amount);
        OnEnergyChanged?.Invoke(CurrentEnergy, _maxEnergy);
    }

    private void HandleHitDealt(DamageInfo info)
    {
        float gain = info.SourceAttack != null ? info.SourceAttack.energyGainPerHit : 0f;
        if (gain > 0f)
            AddEnergy(gain);
    }
}
