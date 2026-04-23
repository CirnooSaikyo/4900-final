using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ABC "Grand Finale" - all-sections ultimate effect.
/// Reuses the real scene baton (BatonConductDriver handles ascend + slam),
/// this script handles damage / particles / hitstop. Responsibilities are separated.
/// </summary>
public class GrandFinaleEffect : UltimateEffectBase
{
    [Header("Damage")]
    [SerializeField] private float _damage = 150f;
    [SerializeField] private float _damageRadius = 15f;
    [SerializeField] private LayerMask _enemyLayer;

    [Header("Hit feel")]
    [Tooltip("Hitstop TimeScale (> 0 to avoid freezing DOTween / Cinemachine)")]
    [SerializeField] private float _hitstopTimeScale = 0.05f;
    [Tooltip("Hitstop duration (seconds, ignoreTimeScale)")]
    [SerializeField] private float _hitstopDuration = 0.12f;

    [Header("Linger after impact (seconds)")]
    [SerializeField] private float _lingerDuration = 1.2f;

    public override async UniTask ExecuteAsync(Transform caster, CancellationToken ct)
    {
        var brain = caster.GetComponent<CharacterBrain>();
        var conductDriver = brain != null ? brain.ConductDriver : null;

        if (conductDriver == null)
        {
            Debug.LogWarning("[GrandFinale] BatonConductDriver not found, skipping baton anim");
            Destroy(gameObject);
            return;
        }

        // impact point = in front of caster (direction locked at ult entry)
        Vector3 forward = caster.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        else forward.Normalize();

        Vector3 impactPos = caster.position + forward * 5f;
        impactPos.y = caster.position.y;

        // phase 1: ascend + slam (returns on impact)
        await conductDriver.PlayGrandFinaleAsync(impactPos, ct);
        ct.ThrowIfCancellationRequested();

        // impact: damage + particles + hitstop
        DealAoeDamage(impactPos, forward, caster);
        SpawnImpactVFX(impactPos);
        await ApplyHitstop(ct);

        ct.ThrowIfCancellationRequested();

        // linger
        await UniTask.Delay(
            (int)(_lingerDuration * 1000f),
            ignoreTimeScale: true,
            cancellationToken: ct);

        ct.ThrowIfCancellationRequested();

        // phase 2: retract + release control
        await conductDriver.ExitGrandFinaleAsync(ct);

        Destroy(gameObject);
    }

    private void DealAoeDamage(Vector3 impactPos, Vector3 forward, Transform caster)
    {
        Collider[] hits = Physics.OverlapSphere(impactPos, _damageRadius, _enemyLayer);
        foreach (var col in hits)
        {
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) continue;

            Vector3 dir = col.transform.position - impactPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = forward;
            else dir.Normalize();

            target.TakeDamage(new DamageInfo(
                _damage,
                col.ClosestPoint(impactPos),
                dir,
                caster));
        }
    }

    private async UniTask ApplyHitstop(CancellationToken ct)
    {
        float prev = Time.timeScale;
        Time.timeScale = _hitstopTimeScale;
        await UniTask.Delay(
            (int)(_hitstopDuration * 1000f),
            ignoreTimeScale: true,
            cancellationToken: ct);
        Time.timeScale = prev;
    }

    private void SpawnImpactVFX(Vector3 impactPos)
    {
        SpawnRing(impactPos, 2000, 18f, 22f, 0.5f, 0.6f, new Color(0.8f, 0.9f, 1f, 1f));
        SpawnRing(impactPos, 800,  9f,  12f, 0.7f, 0.8f, new Color(1f, 0.85f, 0.3f, 1f));
    }

    private static void SpawnRing(
        Vector3 pos, int count,
        float speedMin, float speedMax,
        float lifeMin,  float lifeMax,
        Color color)
    {
        var go = new GameObject("ImpactRing");
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = BuildParticleMaterial(color);

        var main = ps.main;
        main.maxParticles        = count + 100;
        main.duration            = 0.05f;
        main.loop                = false;
        main.startLifetime       = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed          = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize           = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor          = color;
        main.gravityModifier     = 0f;
        main.simulationSpace     = ParticleSystemSimulationSpace.World;
        main.stopAction          = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

        var shape = ps.shape;
        shape.enabled            = true;
        shape.shapeType          = ParticleSystemShapeType.Sphere;
        shape.radius             = 0.05f;
        shape.radiusThickness    = 0f;

        // flatten Y so particles spread on ground plane
        var lv = ps.limitVelocityOverLifetime;
        lv.enabled               = true;
        lv.separateAxes          = true;
        lv.space                 = ParticleSystemSimulationSpace.World;
        lv.limitX                = new ParticleSystem.MinMaxCurve(999f);
        lv.limitY                = new ParticleSystem.MinMaxCurve(0.05f);
        lv.limitZ                = new ParticleSystem.MinMaxCurve(999f);
        lv.dampen                = 1f;

        ps.Play();
    }

    private static Material BuildParticleMaterial(Color color)
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
            if (sh != null) return new Material(sh) { color = color };
        }
        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }
}
