using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// AB "Harmonic Resonance" - wind + strings combo.
/// On activate: pull all enemies to a gather point in front of the caster + expanding ring VFX.
/// Then sustains a damage-amp field of _radius for _duration seconds.
/// Enemies inside take all damage x _damageMultiplier (basic attacks + ult effects).
/// </summary>
public class HarmonicResonanceEffect : UltimateEffectBase
{
    [Header("Field")]
    [SerializeField] private float _radius = 8f;
    [SerializeField] private float _duration = 6f;
    [SerializeField] private float _damageMultiplier = 1.5f;
    [Tooltip("Enemy scan interval (seconds)")]
    [SerializeField] private float _scanInterval = 0.2f;
    [SerializeField] private LayerMask _enemyLayer;

    [Header("Visual - enemy mark")]
    [Tooltip("Tint blended 50/50 with enemy base color while marked")]
    [SerializeField] private Color _markTintColor = new Color(0.5f, 0.2f, 1f, 1f);

    [Header("Gather (one-shot on activate)")]
    [Tooltip("Forward offset from caster for gather point (meters)")]
    [SerializeField] private float _gatherForwardOffset = 3f;
    [Tooltip("Min distance from gather center to prevent full overlap")]
    [SerializeField] private float _stopPullRadius = 1.2f;
    [Tooltip("Pull animation duration (seconds)")]
    [SerializeField] private float _pullDuration = 0.55f;
    [Tooltip("Pull easing curve")]
    [SerializeField] private Ease _pullEase = Ease.OutExpo;

    [Header("Visual - particles (one-shot)")]
    [Tooltip("Expanding ring particle lifetime (seconds)")]
    [SerializeField] private float _ringLifetime = 1.4f;

    private readonly Collider[] _hitBuffer       = new Collider[32];
    private readonly HashSet<EnemyHealth> _markedEnemies = new();
    private readonly HashSet<EnemyHealth> _currentFrame  = new();
    private readonly Dictionary<EnemyHealth, Color> _originalColors = new();

    private static Material _sharedMat;

    public override bool IsFireAndForget => true;

    public override UniTask ExecuteAsync(Transform caster, CancellationToken ct)
    {
        transform.position = caster.position;

        SpawnRing();
        GatherEnemies(caster);

        // field runs on its own CT so it persists after ult exits
        RunFieldAsync(this.GetCancellationTokenOnDestroy()).Forget();

        Destroy(gameObject, _duration);
        return UniTask.CompletedTask;
    }

    private void GatherEnemies(Transform caster)
    {
        Vector3 gatherPt = caster.position + caster.forward * _gatherForwardOffset;
        gatherPt.y = caster.position.y;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, _radius, _hitBuffer, _enemyLayer);

