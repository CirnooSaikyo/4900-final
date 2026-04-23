using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arc-shaped segmented stamina bar using custom Graphic mesh
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StaminaArcUI : Graphic
{
    [Header("Stamina")]
    [SerializeField] private StaminaSystem _staminaSystem;

    [Header("Arc Shape")]
    [Tooltip("Arc radius in UI pixels")]
    [SerializeField] private float _radius = 260f;

    [Tooltip("Line thickness in UI pixels")]
    [SerializeField] private float _thickness = 14f;

    [Tooltip("Start angle (degrees). 0=right, 90=up, 180=left, 270=down")]
    [SerializeField] private float _startAngleDeg = 115f;

    [Tooltip("Total sweep angle (degrees, positive = CCW)")]
    [SerializeField] private float _sweepDeg = 130f;

    [Tooltip("Number of segments")]
    [SerializeField] private int _segmentCount = 5;

    [Tooltip("Gap between segments (degrees)")]
    [SerializeField] private float _gapDeg = 5f;

    [Tooltip("Subdivisions per arc segment (12~24 recommended)")]
    [SerializeField] private int _smoothness = 16;

    [Header("Colors")]
    [SerializeField] private Color _filledColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _emptyColor  = new Color(0.2f, 0.2f, 0.2f, 0.55f);

    private float _fillRatio = 1f;

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        if (_staminaSystem != null)
        {
            _staminaSystem.OnStaminaChanged += HandleStaminaChanged;
            // sync current value when re-enabled after disable
            if (_staminaSystem.MaxStamina > 0f)
                HandleStaminaChanged(_staminaSystem.CurrentStamina, _staminaSystem.MaxStamina);
        }
    }

    protected override void Start()
    {
        base.Start();
        // first frame sync after all Awake calls, avoids reading 0 if StaminaSystem.Awake hasn't run yet
        if (_staminaSystem != null)
            HandleStaminaChanged(_staminaSystem.CurrentStamina, _staminaSystem.MaxStamina);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_staminaSystem != null)
            _staminaSystem.OnStaminaChanged -= HandleStaminaChanged;
    }

    private void HandleStaminaChanged(float current, float max)
    {
        _fillRatio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_segmentCount <= 0) return;

        float totalGap  = _gapDeg * _segmentCount;
        float segSweep  = (_sweepDeg - totalGap) / _segmentCount;
        if (segSweep <= 0f) return;

        // top segment stays full, bottom drains first; supports smooth fractional fill
        float filledSegments = _fillRatio * _segmentCount;
        Color filledC = _filledColor * this.color;
        Color emptyC  = _emptyColor  * this.color;

        for (int i = 0; i < _segmentCount; i++)
        {
            float segStartDeg = _startAngleDeg + i * (segSweep + _gapDeg);
            float segEndDeg   = segStartDeg + segSweep;

            float segFill   = Mathf.Clamp01(filledSegments - i);
            float cutAngle  = Mathf.Lerp(segStartDeg, segEndDeg, segFill);

            if (segFill > 0.001f)
                DrawArcSegment(vh, segStartDeg, cutAngle, filledC);

            if (segFill < 0.999f)
                DrawArcSegment(vh, cutAngle, segEndDeg, emptyC);
        }
    }

    private void DrawArcSegment(VertexHelper vh, float startDeg, float endDeg, Color c)
    {
        int steps     = Mathf.Max(2, _smoothness);
        int baseIndex = vh.currentVertCount;
        float innerR  = _radius - _thickness * 0.5f;
        float outerR  = _radius + _thickness * 0.5f;

        for (int s = 0; s <= steps; s++)
        {
            float t      = (float)s / steps;
            float rad    = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
            float cosA   = Mathf.Cos(rad);
            float sinA   = Mathf.Sin(rad);

            UIVertex inner = new UIVertex();
            inner.position = new Vector3(cosA * innerR, sinA * innerR, 0f);
            inner.color    = c;
            vh.AddVert(inner);

            UIVertex outer = new UIVertex();
            outer.position = new Vector3(cosA * outerR, sinA * outerR, 0f);
            outer.color    = c;
            vh.AddVert(outer);
        }

        for (int s = 0; s < steps; s++)
        {
            int i0 = baseIndex + s * 2;
            int i1 = i0 + 1;
            int i2 = i0 + 3;
            int i3 = i0 + 2;
            vh.AddTriangle(i0, i1, i2);
            vh.AddTriangle(i0, i2, i3);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
