using System;
using UnityEngine;

public class StaminaSystem : MonoBehaviour
{
    [SerializeField] private float _maxStamina = 100f;

    [Tooltip("Stamina drained per second while running")]
    [SerializeField] private float _runDrainPerSecond = 20f;

    [Tooltip("Flat stamina cost per dash")]
    [SerializeField] private float _dashCost = 25f;

    [Tooltip("Seconds after last drain before regen starts")]
    [SerializeField] private float _regenDelay = 1.5f;

    [Tooltip("Stamina recovered per second")]
    [SerializeField] private float _regenPerSecond = 15f;

    public event Action<float, float> OnStaminaChanged;

    public float CurrentStamina { get; private set; }
    public float MaxStamina => _maxStamina;
    public float DashCost => _dashCost;

    public bool HasStamina => CurrentStamina > 0f;
    public bool CanDash => CurrentStamina >= _dashCost;

    private float _regenTimer;

    private void Awake()
    {
        CurrentStamina = _maxStamina;
    }

    private void Update()
    {
        if (_regenTimer > 0f)
        {
            _regenTimer -= Time.deltaTime;
            return;
        }

        if (CurrentStamina < _maxStamina)
        {
            CurrentStamina = Mathf.Min(_maxStamina, CurrentStamina + _regenPerSecond * Time.deltaTime);
            OnStaminaChanged?.Invoke(CurrentStamina, _maxStamina);
        }
    }

    /// <summary>call every frame while running; returns false when depleted</summary>
    public bool DrainRun(float deltaTime)
    {
        if (CurrentStamina <= 0f)
            return false;

        CurrentStamina = Mathf.Max(0f, CurrentStamina - _runDrainPerSecond * deltaTime);
        _regenTimer = _regenDelay;
        OnStaminaChanged?.Invoke(CurrentStamina, _maxStamina);
        return CurrentStamina > 0f;
    }

    public bool TryConsumeDash()
    {
        if (CurrentStamina < _dashCost)
            return false;

        CurrentStamina = Mathf.Max(0f, CurrentStamina - _dashCost);
        _regenTimer = _regenDelay;
        OnStaminaChanged?.Invoke(CurrentStamina, _maxStamina);
        return true;
    }

    public bool TryConsume(float amount)
    {
        if (CurrentStamina < amount)
            return false;

        CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
        _regenTimer = _regenDelay;
        OnStaminaChanged?.Invoke(CurrentStamina, _maxStamina);
        return true;
    }
}
