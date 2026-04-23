using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private RectTransform _crosshairImage;

    [Header("Appearance")]
    [SerializeField] private float _size = 8f;
    [Range(0f, 1f)]
    [SerializeField] private float _alpha = 0.6f;
    [SerializeField] private Color _baseColor = Color.white;

    private void Start()
    {
        ApplyVisual();
    }

    private void OnValidate()
    {
        _size = Mathf.Max(1f, _size);
        _alpha = Mathf.Clamp01(_alpha);
        ApplyVisual();
    }

    private void ApplyVisual()
    {
        if (_crosshairImage == null)
            return;

        _crosshairImage.anchorMin = new Vector2(0.5f, 0.5f);
        _crosshairImage.anchorMax = new Vector2(0.5f, 0.5f);
        _crosshairImage.pivot = new Vector2(0.5f, 0.5f);
        _crosshairImage.anchoredPosition = Vector2.zero;
        _crosshairImage.sizeDelta = new Vector2(_size, _size);

        var image = _crosshairImage.GetComponent<Image>();
        if (image != null)
        {
            Color c = _baseColor;
            c.a = _alpha;
            image.color = c;
        }
    }
}
