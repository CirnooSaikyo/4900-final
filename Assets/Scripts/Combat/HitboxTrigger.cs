using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reuses existing CapsuleCollider (isTrigger=true) on the baton visual.
/// BatonAttackDriver calls Activate/Deactivate during attack windows.
/// </summary>
[DisallowMultipleComponent]
public class HitboxTrigger : MonoBehaviour
{
    [SerializeField] private Collider _collider;
    [Tooltip("Layer mask for valid hit targets")]
    [SerializeField] private LayerMask _targetLayer;

    [Tooltip("CharacterBrain ref for PlayerDamageMultiplier; leave empty to skip")]
    [SerializeField] private CharacterBrain _brain;

    public event Action<DamageInfo> OnHitDealt;

    private DamageInfo _currentTemplate;
    private readonly HashSet<int> _hitInstanceIds = new HashSet<int>();

    private void Awake()
    {
        if (_collider == null)
            _collider = GetComponent<Collider>();

        if (_collider != null)
            _collider.enabled = false;
    }

    public void Activate(DamageInfo template)
    {
        _currentTemplate = template;
        _hitInstanceIds.Clear();
        if (_collider != null)
            _collider.enabled = true;
    }

    public void Deactivate()
    {
        if (_collider != null)
            _collider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsInTargetLayer(other.gameObject.layer))
            return;

        int id = other.GetInstanceID();
        if (!_hitInstanceIds.Add(id))
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || !damageable.IsAlive)
            return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 hitDir = (other.transform.position - transform.position);
        if (hitDir.sqrMagnitude < 1e-6f)
            hitDir = transform.forward;
        else
            hitDir.Normalize();

        float multiplier = _brain != null ? _brain.PlayerDamageMultiplier : 1f;
        var info = new DamageInfo(
            _currentTemplate.BaseDamage * multiplier,
            hitPoint,
            hitDir,
            _currentTemplate.Attacker,
            _currentTemplate.SourceAttack,
            _currentTemplate.ComboSegment);

        damageable.TakeDamage(in info);
        OnHitDealt?.Invoke(info);
    }

    private bool IsInTargetLayer(int layer) =>
        (_targetLayer.value & (1 << layer)) != 0;
}
