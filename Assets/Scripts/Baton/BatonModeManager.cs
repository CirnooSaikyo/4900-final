using DG.Tweening;
using UnityEngine;

/// <summary>
/// Near/Far toggle: tweens BatonFollowDriver params via DOTween,
/// changes baton color with MaterialPropertyBlock (no material instancing).
/// </summary>
[DisallowMultipleComponent]
public class BatonModeManager : MonoBehaviour
{
    [SerializeField] private BatonFollowDriver _followDriver;
    [SerializeField] private Renderer _batonRenderer;

    [Header("Mode Configs")]
    [SerializeField] private BatonModeConfig _nearConfig;
    [SerializeField] private BatonModeConfig _farConfig;

    [Header("Switch - Position")]
    [SerializeField] private float _switchDuration = 0.4f;

    [Header("Switch - Color & Emission")]
    [SerializeField] private Color _nearColor = Color.cyan;
    [SerializeField] private Color _farColor = Color.red;
    [SerializeField] private string _baseColorProperty = "_BaseColor";
    [SerializeField] private string _emissionColorProperty = "_EmissionColor";
    [Tooltip("Color fade duration (seconds)")]
    [SerializeField] private float _colorFadeDuration = 0.4f;
    [Tooltip("Emission spike multiplier at switch instant (flash pulse, decays to 1.5)")]
    [SerializeField] private float _emissionSpikeMult = 5f;

    [Header("Switch - Scale Punch")]
    [Tooltip("Scale punch strength (0 = disabled)")]
    [SerializeField] private float _scalePunchStrength = 0.18f;
    [Tooltip("Scale punch duration (seconds)")]
    [SerializeField] private float _scalePunchDuration = 0.3f;
    [Tooltip("Punch vibrato count")]
    [SerializeField] private int _scalePunchVibrato = 6;

    public bool IsNearMode { get; private set; } = true;

    public BatonModeConfig CurrentConfig => IsNearMode ? _nearConfig : _farConfig;

    private Tweener _switchTween;
    private Tweener _colorTween;
    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        if (_followDriver != null)
        {
            var cfg = CurrentConfig;
            if (cfg != null)
                _followDriver.ApplyConfig(cfg);
        }

        ApplyVisualImmediate(IsNearMode);
    }

    private void OnDisable()
    {
        _switchTween?.Kill();
        _switchTween = null;
        _colorTween?.Kill();
        _colorTween = null;
    }

    public void ToggleMode()
    {
        IsNearMode = !IsNearMode;

        _switchTween?.Kill();

        if (_followDriver != null && _nearConfig != null && _farConfig != null)
        {
            BatonModeConfig from = IsNearMode ? _farConfig : _nearConfig;
            BatonModeConfig to   = IsNearMode ? _nearConfig : _farConfig;

            _followDriver.BeginModeSwitchBlend();

            float t = 0f;
            _switchTween = DOTween.To(() => t, v =>
            {
                t = v;
                _followDriver.LerpConfig(from, to, t);
            }, 1f, _switchDuration)
                .SetEase(Ease.InOutCubic)
                .OnComplete(FinishModeSwitchFollow)
                .OnKill(FinishModeSwitchFollow)
                .SetTarget(this);
        }
        else if (_followDriver != null)
        {
            var cfg = CurrentConfig;
            if (cfg != null)
                _followDriver.ApplyConfig(cfg);
        }

        Color fromColor = IsNearMode ? _farColor  : _nearColor;
        Color toColor   = IsNearMode ? _nearColor : _farColor;
        TweenColorTransition(fromColor, toColor);

        if (_batonRenderer != null && _scalePunchStrength > 1e-4f)
            _batonRenderer.transform.DOPunchScale(
                Vector3.one * _scalePunchStrength, _scalePunchDuration,
                _scalePunchVibrato, elasticity: 0f)
                .SetTarget(_batonRenderer.transform);
    }

    private void TweenColorTransition(Color from, Color to)
    {
        _colorTween?.Kill();
        if (_batonRenderer == null)
            return;

        _mpb ??= new MaterialPropertyBlock();

        float t = 0f;
        _colorTween = DOTween.To(() => t, v =>
        {
            t = v;
            Color col = Color.Lerp(from, to, v);
            float emMult = Mathf.Lerp(_emissionSpikeMult, 1.5f, v);
            _batonRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_baseColorProperty, col);
            _mpb.SetColor(_emissionColorProperty, col * emMult);
            _batonRenderer.SetPropertyBlock(_mpb);
        }, 1f, _colorFadeDuration)
            .SetEase(Ease.OutCubic)
            .SetTarget(this);
    }

    private void FinishModeSwitchFollow()
    {
        if (_followDriver == null)
            return;

        _followDriver.EndModeSwitchBlend();
        var cfg = CurrentConfig;
        if (cfg != null)
            _followDriver.ApplyConfig(cfg);
    }

    private void ApplyVisualImmediate(bool nearMode)
    {
        if (_batonRenderer == null)
            return;

        Color color = nearMode ? _nearColor : _farColor;
        _mpb ??= new MaterialPropertyBlock();
        _batonRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_baseColorProperty, color);
        _mpb.SetColor(_emissionColorProperty, color * 1.5f);
        _batonRenderer.SetPropertyBlock(_mpb);
    }
}
