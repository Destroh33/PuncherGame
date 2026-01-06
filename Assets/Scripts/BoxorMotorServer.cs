using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerMotorServer : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float groundAcceleration = 40f;

    [Header("Air Control")]
    [SerializeField] private float airAcceleration = 20f;
    [SerializeField] private float airBrake = 0f;

    [Header("Rotation")]
    [SerializeField] private float yawSensitivity = 0.42f;
    [SerializeField] private float maxYawSpeedDeg = 720f;

    [Header("Dash")]
    [SerializeField] private float dashImpulse = 8f;
    [SerializeField] private float dashUpImpulse = 2f;
    [SerializeField] private float dashCooldown = 0.35f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckRadius = 0.1f;

    private Rigidbody _rb;
    private BoxerCommandBuffer _cmd;
    private BoxerResourcesServer _resources;

    private float _nextDashAllowedTime;

    private float _moveSpeedMult = 1f;
    public void SetMoveSpeedMultiplier(float mult) => _moveSpeedMult = Mathf.Max(0.01f, mult);

    private float _controlLockedUntil = -1f;

    [Server]
    public void ApplyExternalImpulse(Vector3 impulse, float lockControlSeconds)
    {
        _rb.AddForce(impulse, ForceMode.Impulse);

        float until = Time.time + Mathf.Max(0f, lockControlSeconds);
        if (until > _controlLockedUntil)
            _controlLockedUntil = until;

    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _rb = GetComponent<Rigidbody>();
        _cmd = GetComponent<BoxerCommandBuffer>();
        _resources = GetComponent<BoxerResourcesServer>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (!IsServerInitialized || _cmd == null)
            return;

        ApplyYaw();
        ApplyMoveAndDash();

        _cmd.ConsumeOneFrameButtons();
    }

    private void ApplyYaw()
    {
        float yawDelta = _cmd.ServerCmd.lookX * yawSensitivity;
        float maxDelta = maxYawSpeedDeg * Time.fixedDeltaTime;
        yawDelta = Mathf.Clamp(yawDelta, -maxDelta, maxDelta);

        if (Mathf.Abs(yawDelta) > 0.0001f)
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
    }

    private void ApplyMoveAndDash()
    {
        bool controlLocked = Time.time < _controlLockedUntil;

        Vector2 move = _cmd.ServerCmd.move;
        Vector3 local = new Vector3(move.x, 0f, move.y);
        local = Vector3.ClampMagnitude(local, 1f);

        Vector3 worldDir = transform.right * local.x + transform.forward * local.z;
        worldDir.y = 0f;

        bool grounded = IsGrounded();

        // Dash Logic
        if (!controlLocked && _cmd.ServerCmd.dashPressed && Time.time >= _nextDashAllowedTime)
        {
            if (_resources != null && !_resources.TrySpendDash())
            {
                return;
            }

            _nextDashAllowedTime = Time.time + dashCooldown;

            Vector3 dashDir = worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : transform.forward;
            dashDir.y = 0f;

            Vector3 v = _rb.linearVelocity;
            // Zero out horizontal velocity before dash for crisp control, keep Y
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);

            Vector3 impulse = dashDir * dashImpulse + Vector3.up * dashUpImpulse;
            _rb.AddForce(impulse, ForceMode.Impulse);

            // Removed _disableGroundStickUntil assignment
            return;
        }

        if (controlLocked)
            return;

        // Normal Movement
        Vector3 curVel = _rb.linearVelocity;
        Vector3 horiz = new Vector3(curVel.x, 0f, curVel.z);

        float finalSpeed = moveSpeed * _moveSpeedMult;
        Vector3 targetHoriz = worldDir * finalSpeed;

        if (grounded)
        {
            Vector3 newHoriz = Vector3.MoveTowards(horiz, targetHoriz, groundAcceleration * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(newHoriz.x, curVel.y, newHoriz.z);
        }
        else
        {
            if (worldDir.sqrMagnitude > 0.0001f)
            {
                Vector3 newHoriz = Vector3.MoveTowards(horiz, targetHoriz, airAcceleration * Time.fixedDeltaTime);
                _rb.linearVelocity = new Vector3(newHoriz.x, curVel.y, newHoriz.z);
            }
            else if (airBrake > 0f)
            {
                Vector3 newHoriz = Vector3.MoveTowards(horiz, Vector3.zero, airBrake * Time.fixedDeltaTime);
                _rb.linearVelocity = new Vector3(newHoriz.x, curVel.y, curVel.z);
            }
        }
    }

    private bool IsGrounded()
    {
        // Using CheckSphere as requested in your snippet
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        return Physics.CheckSphere(
            origin,
            groundCheckRadius,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }
}