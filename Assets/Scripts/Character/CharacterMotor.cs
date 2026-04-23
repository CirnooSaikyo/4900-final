using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Character movement physics: no input reading, only called by Brain/states.
/// Horizontal via CharacterController, vertical via gravity.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Falls back to Camera.main if empty")]
    [SerializeField] private Camera _viewCamera;
    [SerializeField] private float _moveSpeed = 7f;
    [Tooltip("Horizontal speed = MoveSpeed * this when sprinting")]
    [SerializeField] private float _sprintSpeedMultiplier = 1.35f;
    [SerializeField] private float _rotationSpeed = 15f;

    [Header("Gravity")]
    [SerializeField] private float _gravity = -25f;

    [Header("Dash")]
    [SerializeField] private float _dashSpeed = 20f;
    [SerializeField] private float _dashDuration = 0.2f;

    private CharacterController _cc;

    private Vector2 _moveInput;

    private Vector3 _velocity;

    private Vector3 _dashDirection;

    private Vector3 _horizontalWorld;

    private const float GroundedVerticalVelocity = -2f;
    private const float InputDeadZoneSqr = 1e-6f;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public bool IsDashing { get; private set; }
    public Vector3 Velocity => _velocity;

    public bool IsSprinting { get; private set; }

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        UpdateHorizontalFromInput();

        _velocity.y += _gravity * dt;

        Vector3 horizontalVelocity = GetHorizontalVelocity();
        Vector3 displacement = horizontalVelocity * dt + Vector3.up * (_velocity.y * dt);
        _cc.Move(displacement);

        if (_cc.isGrounded && _velocity.y < 0f)
            _velocity.y = GroundedVerticalVelocity;

        _velocity = new Vector3(horizontalVelocity.x, _velocity.y, horizontalVelocity.z);

        UpdateRotation(dt);
    }

    public void SetMoveInput(Vector2 input, bool sprinting = false)
    {
        _moveInput = input;
        IsSprinting = sprinting && !IsDashing;
    }

    public void StopMovement() => _moveInput = Vector2.zero;

    public async UniTask DashAsync(CancellationToken cancellationToken)
    {
        if (IsDashing)
            return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < InputDeadZoneSqr)
            forward = Vector3.forward;
        else
            forward.Normalize();

        _dashDirection = forward;
        IsDashing = true;
        float endTime = Time.time + _dashDuration;

        try
        {
            while (Time.time < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(cancellationToken);
            }
        }
        finally
        {
            IsDashing = false;
        }
    }

    /// <summary>
    /// Short lunge via DOTween; uses CC.Move so it respects collisions.
    /// Cancellation stops immediately (for dash interrupt).
    /// </summary>
    public async UniTask LungeAsync(Vector3 direction, float distance, float duration, Ease ease, CancellationToken ct)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < InputDeadZoneSqr || distance < 0.001f || duration < 0.001f)
            return;
        direction.Normalize();

        float moved = 0f;
        var tcs = new UniTaskCompletionSource();

        Tweener tween = DOVirtual.Float(0f, distance, duration, v =>
            {
                float delta = v - moved;
                moved = v;
                if (delta > 0.0001f)
                    _cc.Move(direction * delta);
            })
            .SetEase(ease)
            .OnComplete(() => tcs.TrySetResult())
            .OnKill(() => tcs.TrySetResult());

        using (ct.Register(() => tween?.Kill()))
        {
            await tcs.Task;
            ct.ThrowIfCancellationRequested();
        }
    }

    private void UpdateHorizontalFromInput()
    {
        if (_moveInput.sqrMagnitude < InputDeadZoneSqr)
        {
            _horizontalWorld = Vector3.zero;
            return;
        }

        Vector2 input = _moveInput.sqrMagnitude > 1f ? _moveInput.normalized : _moveInput;

        Camera cam = _viewCamera != null ? _viewCamera : Camera.main;
        if (cam == null)
        {
            _horizontalWorld = new Vector3(input.x, 0f, input.y);
            if (_horizontalWorld.sqrMagnitude > 1f)
                _horizontalWorld.Normalize();
            return;
        }

        Vector3 f = cam.transform.forward;
        f.y = 0f;
        Vector3 r = cam.transform.right;
        r.y = 0f;
        if (f.sqrMagnitude < InputDeadZoneSqr || r.sqrMagnitude < InputDeadZoneSqr)
        {
            _horizontalWorld = new Vector3(input.x, 0f, input.y);
            if (_horizontalWorld.sqrMagnitude > 1f)
                _horizontalWorld.Normalize();
            return;
        }

        f.Normalize();
        r.Normalize();
        _horizontalWorld = f * input.y + r * input.x;
        if (_horizontalWorld.sqrMagnitude > 1f)
            _horizontalWorld.Normalize();
    }

    private Vector3 GetHorizontalVelocity()
    {
        if (IsDashing)
            return _dashDirection * _dashSpeed;
        float speed = _moveSpeed;
        if (IsSprinting && _horizontalWorld.sqrMagnitude >= InputDeadZoneSqr)
            speed *= Mathf.Max(1f, _sprintSpeedMultiplier);
        return _horizontalWorld * speed;
    }

    private void UpdateRotation(float dt)
    {
        if (IsDashing)
            return;

        if (_horizontalWorld.sqrMagnitude < InputDeadZoneSqr)
            return;

        Quaternion target = Quaternion.LookRotation(_horizontalWorld, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, _rotationSpeed * dt);
    }
}
