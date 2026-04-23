using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Ultimate state: orchestrates the full "conductor" sequence.
/// Enter -> lock movement -> push camera -> baton ready -> section select (crosshair) ->
/// confirm combo -> finale swing -> effect -> cleanup exit.
/// Cannot be interrupted by dash.
/// </summary>
public class UltimateState : CharacterState
{
    [SerializeField] private UltimateCommandUI _commandUI;
    [SerializeField] private BatonConductDriver _conductDriver;
    [SerializeField] private ConductorUltimateConfig _config;
    [SerializeField] private EnergySystem _energySystem;

    [Header("Cinemachine")]
    [Tooltip("UltimateCam object in scene (with CinemachineCamera)")]
    [SerializeField] private CinemachineCamera _ultimateCamera;
    [SerializeField] private int _activePriority = 20;
    [SerializeField] private int _inactivePriority = 0;

    [Header("Timeout")]
    [Tooltip("Overall timeout after entering ult (seconds), auto-exits")]
    [SerializeField] private float _timeoutSeconds = 10f;

    private CancellationTokenSource _cts;
    private CancellationTokenSource _exitCts;
    private bool _running;

    /// <summary>Needs at least 50 energy (minimum threshold for any combo)</summary>
    public override bool CanEnterState => !_running && (_energySystem == null || _energySystem.HasEnergy(50f));
    public override bool CanExitState => !_running;

    /// <summary>Called by CharacterBrain when R is pressed during ult to cancel selection and exit</summary>
    public void RequestExit() => _exitCts?.Cancel();

    public override void OnEnterState()
    {
        _running = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        RunUltimate(_cts.Token).Forget();
    }

    public override void OnExitState()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Cleanup();
    }

    private async UniTaskVoid RunUltimate(CancellationToken ct)
    {
        _exitCts = new CancellationTokenSource();

        try
        {
            Brain.Motor.StopMovement();

            if (_ultimateCamera != null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                    _ultimateCamera.ForceCameraPosition(mainCam.transform.position, mainCam.transform.rotation);

                _ultimateCamera.Priority = _activePriority;
            }

            // move baton to conduct position (once for the whole ult)
            if (_conductDriver != null)
                await _conductDriver.EnterConductModeAsync(Brain.transform, ct);

            if (_commandUI != null)
            {
                _commandUI.OnSectionToggled += OnSectionCued;
                _commandUI.Show();
            }

            // loop: select -> execute -> check energy -> reset, until energy depleted or player presses R
            while (!_exitCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (_energySystem != null && !_energySystem.HasEnergy(50f))
                {
                    Debug.Log("[Ult] energy depleted, auto-exit");
                    break;
                }

                // energy >= 100 allows 3 sections, otherwise cap at 2
                int maxSelections = (_energySystem != null && _energySystem.CurrentEnergy < 100f) ? 2 : 3;

                SectionFlag result = SectionFlag.None;
                if (_commandUI != null)
                {
                    using var timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter(System.TimeSpan.FromSeconds(_timeoutSeconds));
                    using var roundCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitCts.Token, timeoutCts.Token);

                    try
                    {
                        result = await _commandUI.WaitForSelectionAsync(roundCts.Token, maxSelections);
                    }
                    catch (System.OperationCanceledException)
                    {
                        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested && !_exitCts.IsCancellationRequested)
                            Debug.Log("[Ult] timed out, auto-exit");
                        break;
                    }
                }

                if (result == SectionFlag.None)
                    break;

                if (_config != null)
                {
                    var entry = _config.Resolve(result);
                    if (entry != null)
                    {
                        if (_energySystem != null && !_energySystem.TryConsume(entry.energyCost))
                        {
                            Debug.LogWarning($"[Ult] not enough energy: need {entry.energyCost}, have {_energySystem.CurrentEnergy}");
                            break;
                        }

                        Debug.Log($"[Ult] {entry.effectName} (combo:{result}, cost:{entry.energyCost})");

                        if (entry.effectPrefab != null)
                        {
                            var go = Instantiate(entry.effectPrefab, Brain.transform.position, Brain.transform.rotation);
                            var effect = go.GetComponent<UltimateEffectBase>();
                            if (effect != null)
                            {
                                if (effect.IsFireAndForget)
                                    effect.ExecuteAsync(Brain.transform, ct).Forget();
                                else
                                    await effect.ExecuteAsync(Brain.transform, ct);
                            }
                            else
                            {
                                Debug.LogWarning($"[Ult] effectPrefab [{entry.effectPrefab.name}] missing UltimateEffectBase");
                                Destroy(go);
                            }
                        }
                    }
                    else
                        Debug.LogWarning($"[Ult] no config found for combo {result}");
                }
            }
        }
        catch (System.OperationCanceledException)
        {
        }
        finally
        {
            _exitCts?.Dispose();
            _exitCts = null;
            Cleanup();
            if (Brain != null && Brain.StateMachine.CurrentState == this)
            {
                _running = false;
                Brain.StateMachine.TrySetDefaultState();
            }
            else
            {
                _running = false;
            }
        }
    }

    private void OnSectionCued(OrchestraSection section)
    {
        if (_conductDriver != null && _cts != null && !_cts.IsCancellationRequested)
            _conductDriver.PlayTapAsync(_cts.Token).Forget();

        if (section != null && section.cueSound != null)
            AudioSource.PlayClipAtPoint(section.cueSound, transform.position);
    }

    private void Cleanup()
    {
        if (_commandUI != null)
        {
            _commandUI.OnSectionToggled -= OnSectionCued;
            _commandUI.Hide();
        }

        if (_conductDriver != null && _conductDriver.IsInConductMode)
            _conductDriver.ExitConductMode();

        if (_ultimateCamera != null)
            _ultimateCamera.Priority = _inactivePriority;

    }
}
