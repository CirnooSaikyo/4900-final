using Animancer.FSM;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CharacterBrain : MonoBehaviour
{
    private const float MoveDeadZoneSqr = 1e-4f;

    [SerializeField] private CharacterMotor _motor;
    [SerializeField] private StateMachine<CharacterState>.WithDefault _stateMachine;

    [Header("State refs")]
    [SerializeField] private IdleState _idleState;
    [SerializeField] private MoveState _moveState;
    [Tooltip("Hold Run (default Left Shift) + movement to sprint; leave empty for walk only")]
    [SerializeField] private RunState _runState;
    [SerializeField] private DashState _dashState;
    [SerializeField] private AttackState _attackState;
    [SerializeField] private ModeSwitchState _modeSwitchState;
    [SerializeField] private UltimateState _ultimateState;

    [Header("Body animation (Animancer)")]
    [SerializeField] private CharacterAnimancerController _characterAnimancer;

    [Header("Baton (Q toggle; AttackState reads Near/Far)")]
    [SerializeField] private BatonModeManager _batonModeManager;
    [SerializeField] private BatonAttackDriver _attackDriver;
    [SerializeField] private BatonConductDriver _conductDriver;

    [Header("Camera")]
    [Tooltip("Leave empty to skip look input; scene needs CameraTarget + Cinemachine Follow/LookAt")]
    [SerializeField] private CameraController _cameraController;

    [Header("Energy")]
    [SerializeField] private EnergySystem _energySystem;

    [Header("Stamina")]
    [SerializeField] private StaminaSystem _staminaSystem;
    [Tooltip("Stamina cost for Q mode switch")]
    [SerializeField] private float _modeSwitchStaminaCost = 15f;

    [Header("Debug (whitebox)")]
    [SerializeField] private bool _debugStateColors;
    [SerializeField] private Renderer _debugBodyRenderer;

    private PlayerInputActions _input;
    private InputAction _lookAction;
    private bool _warnedBatonManagerMissing;

    // stays true after dash-to-run until movement returns to deadzone
    private bool _postDashSprintUntilIdle;

    public CharacterMotor Motor => _motor;
    public StateMachine<CharacterState>.WithDefault StateMachine => _stateMachine;
    public EnergySystem EnergySystem => _energySystem;
    public StaminaSystem StaminaSystem => _staminaSystem;
    public CharacterAnimancerController CharacterAnimancer => _characterAnimancer;
    public BatonModeManager BatonModeManager => _batonModeManager;

    /// <summary>Ultimate effects that need HitboxTrigger (e.g. AC buff) use this</summary>
    public BatonAttackDriver AttackDriver => _attackDriver;

    /// <summary>GrandFinaleEffect drives the real baton animation through this</summary>
    public BatonConductDriver ConductDriver => _conductDriver;

    // supports stacking: each buff instance adds an entry, getter multiplies all
    private readonly System.Collections.Generic.List<float> _damageMultipliers = new();

    /// <summary>Global player damage multiplier (product of all active buff entries)</summary>
    public float PlayerDamageMultiplier
    {
        get
        {
            float r = 1f;
            foreach (var m in _damageMultipliers) r *= m;
            return r;
        }
    }

    public void AddDamageMultiplier(float multiplier) => _damageMultipliers.Add(multiplier);

    /// <summary>Removes the earliest matching multiplier (called by buff OnDestroy)</summary>
    public void RemoveDamageMultiplier(float multiplier) => _damageMultipliers.Remove(multiplier);

    private void Awake()
    {
        InitState(_idleState, this);
        InitState(_moveState, this);
        if (_runState != null)
            InitState(_runState, this);
        InitState(_dashState, this);
        InitState(_attackState, this);
        if (_modeSwitchState != null)
            InitState(_modeSwitchState, this);
        if (_ultimateState != null)
            InitState(_ultimateState, this);

        _stateMachine.DefaultState = _idleState;
        _stateMachine.InitializeAfterDeserialize();
    }

    private void Start()
    {
        // sync body anim in Start to avoid t-pose when this runs before AnimancerComponent in Awake
        SyncBodyAnimancerWithLocomotionState();
    }

    private static void InitState(CharacterState state, CharacterBrain brain) => state.Init(brain);

    private void SyncBodyAnimancerWithLocomotionState()
    {
        if (_characterAnimancer == null)
            return;

        if (_stateMachine.CurrentState == _idleState)
            _characterAnimancer.PlayIdle();
        else if (_stateMachine.CurrentState == _moveState)
            _characterAnimancer.PlayMove();
        else if (_runState != null && _stateMachine.CurrentState == _runState)
            _characterAnimancer.PlaySprint();
    }

    private void OnEnable()
    {
        _input ??= new PlayerInputActions();
        _input.Gameplay.Enable();
        _lookAction = _input.asset.FindActionMap("Gameplay").FindAction("Look");
        if (_cameraController != null && _lookAction == null)
            Debug.LogWarning(
                "[CharacterBrain] Gameplay/Look action not found. Re-import PlayerInputActions.inputactions with mouse delta",
                this);
    }

    private void OnDisable()
    {
        if (_input != null)
            _input.Gameplay.Disable();
        _lookAction = null;
    }

    private void OnDestroy()
    {
        _input?.Dispose();
        _input = null;
    }

    private void Update()
    {
        bool inUltimate = _ultimateState != null && _stateMachine.CurrentState == _ultimateState;

        if (_cameraController != null && _lookAction != null)
            _cameraController.SetLookInput(_lookAction.ReadValue<Vector2>());

        Vector2 move = _input.Gameplay.Move.ReadValue<Vector2>();

        // block dash/attack/modeswitch during ultimate
        if (!inUltimate)
        {
            if (_input.Gameplay.Dash.WasPressedThisFrame())
            {
                if (_stateMachine.CurrentState == _attackState && _dashState != null && _dashState.CanEnterState)
                    _stateMachine.ForceSetState(_dashState);
                else
                    _stateMachine.TrySetState(_dashState);
            }

            if (_input.Gameplay.Attack.WasPressedThisFrame())
                HandleAttackInput();

            if (_input.Gameplay.ModeSwitch.WasPressedThisFrame())
            {
                bool hasStaminaForSwitch = _staminaSystem == null || _staminaSystem.TryConsume(_modeSwitchStaminaCost);
                if (hasStaminaForSwitch)
                {
                    if (_modeSwitchState != null)
                        _stateMachine.TrySetState(_modeSwitchState);
                    else if (_batonModeManager != null)
                        _batonModeManager.ToggleMode();
                    else if (!_warnedBatonManagerMissing)
                    {
                        _warnedBatonManagerMissing = true;
                        Debug.LogWarning(
                            "[CharacterBrain] No ModeSwitchState or BatonModeManager assigned, Q key disabled",
                            this);
                    }
                }
            }
        }

        // R key: enter ult or request exit if already in ult
        if (_ultimateState != null && _input.Gameplay.Ult.WasPressedThisFrame())
        {
            if (inUltimate)
                _ultimateState.RequestExit();
            else
                _stateMachine.TrySetState(_ultimateState);
        }


        if (IsLocomotionState())
            ApplyLocomotionStateFromInput(move);

        bool isLocked = _stateMachine.CurrentState == _attackState || inUltimate;
        Vector2 motorMove = isLocked ? Vector2.zero : move;
        bool sprintMotor = _runState != null &&
                           _stateMachine.CurrentState == _runState &&
                           motorMove.sqrMagnitude > MoveDeadZoneSqr;
        _motor.SetMoveInput(motorMove, sprintMotor);

        if (_characterAnimancer != null &&
            (_stateMachine.CurrentState == _moveState ||
             (_runState != null && _stateMachine.CurrentState == _runState)))
        {
            float mag = move.magnitude;
            if (move.sqrMagnitude > 1.0001f)
                mag = 1f;
            _characterAnimancer.SetLocomotionSpeed(mag);
        }

        // drain stamina while sprinting; downgrade to walk when empty
        if (_staminaSystem != null && _runState != null && _stateMachine.CurrentState == _runState)
        {
            bool stillHasStamina = _staminaSystem.DrainRun(Time.deltaTime);
            if (!stillHasStamina)
                _stateMachine.TrySetState(_moveState);
        }
    }

    private void LateUpdate()
    {
        if (!_debugStateColors || _debugBodyRenderer == null)
            return;

        Color c = Color.white;
        var current = _stateMachine.CurrentState;
        if (current == _idleState)
            c = Color.white;
        else if (current == _moveState)
            c = Color.green;
        else if (_runState != null && current == _runState)
            c = Color.cyan;
        else if (current == _dashState)
            c = Color.yellow;
        else if (current == _attackState)
            c = Color.red;
        else if (_modeSwitchState != null && current == _modeSwitchState)
            c = new Color(1f, 0.5f, 0f);
        else if (_ultimateState != null && current == _ultimateState)
            c = Color.magenta;

        _debugBodyRenderer.material.color = c;
    }

    private bool IsLocomotionState()
    {
        var cs = _stateMachine.CurrentState;
        return cs == _idleState || cs == _moveState || (_runState != null && cs == _runState);
    }

    private void HandleAttackInput()
    {
        if (_attackState == null)
            return;

        if (_stateMachine.CurrentState == _attackState)
        {
            // baton finished but WaitForComboEnd hasn't exited yet - restart combo to avoid dropped input
            if (_attackDriver != null && !_attackDriver.IsExecuting)
                _stateMachine.ForceSetState(_attackState);
            else
                _attackState.HandleComboInput();
            return;
        }

        _stateMachine.TrySetState(_attackState);
    }

    private void ApplyLocomotionStateFromInput(Vector2 move)
    {
        bool holdRun = _input.Gameplay.Run.IsPressed();
        bool staminaOk = _staminaSystem == null || _staminaSystem.HasStamina;
        if (move.sqrMagnitude > MoveDeadZoneSqr)
        {
            if (_runState != null && staminaOk && (holdRun || _postDashSprintUntilIdle))
                _stateMachine.TrySetState(_runState);
            else
                _stateMachine.TrySetState(_moveState);
        }
        else
        {
            _postDashSprintUntilIdle = false;
            _stateMachine.TrySetState(_idleState);
        }
    }

    /// <summary>After dash lands, enter run if moving (with RunState), otherwise idle</summary>
    public void EnterLocomotionAfterDash()
    {
        Vector2 move = _input.Gameplay.Move.ReadValue<Vector2>();
        ApplyLocomotionAfterDash(move);

        bool sprintMotor = _runState != null &&
                           _stateMachine.CurrentState == _runState &&
                           move.sqrMagnitude > MoveDeadZoneSqr;
        _motor.SetMoveInput(move, sprintMotor);

        if (_characterAnimancer != null &&
            (_stateMachine.CurrentState == _moveState ||
             (_runState != null && _stateMachine.CurrentState == _runState)))
        {
            float mag = move.magnitude;
            if (move.sqrMagnitude > 1.0001f)
                mag = 1f;
            _characterAnimancer.SetLocomotionSpeed(mag);
        }
    }

    // after dash: go straight to run if moving (skip walk), otherwise idle
    private void ApplyLocomotionAfterDash(Vector2 move)
    {
        if (move.sqrMagnitude > MoveDeadZoneSqr)
        {
            if (_runState != null)
            {
                _postDashSprintUntilIdle = true;
                _stateMachine.TrySetState(_runState);
            }
            else
            {
                _postDashSprintUntilIdle = false;
                _stateMachine.TrySetState(_moveState);
            }
        }
        else
        {
            _postDashSprintUntilIdle = false;
            _stateMachine.TrySetState(_idleState);
        }
    }
}
