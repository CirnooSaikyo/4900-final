using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// BC "War Symphony" - strings + percussion combo.
/// IsFireAndForget=true: doesn't block the ult loop, fires every _interval seconds in background.
/// Stacking the same combo offsets each wave by _stackOffset seconds.
/// </summary>
public class WarSymphonyEffect : UltimateEffectBase
{
    [Header("Damage")]
    [SerializeField] private float _damage = 80f;
    [SerializeField] private float _range = 10f;
    [Tooltip("Cone half-angle (degrees), only affects damage; particles are 360")]
    [SerializeField] private float _halfAngle = 45f;
    [SerializeField] private LayerMask _enemyLayer;
    [SerializeField] private float _damageDecayAtEdge = 0.6f;

    [Header("Repeating trigger")]
    [Tooltip("Interval between triggers (seconds)")]
    [SerializeField] private float _interval = 0.5f;
    [Tooltip("Max duration per instance (seconds), auto-stops")]
    [SerializeField] private float _maxDuration = 12f;
    [Tooltip("Stagger offset between stacked waves (seconds)")]
    [SerializeField] private float _stackOffset = 0.1f;

    [Header("Screen shake (leave empty to skip)")]
    [SerializeField] private CinemachineImpulseSource _impulseSource;
    [SerializeField] private float _screenShakeForce = 0.5f;

    private static int _runningCount = 0;

    private Material _material;

    public override bool IsFireAndForget => true;

    public override async UniTask ExecuteAsync(Transform caster, CancellationToken ct)
    {
        int stackIndex = _runningCount;
        _runningCount++;

        _material = CreateMaterial();

        try
        {
            if (stackIndex > 0)
            {
                await UniTask.Delay(
                    (int)(stackIndex * _stackOffset * 1000),
                    ignoreTimeScale: true,
                    cancellationToken: ct);
            }

            float elapsed = 0f;
            while (elapsed < _maxDuration)
            {
                ct.ThrowIfCancellationRequested();

                FireOnce(caster);

                await UniTask.Delay(
                    (int)(_interval * 1000),
                    ignoreTimeScale: true,
                    cancellationToken: ct);

                elapsed += _interval;
            }
        }
        catch (System.OperationCanceledException)
        {
        }
        finally
        {
            _runningCount = Mathf.Max(0, _runningCount - 1);
            Destroy(gameObject);
        }
    }

    private void FireOnce(Transform caster)
    {
        Vector3 center = caster.position;
        Vector3 forward = caster.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        else forward.Normalize();

        // damage in forward cone (particles are 360 visual-only)
        Collider[] hits = Physics.OverlapSphere(center, _range, _enemyLayer);
        foreach (var col in hits)
        {
            Vector3 dir = col.transform.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) continue;
            if (Vector3.Angle(forward, dir) > _halfAngle) continue;

            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) continue;

            float dist01 = dir.magnitude / _range;
            float dmg = Mathf.Lerp(_damage, _damage * _damageDecayAtEdge, dist01);
            target.TakeDamage(new DamageInfo(dmg, col.ClosestPoint(center), dir.normalized, caster));
        }

        _impulseSource?.GenerateImpulse(_screenShakeForce);

        SpawnSingleRing(center);
    }

    private void SpawnSingleRing(Vector3 center)
    {
        var go = new GameObject("WaveRing");
        go.transform.position = center;
        go.transform.rotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (_material != null) r.material = _material;

        var main = ps.main;
        main.maxParticles = 1000;
        main.duration = 0.05f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(28f, 32f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
        main.startColor = new Color(0f, 0.55f, 1f, 1f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1000) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        // radiusThickness=0: emit from surface only -> thinner ring
        shape.radiusThickness = 0f;

        var lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.separateAxes = true;
        lv.space = ParticleSystemSimulationSpace.World;
        lv.limitX = new ParticleSystem.MinMaxCurve(999f);
        lv.limitY = new ParticleSystem.MinMaxCurve(0.01f);
        lv.limitZ = new ParticleSystem.MinMaxCurve(999f);
        lv.dampen = 1f;

        ps.Play();
    }

    private static Material CreateMaterial()
    {
        string[] candidates = {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Particles/Lit",
            "Particles/Standard Unlit",
            "Sprites/Default",
            "Unlit/Color",
        };
        foreach (var n in candidates)
        {
            var sh = Shader.Find(n);
            if (sh != null) return new Material(sh) { color = Color.white };
        }
        Debug.LogError("[BC VFX] no usable shader found");
        return null;
    }
}
