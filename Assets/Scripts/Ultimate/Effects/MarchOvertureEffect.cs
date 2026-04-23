using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MarchOvertureEffect : UltimateEffectBase
{
    [Header("Buff")]
    [SerializeField] private float _duration = 8f;
    [SerializeField] private float _damageMultiplier = 1.5f;
    [SerializeField] private float _attackSpeedMultiplier = 1.25f;

    [Header("Glow")]
    [Tooltip("Baton emission HDR color (values >1 bloom, try 2-4)")]
    [SerializeField] private Color _batonGlowColor = new Color(3f, 2f, 0.1f, 1f);

    [Header("Orbit Particles")]
    [Tooltip("Orbit radius in meters")]
    [SerializeField] private float _orbitRadius = 1.1f;
    [Tooltip("Orbit speed in deg/s")]
    [SerializeField] private float _orbitSpeed = 200f;
    [Tooltip("Local Y offset from player feet")]
    [SerializeField] private float _orbitHeight = 0.9f;

    public override bool IsFireAndForget => true;

    private CharacterBrain _brain;
    private CharacterAnimancerController _animCtrl;

    private Renderer _batonRenderer;
    private MaterialPropertyBlock _mpb;
    private Color _savedEmission;
    private ParticleSystem _orbitPs;

    private static Material _sharedMat;

    public override UniTask ExecuteAsync(Transform caster, CancellationToken ct)
    {
        _brain    = caster.GetComponent<CharacterBrain>();
        _animCtrl = _brain != null ? _brain.CharacterAnimancer : null;

        _brain?.AddDamageMultiplier(_damageMultiplier);
        _animCtrl?.AddAttackSpeedMultiplier(_attackSpeedMultiplier);

        SpawnBurst(caster.position);

        var visualRoot = _brain?.AttackDriver?.VisualRoot;
        if (visualRoot != null)
            _batonRenderer = visualRoot.GetComponentInChildren<Renderer>();
        ApplyBatonGlow();

        _orbitPs = SpawnOrbitPs(caster);

        Debug.Log($"[MarchOverture] activated: dmg x{_damageMultiplier}, atkSpd x{_attackSpeedMultiplier}, {_duration}s");

        Destroy(gameObject, _duration);
        return UniTask.CompletedTask;
    }

    private void OnDestroy()
    {
        _brain?.RemoveDamageMultiplier(_damageMultiplier);
        _animCtrl?.RemoveAttackSpeedMultiplier(_attackSpeedMultiplier);

        RestoreBatonGlow();

        if (_orbitPs != null)
        {
            _orbitPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_orbitPs.gameObject, 3f);
        }

        Debug.Log("[MarchOverture] expired, multipliers removed");
    }

    private void SpawnBurst(Vector3 center)
    {
        var go = new GameObject("MarchBurst");
        go.transform.position = center;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.GetComponent<ParticleSystemRenderer>().material = GetMat();

        var main      = ps.main;
        main.duration        = 0.2f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 10f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor      = new Color(1f, 0.85f, 0.1f, 1f);
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.Destroy;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 280) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.5f;

        var col  = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0f), 0.7f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();
    }

    private void ApplyBatonGlow()
    {
        if (_batonRenderer == null) return;
        _mpb = new MaterialPropertyBlock();
        _batonRenderer.GetPropertyBlock(_mpb);
        _savedEmission = _mpb.GetColor("_EmissionColor");
        _mpb.SetColor("_EmissionColor", _batonGlowColor);
        _batonRenderer.SetPropertyBlock(_mpb);
    }

    private void RestoreBatonGlow()
    {
        if (_batonRenderer == null || _mpb == null) return;
        _mpb.SetColor("_EmissionColor", _savedEmission);
        _batonRenderer.SetPropertyBlock(_mpb);
    }

    private ParticleSystem SpawnOrbitPs(Transform parent)
    {
        var go = new GameObject("MarchOrbit");
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(0f, _orbitHeight, 0f);
        go.transform.localRotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.GetComponent<ParticleSystemRenderer>().material = GetMat();

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor      = new Color(1f, 0.88f, 0.15f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles    = 80;

        var emission = ps.emission;
        emission.rateOverTime = 14f;

        var shape = ps.shape;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = _orbitRadius;
        shape.radiusThickness = 0f;
        shape.rotation        = new Vector3(90f, 0f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled  = true;
        vel.space    = ParticleSystemSimulationSpace.Local;
        vel.orbitalY = new ParticleSystem.MinMaxCurve(_orbitSpeed);

        var col  = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.5f, 0.05f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f,   0f),
                new GradientAlphaKey(1f,   0.15f),
                new GradientAlphaKey(0.85f, 0.75f),
                new GradientAlphaKey(0f,   1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();
        return ps;
    }

    private static Material GetMat()
    {
        if (_sharedMat != null) return _sharedMat;
        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Particles/Lit",
            "Particles/Standard Unlit",
            "Sprites/Default",
            "Unlit/Color",
        };
        foreach (var n in candidates)
        {
            var sh = Shader.Find(n);
            if (sh != null) return _sharedMat = new Material(sh) { color = Color.white };
        }
        Debug.LogError("[MarchOverture] no usable particle shader found");
        return null;
    }
}
