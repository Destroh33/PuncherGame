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
    [SerializeField] private float groundCheckDistance = 0.05f;
    [SerializeField] private float groundStickVelocity = -2f;

    [Header("Ground Stick Control")]
    [SerializeField] private float disableGroundStickAfterDash = 0.08f;

    private Rigidbody _rb;
    private BoxerCommandBuffer _cmd;
    private BoxerResourcesServer _resources;

    private float _nextDashAllowedTime;
    private float _disableGroundStickUntil = -1f;

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

        _disableGroundStickUntil = Mathf.Max(_disableGroundStickUntil, Time.time + 0.08f);
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

        if (grounded && Time.time >= _disableGroundStickUntil)
        {
            Vector3 v0 = _rb.linearVelocity;
            if (v0.y > groundStickVelocity)
                _rb.linearVelocity = new Vector3(v0.x, groundStickVelocity, v0.z);
        }

        // Dash: only if not controlLocked, cooldown ok, AND stamina spend succeeds
        if (!controlLocked && _cmd.ServerCmd.dashPressed && Time.time >= _nextDashAllowedTime)
        {
            // If you have resources, require cost payment at the moment of dash.
            if (_resources != null && !_resources.TrySpendDash())
            {
                // Not enough stamina -> no dash
                return;
            }

            _nextDashAllowedTime = Time.time + dashCooldown;

            Vector3 dashDir = worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : transform.forward;
            dashDir.y = 0f;

            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);

            Vector3 impulse = dashDir * dashImpulse + Vector3.up * dashUpImpulse;
            _rb.AddForce(impulse, ForceMode.Impulse);

            _disableGroundStickUntil = Time.time + disableGroundStickAfterDash;
            return;
        }

        if (controlLocked)
            return;

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
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        return Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }
}