        for (int i = 0; i < count; i++)
        {
            var health = _hitBuffer[i].GetComponentInParent<EnemyHealth>();
            if (health == null || !health.IsAlive) continue;
            PullToPoint(health.transform, gatherPt);
        }
    }

    /// <summary>
    /// Smoothly pulls transform to gather point (preserving Y).
    /// Skips if already within _stopPullRadius. Uses per-frame Move for CharacterController.
    /// </summary>
    private void PullToPoint(Transform t, Vector3 targetPos)
    {
        if (t == null) return;

        Vector3 cur  = t.position;
        Vector3 flat = new Vector3(targetPos.x, cur.y, targetPos.z);

        float horizDist = new Vector2(cur.x - flat.x, cur.z - flat.z).magnitude;
        if (horizDist <= _stopPullRadius) return;

        Vector3 dir  = (flat - cur).normalized;
        Vector3 dest = flat - dir * _stopPullRadius;

        var cc = t.GetComponent<CharacterController>();
        if (cc != null && cc.enabled)
        {
            PullWithCharacterControllerAsync(cc, dest, this.GetCancellationTokenOnDestroy()).Forget();
            return;
        }

        t.DOKill();
        t.DOMove(dest, _pullDuration).SetEase(_pullEase);
    }

    private async UniTaskVoid PullWithCharacterControllerAsync(
        CharacterController cc, Vector3 dest, CancellationToken ct)
    {
        float elapsed  = 0f;
        Vector3 origin = cc.transform.position;

        while (elapsed < _pullDuration && !ct.IsCancellationRequested)
        {
            elapsed += Time.deltaTime;
            float t01   = Mathf.Clamp01(elapsed / _pullDuration);
            float eased = DOVirtual.EasedValue(0f, 1f, t01, _pullEase);
            Vector3 next = Vector3.Lerp(origin, dest, eased);
            cc.Move(next - cc.transform.position);

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private async UniTaskVoid RunFieldAsync(CancellationToken ct)
    {
        float elapsed = 0f;

        while (elapsed < _duration)
        {
            try
            {
                await UniTask.Delay(
                    (int)(_scanInterval * 1000),
                    ignoreTimeScale: false,
                    cancellationToken: ct);
            }
            catch (System.OperationCanceledException) { break; }

            elapsed += _scanInterval;

            _currentFrame.Clear();
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _radius, _hitBuffer, _enemyLayer);

            for (int i = 0; i < count; i++)
            {
                var health = _hitBuffer[i].GetComponentInParent<EnemyHealth>();
                if (health == null || !health.IsAlive) continue;
                _currentFrame.Add(health);
                if (_markedEnemies.Add(health))
                    ApplyMark(health);
            }

            _markedEnemies.RemoveWhere(e =>
            {
                if (_currentFrame.Contains(e)) return false;
                RemoveMark(e);
                return true;
            });
        }

        foreach (var e in _markedEnemies) RemoveMark(e);
        _markedEnemies.Clear();
        _originalColors.Clear();
    }

    private void ApplyMark(EnemyHealth health)
    {
        health.DamageTakenMultiplier = _damageMultiplier;

        var rend = health.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Color baseColor = rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_BaseColor")
            ? rend.sharedMaterial.GetColor("_BaseColor")
            : Color.white;
        _originalColors[health] = baseColor;

        Color markColor = Color.Lerp(baseColor, _markTintColor, 0.5f);

        var mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", markColor);
        rend.SetPropertyBlock(mpb);

        // tell TestDummy to return to mark color after flash, not original
        health.GetComponent<TestDummy>()?.SetBaseColorOverride(markColor);
    }

    private void RemoveMark(EnemyHealth health)
    {
        if (health == null) return;
        health.DamageTakenMultiplier = 1f;

        health.GetComponent<TestDummy>()?.SetBaseColorOverride(null);

        var rend = health.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Color original = _originalColors.TryGetValue(health, out var c) ? c : Color.white;
        var mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", original);
        rend.SetPropertyBlock(mpb);
        _originalColors.Remove(health);
    }

    private void SpawnRing()
    {
        var go = new GameObject("ResonanceRing");
        go.transform.position = transform.position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.GetComponent<ParticleSystemRenderer>().material = GetMat();

        float speed = _radius / _ringLifetime;

        var main = ps.main;
        main.duration        = 0.1f;
        main.loop            = false;
        main.startLifetime   = _ringLifetime;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(speed * 0.9f, speed * 1.1f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.startColor      = new Color(0.6f, 0.3f, 1f, 1f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.Destroy;

        ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 400) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.15f;

        // clamp Y velocity to create a flat ring
        var lv = ps.limitVelocityOverLifetime;
        lv.enabled      = true;
        lv.separateAxes = true;
        lv.space        = ParticleSystemSimulationSpace.World;
        lv.limitX       = new ParticleSystem.MinMaxCurve(999f);
        lv.limitY       = new ParticleSystem.MinMaxCurve(0.01f);
        lv.limitZ       = new ParticleSystem.MinMaxCurve(999f);
        lv.dampen       = 1f;

        var col  = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.7f, 0.3f, 1f), 0f),
                new GradientColorKey(new Color(0.2f, 0.5f, 1f), 0.6f),
            },
            new[]
            {
                new GradientAlphaKey(1f,   0f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f,   1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();
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
        Debug.LogError("[AB VFX] no usable shader found");
        return null;
    }
}
