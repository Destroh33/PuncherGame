using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerRigidbodyController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float groundAcceleration = 40f;

    [Header("Air Control")]
    [SerializeField] private float airAcceleration = 20f;
    [SerializeField] private float airBrake = 0f;

    [Header("Rotation (yaw only)")]
    [SerializeField] private float yawSensitivity = 0.12f;
    [SerializeField] private float maxYawSpeedDeg = 720f;

    [Header("Dash (triggered by Jump)")]
    [SerializeField] private float dashImpulse = 8f;
    [SerializeField] private float dashUpImpulse = 1.5f;
    [SerializeField] private float dashCooldown = 0.35f;

    [Header("Smoothing")]
    [SerializeField] private float inputLerpSpeed = 18f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.28f;
    [SerializeField] private float groundStickVelocity = -2f;

    [Header("Ground Stick Control")]
    [SerializeField] private float disableGroundStickAfterDash = 0.08f;

    [Header("Animator Params")]
    [SerializeField] private string runForwardParam = "runforward";
    [SerializeField] private string runLeftParam = "runleft";
    [SerializeField] private string punchingParam = "punching";
    [SerializeField] private string blockingParam = "blocking";
    [SerializeField] private string kickTriggerParam = "kick";
    [SerializeField] private string runSpeedMultParam = "runspeed";

    [Header("Animator Layer Control")]
    [SerializeField] private string handsLayerName = "HandsLayer";
    [SerializeField] private string kickStateName = "Kick";
    [SerializeField] private string runBlendStateName = "RunBlend";

    [Header("Hit Knockback (Base Impulses)")]
    [SerializeField] private float punchForwardImpulse = 3.5f;
    [SerializeField] private float punchUpImpulse = 0.75f;
    [SerializeField] private float kickForwardImpulse = 8.0f;
    [SerializeField] private float kickUpImpulse = 1.8f;

    [Header("Hit Knockback (Momentum Scaling)")]
    [SerializeField] private float knockbackSpeedToMultiplier = 0.08f;
    [SerializeField] private float knockbackMultiplierMin = 0.35f;
    [SerializeField] private float knockbackMultiplierMax = 1.8f;
    [SerializeField] private float forwardKnockbackMultWeight = 1.0f;
    [SerializeField] private float upKnockbackMultWeight = 0.8f;

    [Header("Hit Rules")]
    [SerializeField] private float hitCooldownPerTarget = 0.12f;

    [Header("Kick Hit Timing")]
    [SerializeField] private float kickWindupSeconds = 0.20f;
    [SerializeField] private float kickActiveWindowSeconds = 0.12f;
    [SerializeField] private bool requireKickStateForHit = true;

    [Header("Hit Filtering")]
    [SerializeField] private string hittableTag = "Player";

    [Header("Block Mult")]
    [SerializeField] private float blockMult = 2f;

    private float originalMass;
    private float originalSpeed;

    private int _handsLayerIndex = -1;
    private Rigidbody _rb;

    // InputSystem only for local owner
    private InputSystem_Actions _inputActions;

    private struct InputState
    {
        public Vector2 moveRaw;
        public Vector2 moveSmoothed;
        public Vector2 look;
        public bool punchHeld;
        public bool blockHeld;
        public bool dashPressed;
        public bool kickPressed;
    }

    private InputState _in;

    // Server-side authoritative input snapshot (sent from owner).
    private struct NetInput
    {
        public Vector2 move;
        public float yawLookX;
        public bool punchHeld;
        public bool blockHeld;
        public bool dashPressed;
        public bool kickPressed;
    }

    private NetInput _serverInput;

    private float _nextDashAllowedTime;
    private bool _kickPending;
    private float _kickHitOpensAt;
    private float _kickHitClosesAt;
    private float _disableGroundStickUntil = -1f;

    private readonly Dictionary<int, float> _nextAllowedHitTimeByTargetId = new Dictionary<int, float>(32);
    private readonly List<int> _tempRemoveList = new List<int>(64);

    // FishNet 4.6+: SyncVar<T> (attribute is obsolete)
    private readonly SyncVar<float> _netRunForward = new SyncVar<float>();
    private readonly SyncVar<float> _netRunLeft = new SyncVar<float>();
    private readonly SyncVar<bool> _netPunching = new SyncVar<bool>();
    private readonly SyncVar<bool> _netBlocking = new SyncVar<bool>();
    private readonly SyncVar<bool> _netKickEdge = new SyncVar<bool>(); // edge flag

    private bool _kickEdgeConsumed;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _rb = GetComponent<Rigidbody>();
        originalMass = _rb.mass;
        originalSpeed = moveSpeed;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        CacheHandsLayerIndex();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // FishNet rule: don't use IsOwner here; use base.Owner.IsLocalClient
        if (base.Owner != null && base.Owner.IsLocalClient)
            SetupLocalInput();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        TeardownLocalInput();
    }

    private void SetupLocalInput()
    {
        if (_inputActions != null)
            return;

        _inputActions = new InputSystem_Actions();
        _inputActions.Enable();

        _inputActions.Player.Move.performed += OnMove;
        _inputActions.Player.Move.canceled += OnMove;

        _inputActions.Player.Look.performed += OnLook;
        _inputActions.Player.Look.canceled += OnLook;

        _inputActions.Player.Jump.performed += OnJump;

        _inputActions.Player.Punch.started += OnPunch;
        _inputActions.Player.Punch.canceled += OnPunch;

        _inputActions.Player.Block.started += OnBlock;
        _inputActions.Player.Block.canceled += OnBlock;

        _inputActions.Player.Kick.started += OnKick;
    }

    private void TeardownLocalInput()
    {
        if (_inputActions == null)
            return;

        _inputActions.Player.Move.performed -= OnMove;
        _inputActions.Player.Move.canceled -= OnMove;

        _inputActions.Player.Look.performed -= OnLook;
        _inputActions.Player.Look.canceled -= OnLook;

        _inputActions.Player.Jump.performed -= OnJump;

        _inputActions.Player.Punch.started -= OnPunch;
        _inputActions.Player.Punch.canceled -= OnPunch;

        _inputActions.Player.Block.started -= OnBlock;
        _inputActions.Player.Block.canceled -= OnBlock;

        _inputActions.Player.Kick.started -= OnKick;

        _inputActions.Disable();
        _inputActions = null;
    }

    private void CacheHandsLayerIndex()
    {
        if (animator == null)
        {
            _handsLayerIndex = -1;
            return;
        }
        _handsLayerIndex = animator.GetLayerIndex(handsLayerName);
    }

    private void Update()
    {
        // Local owner: smooth and send input.
        if (base.Owner != null && base.Owner.IsLocalClient)
        {
            float t = 1f - Mathf.Exp(-inputLerpSpeed * Time.deltaTime);
            _in.moveSmoothed = Vector2.Lerp(_in.moveSmoothed, _in.moveRaw, t);

            SendInputToServer();
        }

        // Everyone: animate from replicated state.
        UpdateAnimatorFromNetState();
    }

    private void FixedUpdate()
    {
        // Server-only physics and hit logic.
        if (!IsServerInitialized)
            return;

        ApplyYawRotation_Server();
        ApplyMoveAndDash_Server();
        CleanupHitCooldownCache();
    }

    private void SendInputToServer()
    {
        // Package owner snapshot and clear one-frame buttons.
        Vector2 move = _in.moveSmoothed;
        float yawLookX = _in.look.x;

        bool punchHeld = _in.punchHeld;
        bool blockHeld = _in.blockHeld;
        bool dashPressed = _in.dashPressed;
        bool kickPressed = _in.kickPressed;

        _in.dashPressed = false;
        _in.kickPressed = false;
        _in.look = Vector2.zero;

        SubmitInputServerRpc(move, yawLookX, punchHeld, blockHeld, dashPressed, kickPressed);
    }

    [ServerRpc]
    private void SubmitInputServerRpc(Vector2 move, float yawLookX, bool punchHeld, bool blockHeld, bool dashPressed, bool kickPressed)
    {
        _serverInput.move = move;
        _serverInput.yawLookX = yawLookX;
        _serverInput.punchHeld = punchHeld;
        _serverInput.blockHeld = blockHeld;
        _serverInput.dashPressed = dashPressed;
        _serverInput.kickPressed = kickPressed;

        _netRunForward.Value = Mathf.Clamp(move.y, -1f, 1f);
        _netRunLeft.Value = Mathf.Clamp(-move.x, -1f, 1f);
        _netPunching.Value = punchHeld;
        _netBlocking.Value = blockHeld;

        if (kickPressed)
            _netKickEdge.Value = true;
    }

    private void UpdateAnimatorFromNetState()
    {
        if (animator == null)
            return;

        bool kickEdge = _netKickEdge.Value;
        if (kickEdge && !_kickEdgeConsumed)
        {
            animator.SetTrigger(kickTriggerParam);
            _kickEdgeConsumed = true;
        }
        if (!kickEdge)
        {
            _kickEdgeConsumed = false;
        }

        animator.SetFloat(runForwardParam, _netRunForward.Value);
        animator.SetFloat(runLeftParam, _netRunLeft.Value);
        animator.SetBool(punchingParam, _netPunching.Value);
        animator.SetBool(blockingParam, _netBlocking.Value);

        UpdateHandsLayerWeightFromBaseLayer();

        if (_netBlocking.Value && !IsBaseLayerInOrTransitioningKick())
        {
            _rb.mass = originalMass * blockMult;
            moveSpeed = originalSpeed / blockMult;
            if (!string.IsNullOrEmpty(runSpeedMultParam))
                animator.SetFloat(runSpeedMultParam, 1f / blockMult);
        }
        else
        {
            _rb.mass = originalMass;
            moveSpeed = originalSpeed;
            if (!string.IsNullOrEmpty(runSpeedMultParam))
                animator.SetFloat(runSpeedMultParam, 1f);
        }
    }

    private void UpdateHandsLayerWeightFromBaseLayer()
    {
        if (_handsLayerIndex < 0) return;

        bool inKick = IsBaseLayerInOrTransitioningKick();

        if (inKick)
        {
            if (!Mathf.Approximately(animator.GetLayerWeight(_handsLayerIndex), 0f))
                animator.SetLayerWeight(_handsLayerIndex, 0f);
        }
        else
        {
            if (IsBaseLayerInOrTransitioningRunBlend())
            {
                if (!Mathf.Approximately(animator.GetLayerWeight(_handsLayerIndex), 1f))
                    animator.SetLayerWeight(_handsLayerIndex, 1f);
            }
        }
    }

    private bool IsBaseLayerInOrTransitioningKick()
    {
        if (animator == null) return false;

        var cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsName(kickStateName))
            return true;

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsName(kickStateName))
                return true;
        }

        return false;
    }

    private bool IsBaseLayerInOrTransitioningRunBlend()
    {
        if (animator == null) return false;

        var cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsName(runBlendStateName))
            return true;

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsName(runBlendStateName))
                return true;
        }

        return false;
    }

    private void ApplyYawRotation_Server()
    {
        float yawDelta = _serverInput.yawLookX * yawSensitivity;
        float maxDelta = maxYawSpeedDeg * Time.fixedDeltaTime;
        yawDelta = Mathf.Clamp(yawDelta, -maxDelta, maxDelta);

        if (Mathf.Abs(yawDelta) > 0.0001f)
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
    }

    private void ApplyMoveAndDash_Server()
    {
        Vector3 local = new Vector3(_serverInput.move.x, 0f, _serverInput.move.y);
        local = Vector3.ClampMagnitude(local, 1f);

        Vector3 worldDir = (transform.right * local.x + transform.forward * local.z);
        worldDir.y = 0f;

        bool grounded = IsGrounded();

        if (grounded && Time.time >= _disableGroundStickUntil)
        {
            Vector3 v0 = _rb.linearVelocity;
            if (v0.y > groundStickVelocity)
                _rb.linearVelocity = new Vector3(v0.x, groundStickVelocity, v0.z);
        }

        if (_serverInput.kickPressed)
        {
            float now = Time.time;
            _kickPending = true;
            _kickHitOpensAt = now + kickWindupSeconds;
            _kickHitClosesAt = _kickHitOpensAt + Mathf.Max(0f, kickActiveWindowSeconds);
        }

        if (_kickPending && Time.time > _kickHitClosesAt)
            _kickPending = false;

        if (_serverInput.dashPressed && Time.time >= _nextDashAllowedTime)
        {
            _nextDashAllowedTime = Time.time + dashCooldown;

            Vector3 dashDir = worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : transform.forward;
            dashDir.y = 0f;

            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);

            Vector3 impulse = dashDir.normalized * dashImpulse + Vector3.up * dashUpImpulse;
            _rb.AddForce(impulse, ForceMode.Impulse);

            _disableGroundStickUntil = Time.time + disableGroundStickAfterDash;
            return;
        }

        Vector3 curVel = _rb.linearVelocity;
        Vector3 horiz = new Vector3(curVel.x, 0f, curVel.z);
        Vector3 targetHoriz = worldDir * moveSpeed;

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

        // Clear edge after server tick so clients can re-consume next kick.
        if (_netKickEdge.Value)
            _netKickEdge.Value = false;
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

    private void OnTriggerStay(Collider other)
    {
        if (!IsServerInitialized) return;
        if (!enabled || other == null) return;
        if (other.attachedRigidbody == _rb) return;

        if (!string.IsNullOrEmpty(hittableTag) && !other.CompareTag(hittableTag))
            return;

        Rigidbody targetRb = other.attachedRigidbody;
        if (targetRb == null) return;

        int targetId = targetRb.GetInstanceID();
        float now = Time.time;

        if (_nextAllowedHitTimeByTargetId.TryGetValue(targetId, out float nextAllowed) && now < nextAllowed)
            return;

        bool kickReady = _kickPending && now >= _kickHitOpensAt && now <= _kickHitClosesAt;
        if (kickReady && requireKickStateForHit && !IsBaseLayerInOrTransitioningKick())
            kickReady = false;

        bool punchReady = _serverInput.punchHeld;

        if (!kickReady && !punchReady)
            return;

        bool useKick = kickReady;

        float baseForward = useKick ? kickForwardImpulse : punchForwardImpulse;
        float baseUp = useKick ? kickUpImpulse : punchUpImpulse;

        Vector3 pushDir = (targetRb.worldCenterOfMass - _rb.worldCenterOfMass);
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.0001f) pushDir = transform.forward;
        pushDir.Normalize();

        Vector3 attackerHorizVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        float forwardSpeed = Vector3.Dot(attackerHorizVel, transform.forward);

        float rawMult = 1f + forwardSpeed * knockbackSpeedToMultiplier;
        float mult = Mathf.Clamp(rawMult, knockbackMultiplierMin, knockbackMultiplierMax);

        float forwardScale = Mathf.Lerp(1f, mult, Mathf.Clamp01(forwardKnockbackMultWeight));
        float upScale = Mathf.Lerp(1f, mult, Mathf.Clamp01(upKnockbackMultWeight));

        float finalForward = baseForward * forwardScale;
        float finalUp = baseUp * upScale;

        Vector3 impulse = pushDir * finalForward + Vector3.up * finalUp;
        targetRb.AddForce(impulse, ForceMode.Impulse);

        _nextAllowedHitTimeByTargetId[targetId] = now + hitCooldownPerTarget;

        if (useKick)
            _kickPending = false;
    }

    private void CleanupHitCooldownCache()
    {
        if (_nextAllowedHitTimeByTargetId.Count < 64) return;

        float now = Time.time;
        _tempRemoveList.Clear();

        foreach (var kvp in _nextAllowedHitTimeByTargetId)
        {
            if (kvp.Value <= now)
                _tempRemoveList.Add(kvp.Key);
        }

        for (int i = 0; i < _tempRemoveList.Count; i++)
            _nextAllowedHitTimeByTargetId.Remove(_tempRemoveList[i]);
    }

    private void OnMove(InputAction.CallbackContext ctx) => _in.moveRaw = ctx.ReadValue<Vector2>();
    private void OnLook(InputAction.CallbackContext ctx) => _in.look = ctx.ReadValue<Vector2>();
    private void OnJump(InputAction.CallbackContext ctx) => _in.dashPressed = true;
    private void OnPunch(InputAction.CallbackContext ctx) => _in.punchHeld = ctx.phase != InputActionPhase.Canceled;
    private void OnBlock(InputAction.CallbackContext ctx) => _in.blockHeld = ctx.phase != InputActionPhase.Canceled;
    private void OnKick(InputAction.CallbackContext ctx) => _in.kickPressed = true;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Gizmos.DrawWireSphere(origin + Vector3.down * groundCheckDistance, groundCheckRadius);
    }
}
