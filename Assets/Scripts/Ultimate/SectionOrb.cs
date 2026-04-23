using DG.Tweening;
using UnityEngine;

/// <summary>
/// Orb visual state: normal / hovered / selected.
/// Uses MaterialPropertyBlock to avoid material instancing.
/// </summary>
public class SectionOrb : MonoBehaviour
{
    [SerializeField] private OrchestraSection _section;
    [Tooltip("Leave empty to auto-collect child renderers")]
    [SerializeField] private Renderer[] _renderers;
    [Tooltip("Starfield material (shared); leave empty to keep original")]
    [SerializeField] private Material _orbMaterial;

    [Header("Visuals")]
    [SerializeField] private Color _normalColor = new(1f, 0.85f, 0.1f, 0.22f);
    [SerializeField] private Color _hoverColor  = new(1f, 0.95f, 0.3f, 0.55f);
    [SerializeField] private float _selectedAlpha = 0.85f;
    [SerializeField] private float _selectedScale = 1.3f;

    [Header("Spawn Animation")]
    [Tooltip("Units below target position to start from")]
    [SerializeField] private float _spawnDropOffset  = 0.6f;
    [Tooltip("Total animation duration in seconds")]
    [SerializeField] private float _spawnDuration    = 0.9f;
    [Tooltip("Initial scale ratio (0 = from nothing)")]
    [SerializeField] private float _spawnStartScale  = 0.05f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MaterialPropertyBlock _mpb;
    private Vector3 _originalScale;
    private Vector3 _originalLocalPos;
    private bool _isSelected;
    private bool _isHovered;
    private Sequence _spawnSeq;

    public OrchestraSection Section => _section;
    public bool IsSelected => _isSelected;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _originalScale    = transform.localScale;
        _originalLocalPos = transform.localPosition;

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        if (_orbMaterial != null)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = _orbMaterial;
                r.sharedMaterials = mats;
            }
        }

        HideImmediate();
    }

    private void OnEnable()
    {
        if (_mpb == null) return;
        HideImmediate();
    }

    private void HideImmediate()
    {
        _spawnSeq?.Kill();
        _isSelected = false;
        _isHovered  = false;
        transform.localScale    = _originalScale * _spawnStartScale;
        transform.localPosition = _originalLocalPos;
        SetRenderersEnabled(false);
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
            if (r != null) r.enabled = enabled;
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        ApplyVisual();
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        ApplyVisual();
    }

    public void ResetVisual()
    {
        _isSelected = false;
        _isHovered = false;
        transform.localScale    = _originalScale;
        transform.localPosition = _originalLocalPos;
        ApplyColor(_normalColor);
    }

    /// <summary>spawn-in: scale up + rise from below + fade in</summary>
    public void PlaySpawn(float delay = 0f)
    {
        float startY  = _originalLocalPos.y - _spawnDropOffset;
        float targetY = _originalLocalPos.y;

        _spawnSeq = DOTween.Sequence();

        if (delay > 0f)
            _spawnSeq.AppendInterval(delay);

        _spawnSeq.AppendCallback(() =>
        {
            SetRenderersEnabled(true);
            ApplyColor(new Color(_normalColor.r, _normalColor.g, _normalColor.b, 0f));
        });

        _spawnSeq.Append(
            transform.DOLocalMoveY(targetY, _spawnDuration)
                     .From(startY)
                     .SetEase(Ease.OutCubic)
        );

        _spawnSeq.Join(
            transform.DOScale(_originalScale, _spawnDuration)
                     .From(_originalScale * _spawnStartScale)
                     .SetEase(Ease.OutQuad)
        );

        float fadeTarget = _normalColor.a;
        _spawnSeq.Join(
            DOTween.To(
                () => 0f,
                a  => ApplyColor(new Color(_normalColor.r, _normalColor.g, _normalColor.b, a)),
                fadeTarget,
                _spawnDuration * 0.6f
            ).SetEase(Ease.OutQuad)
        );
    }

    private void ApplyVisual()
    {
        if (_isSelected)
        {
            Color c = _section != null ? _section.themeColor : Color.white;
            c.a = _selectedAlpha;
            ApplyColor(c);
            transform.localScale = _originalScale * _selectedScale;
        }
        else if (_isHovered)
        {
            ApplyColor(_hoverColor);
            transform.localScale = _originalScale;
        }
        else
        {
            ApplyColor(_normalColor);
            transform.localScale = _originalScale;
        }
    }

    private void ApplyColor(Color color)
    {
        if (_renderers == null) return;
        _mpb.SetColor(BaseColorId, color);
        foreach (var r in _renderers)
        {
            if (r != null) r.SetPropertyBlock(_mpb);
        }
    }
}
