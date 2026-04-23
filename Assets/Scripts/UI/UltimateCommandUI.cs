using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Ultimate section picker: 3 orbs + screen-center crosshair raycast.
/// Aim at orbs with camera center ray, click to toggle. 2 selected starts countdown; 3 fires immediately.
/// </summary>
public class UltimateCommandUI : MonoBehaviour
{
    [Header("3D Orbs")]
    [SerializeField] private SectionOrb[] _orbs;
    [SerializeField] private Transform _orbsParent;
    [Tooltip("Spawn anchor (Player or CameraTarget)")]
    [SerializeField] private Transform _spawnAnchor;
    [Tooltip("Offset from anchor, rotated by camera yaw")]
    [SerializeField] private Vector3 _spawnOffset = new(0f, 1.2f, 1.5f);

    [Header("Raycast")]
    [SerializeField] private LayerMask _orbLayer;
    [SerializeField] private float _maxRayDistance = 50f;

    [Header("Spawn Animation")]
    [SerializeField] private float _spawnStagger = 0.22f;

    [Header("Countdown")]
    [SerializeField] private float _confirmDelay = 1.5f;
    [Tooltip("Screen-space countdown ring (optional)")]
    [SerializeField] private Image _countdownRing;

    public event Action<OrchestraSection> OnSectionToggled;

    private SectionFlag _selectedFlags;
    private bool[] _orbSelected;
    private SectionOrb _currentHovered;
    private bool _isActive;

    private void Awake()
    {
        _orbSelected = new bool[_orbs != null ? _orbs.Length : 0];
    }

    public void Show()
    {
        if (_orbsParent == null) return;

        PositionOrbs();
        _orbsParent.gameObject.SetActive(true);
        _isActive = true;

        for (int i = 0; i < _orbs.Length; i++)
            _orbs[i].PlaySpawn(i * _spawnStagger);
    }

    public void Hide()
    {
        _isActive = false;
        _currentHovered = null;

        if (_orbsParent != null)
            _orbsParent.gameObject.SetActive(false);

        HideCountdown();
    }

    /// <summary>
    /// Async wait for player to pick sections.
    /// </summary>
    /// <param name="ct">Cancellation token for timeout/exit.</param>
    /// <param name="maxSelections">Max selectable sections. Full = instant fire; at maxSelections-1 starts countdown (only when max>=3).</param>
    /// <returns>Combined SectionFlag (None = timeout/cancelled).</returns>
    public async UniTask<SectionFlag> WaitForSelectionAsync(CancellationToken ct, int maxSelections = 3)
    {
        ResetSelection();

        var resultSource = new UniTaskCompletionSource<SectionFlag>();
        CancellationTokenSource countdownCts = null;

        try
        {
            using (ct.Register(() => resultSource.TrySetResult(SectionFlag.None)))
            {
                while (!ct.IsCancellationRequested)
                {
                    UpdateHover();

                    if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        SectionOrb hitOrb = RaycastOrb();
                        if (hitOrb != null)
                        {
                            int index = FindOrbIndex(hitOrb);
                            if (index >= 0)
                            {
                                ToggleOrb(index);

                                int count = CountSelected();

                                countdownCts?.Cancel();
                                countdownCts?.Dispose();
                                countdownCts = null;
                                HideCountdown();

                                if (count >= maxSelections)
                                {
                                    return _selectedFlags;
                                }
                                else if (maxSelections >= 3 && count == maxSelections - 1)
                                {
                                    countdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                    RunCountdown(countdownCts.Token, resultSource).Forget();
                                }
                            }
                        }
                    }

                    if (resultSource.Task.Status.IsCompleted())
                        return await resultSource.Task;

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
        }
        finally
        {
            countdownCts?.Cancel();
            countdownCts?.Dispose();
            ClearHover();
            HideCountdown();
        }

        return SectionFlag.None;
    }

    private void PositionOrbs()
    {
        if (_orbsParent == null || _spawnAnchor == null) return;

        Camera cam = Camera.main;
        Vector3 forward = cam != null ? cam.transform.forward : _spawnAnchor.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            forward = _spawnAnchor.forward;
        forward.Normalize();

        Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
        _orbsParent.position = _spawnAnchor.position + yaw * _spawnOffset;
        _orbsParent.rotation = yaw;
    }

    private void UpdateHover()
    {
        SectionOrb hitOrb = RaycastOrb();

        if (hitOrb != _currentHovered)
        {
            if (_currentHovered != null && !_currentHovered.IsSelected)
                _currentHovered.SetHovered(false);

            _currentHovered = hitOrb;

            if (_currentHovered != null && !_currentHovered.IsSelected)
                _currentHovered.SetHovered(true);
        }
    }

    private void ClearHover()
    {
        if (_currentHovered != null)
        {
            if (!_currentHovered.IsSelected)
                _currentHovered.SetHovered(false);
            _currentHovered = null;
        }
    }

    private SectionOrb RaycastOrb()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Vector2 screenCenter = new(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        Ray ray = cam.ScreenPointToRay(screenCenter);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _orbLayer))
            return hit.collider.GetComponentInParent<SectionOrb>();

