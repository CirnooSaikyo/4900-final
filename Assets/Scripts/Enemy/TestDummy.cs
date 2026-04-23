using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Stationary target dummy: hit flash + WorldSpace HP text + floating damage numbers + cumulative stats.
/// Attach to a Capsule on Layer=Enemy(7), paired with EnemyHealth.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class TestDummy : MonoBehaviour
{
    [SerializeField] private EnemyHealth _health;
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private MeshRenderer _bodyRenderer;

    [Header("Hit flash")]
    [SerializeField] private float _flashDuration = 0.12f;
    [SerializeField] private Color _hitColor = Color.red;
    [SerializeField] private Color _deadColor = new Color(0.25f, 0f, 0f);

    [Header("Floating damage numbers")]
    [SerializeField] private bool _showFloatingNumbers = true;
    [SerializeField] private int _fontSize = 50;
    [SerializeField] private float _textDuration = 1.2f;
    [SerializeField] private float _riseSpeed = 1.5f;
    [SerializeField] private Color _damageNumberColor = Color.yellow;

    [Header("Debug")]
    [SerializeField] private bool _invincible = true;
    [SerializeField] private bool _logDamage = true;

    private Color _originalColor;
    private Color? _baseColorOverride;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorPropId = Shader.PropertyToID("_BaseColor");
    private Tween _flashTween;

    private float _totalDamageTaken;
    private int _hitCount;

    private void Awake()
    {
        if (_health == null)
            _health = GetComponent<EnemyHealth>();

        if (_bodyRenderer == null)
            _bodyRenderer = GetComponentInChildren<MeshRenderer>();

        _mpb = new MaterialPropertyBlock();
        if (_bodyRenderer != null)
        {
            _bodyRenderer.GetPropertyBlock(_mpb);
            _originalColor = _bodyRenderer.sharedMaterial != null
                ? _bodyRenderer.sharedMaterial.color
                : Color.white;
        }

        if (_healthText == null)
            _healthText = CreateHealthText();
    }

    private TextMeshProUGUI CreateHealthText()
    {
        var canvasGo = new GameObject("HealthCanvas");
        canvasGo.transform.SetParent(transform);
        canvasGo.transform.localPosition = Vector3.up * 1.4f;
        canvasGo.transform.localRotation = Quaternion.identity;
        canvasGo.transform.localScale = Vector3.one * 0.01f;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(canvasGo.transform, false);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;

        var rt = tmp.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120f, 20f);
        rt.localPosition = Vector3.zero;

        return tmp;
    }

    private void Start()
    {
        _health.OnDamaged += HandleDamaged;
        _health.OnDied += HandleDied;
        RefreshText();
    }

    private void OnDestroy()
    {
        _health.OnDamaged -= HandleDamaged;
        _health.OnDied -= HandleDied;
        _flashTween?.Kill();
    }

    private void HandleDamaged(DamageInfo info, DamageCalculator.DamageResult result)
    {
        _totalDamageTaken += result.FinalDamage;
        _hitCount++;

        if (_logDamage)
            Debug.Log(
                $"[TestDummy] {name} hit {result.FinalDamage:F1} | hits {_hitCount} | total {_totalDamageTaken:F1}",
                gameObject);

        RefreshText();
        FlashColor(_hitColor, _originalColor);

        if (_showFloatingNumbers)
            ShowFloatingNumberAsync(result.FinalDamage, info.HitPoint).Forget();
    }

    private void HandleDied()
    {
        if (_invincible)
        {
            _health.ResetHealth();
            RefreshText();
            return;
        }

        RefreshText();
        _flashTween?.Kill();
        SetBodyColor(_deadColor);
    }

    private async UniTaskVoid ShowFloatingNumberAsync(float damage, Vector3 hitPoint)
    {
        var go = new GameObject("DmgNum");

        float rx = Random.Range(-0.3f, 0.3f);
        float rz = Random.Range(-0.3f, 0.3f);
        Vector3 spawnPos = transform.position + Vector3.up * 1.8f + new Vector3(rx, 0f, rz);
        go.transform.position = spawnPos;

        var tm = go.AddComponent<TextMesh>();
        tm.text = damage.ToString("F0");
        tm.fontSize = _fontSize;
        tm.color = _damageNumberColor;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.1f;

        float elapsed = 0f;
        Color startColor = tm.color;

        while (elapsed < _textDuration)
        {
            if (go == null)
                return;

            elapsed += Time.deltaTime;
            float t = elapsed / _textDuration;

            go.transform.position = spawnPos + Vector3.up * (_riseSpeed * elapsed);

            if (Camera.main != null)
            {
                Vector3 dir = go.transform.position - Camera.main.transform.position;
                if (dir.sqrMagnitude > 1e-6f)
                    go.transform.rotation = Quaternion.LookRotation(dir);
            }

            float alpha = 1f - t * t;
            tm.color = new Color(startColor.r, startColor.g, startColor.b, alpha);

            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (go != null)
            Destroy(go);
    }

    private void RefreshText()
    {
        if (_healthText == null)
            return;
        _healthText.text = $"HP {_health.CurrentHealth:F0}/{_health.MaxHealth:F0}";
    }

    /// <summary>
    /// External mark effects call this to override the "return color" after hit flash.
    /// null = restore to original material color; non-null = restore to mark color (e.g. AB purple).
    /// </summary>
    public void SetBaseColorOverride(Color? color)
    {
        _baseColorOverride = color;
        if (_flashTween == null || !_flashTween.IsActive())
            SetBodyColor(_baseColorOverride ?? _originalColor);
    }

    private void FlashColor(Color from, Color to)
    {
        if (_bodyRenderer == null)
            return;

        Color returnColor = _baseColorOverride ?? _originalColor;

        _flashTween?.Kill();
        SetBodyColor(from);
        _flashTween = DOVirtual.DelayedCall(_flashDuration, () => SetBodyColor(returnColor))
            .SetTarget(this);
    }

    private void SetBodyColor(Color color)
    {
        if (_bodyRenderer == null)
            return;
        _mpb.SetColor(ColorPropId, color);
        _bodyRenderer.SetPropertyBlock(_mpb);
    }

    [ContextMenu("Deal 10 damage")]
    private void TestTenDamage()
    {
        var info = new DamageInfo(10f, transform.position, Vector3.forward, null);
        _health.TakeDamage(in info);
    }

    [ContextMenu("Reset stats")]
    private void ResetStats()
    {
        _hitCount = 0;
        _totalDamageTaken = 0f;
        _health.ResetHealth();
        RefreshText();
        SetBodyColor(_originalColor);
        Debug.Log($"[TestDummy] {name} stats reset", gameObject);
    }

    [ContextMenu("Print stats")]
    private void PrintStats()
    {
        Debug.Log(
            $"[TestDummy] {name} | hits {_hitCount} | total dmg {_totalDamageTaken:F1}",
            gameObject);
    }
}
