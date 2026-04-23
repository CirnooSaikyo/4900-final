using UnityEngine;
using UnityEngine.UI;

/// <summary>segmented energy bar; each Image fills one segment worth of energy</summary>
public class EnergyBarUI : MonoBehaviour
{
    [SerializeField] private EnergySystem _energySystem;
    [Tooltip("Energy per segment (default 50 = 4 segments for 200 max)")]
    [SerializeField] private float _energyPerSegment = 50f;
    [Tooltip("Filled Images in order (Image Type = Filled, Method = Horizontal)")]
    [SerializeField] private Image[] _segmentFills;

    private void OnEnable()
    {
        if (_energySystem != null)
        {
            _energySystem.OnEnergyChanged += Refresh;
            Refresh(_energySystem.CurrentEnergy, _energySystem.MaxEnergy);
        }
    }

    private void OnDisable()
    {
        if (_energySystem != null)
            _energySystem.OnEnergyChanged -= Refresh;
    }

    private void Refresh(float current, float max)
    {
        if (_segmentFills == null)
            return;

        for (int i = 0; i < _segmentFills.Length; i++)
        {
            if (_segmentFills[i] == null)
                continue;

            float segStart = i * _energyPerSegment;
            _segmentFills[i].fillAmount = Mathf.Clamp01((current - segStart) / _energyPerSegment);
        }
    }
}