        return null;
    }

    private void ToggleOrb(int index)
    {
        var orb = _orbs[index];
        bool wasSelected = _orbSelected[index];

        if (wasSelected)
        {
            _orbSelected[index] = false;
            _selectedFlags &= ~orb.Section.flag;
            orb.SetSelected(false);
            Debug.Log($"[Ult] deselected [{orb.Section.sectionName}], combo: {_selectedFlags} ({CountSelected()}/{_orbs.Length})");
        }
        else
        {
            _orbSelected[index] = true;
            _selectedFlags |= orb.Section.flag;
            orb.SetSelected(true);
            OnSectionToggled?.Invoke(orb.Section);
            Debug.Log($"[Ult] selected [{orb.Section.sectionName}], combo: {_selectedFlags} ({CountSelected()}/{_orbs.Length})");
        }
    }

    private int FindOrbIndex(SectionOrb orb)
    {
        for (int i = 0; i < _orbs.Length; i++)
        {
            if (_orbs[i] == orb) return i;
        }
        return -1;
    }

    private void ResetSelection()
    {
        _selectedFlags = SectionFlag.None;
        if (_orbSelected == null)
            _orbSelected = new bool[_orbs != null ? _orbs.Length : 0];

        for (int i = 0; i < _orbSelected.Length; i++)
        {
            _orbSelected[i] = false;
            if (i < _orbs.Length)
                _orbs[i].ResetVisual();
        }
        _currentHovered = null;
        HideCountdown();
    }

    private int CountSelected()
    {
        int count = 0;
        for (int i = 0; i < _orbSelected.Length; i++)
        {
            if (_orbSelected[i]) count++;
        }
        return count;
    }

    private async UniTaskVoid RunCountdown(CancellationToken ct, UniTaskCompletionSource<SectionFlag> resultSource)
    {
        ShowCountdown();
        float elapsed = 0f;

        while (elapsed < _confirmDelay)
        {
            if (ct.IsCancellationRequested) return;

            elapsed += Time.unscaledDeltaTime;
            float fill = Mathf.Clamp01(1f - elapsed / _confirmDelay);
            if (_countdownRing != null)
                _countdownRing.fillAmount = fill;

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        if (!ct.IsCancellationRequested)
            resultSource.TrySetResult(_selectedFlags);
    }

    private void ShowCountdown()
    {
        if (_countdownRing != null)
        {
            _countdownRing.enabled = true;
            _countdownRing.fillAmount = 1f;
        }
    }

    private void HideCountdown()
    {
        if (_countdownRing != null)
        {
            _countdownRing.enabled = false;
            _countdownRing.fillAmount = 0f;
        }
    }
}
