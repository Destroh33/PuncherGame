using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerRigidbodyController : MonoBehaviour
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

    [Header("BlockMult")]
    [SerializeField] private float blockMult = 2f;
    
    private float originalMass;
    private float originalSpeed;
    private int _handsLayerIndex = -1;
    private Rigidbody _rb;
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
    private float _nextDashAllowedTime;

    private readonly Dictionary<int, float> _nextAllowedHitTimeByTargetId = new Dictionary<int, float>(32);
    private readonly List<int> _tempRemoveList = new List<int>(64);

    private bool _kickPending;
    private float _kickHitOpensAt;
    private float _kickHitClosesAt;

    private float _disableGroundStickUntil = -1f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        originalMass = _rb.mass;
        originalSpeed = moveSpeed;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _inputActions = new InputSystem_Actions();
        CacheHandsLayerIndex();
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

    private void OnEnable()
    {
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

    private void OnDisable()
    {
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
    }

    private void Update()
    {
        float t = 1f - Mathf.Exp(-inputLerpSpeed * Time.deltaTime);
        _in.moveSmoothed = Vector2.Lerp(_in.moveSmoothed, _in.moveRaw, t);

        if (animator != null)
        {
            if (_in.kickPressed)
                animator.SetTrigger(kickTriggerParam);

            animator.SetFloat(runForwardParam, Mathf.Clamp(_in.moveSmoothed.y, -1f, 1f));
            animator.SetFloat(runLeftParam, Mathf.Clamp(-_in.moveSmoothed.x, -1f, 1f));
            animator.SetBool(punchingParam, _in.punchHeld);
            animator.SetBool(blockingParam, _in.blockHeld);

            UpdateHandsLayerWeightFromBaseLayer();
        }

        _in.kickPressed = false;
        if (_in.blockHeld && !IsBaseLayerInOrTransitioningKick())
        {
            _rb.mass = originalMass * blockMult;
            moveSpeed = originalSpeed / blockMult;
            animator.SetFloat(runSpeedMultParam, 1f / blockMult);
        }
        else
        {
            _rb.mass = originalMass;
            moveSpeed = originalSpeed;
            animator.SetFloat(runSpeedMultParam, 1f);
        }
        if (_kickPending && Time.time > _kickHitClosesAt)
            _kickPending = false;
    }

    private void FixedUpdate()
    {
        ApplyYawRotation();
        ApplyMoveAndDash();

        _in.dashPressed = false;
        _in.look = Vector2.zero;

        CleanupHitCooldownCache();
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

    private void ApplyYawRotation()
    {
        float yawDelta = _in.look.x * yawSensitivity;
        float maxDelta = maxYawSpeedDeg * Time.fixedDeltaTime;
        yawDelta = Mathf.Clamp(yawDelta, -maxDelta, maxDelta);

        if (Mathf.Abs(yawDelta) > 0.0001f)
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
    }

    private void ApplyMoveAndDash()
    {
        Vector3 local = new Vector3(_in.moveSmoothed.x, 0f, _in.moveSmoothed.y);
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

        if (_in.dashPressed && Time.time >= _nextDashAllowedTime)
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

        bool punchReady = _in.punchHeld;

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

    private void OnKick(InputAction.CallbackContext ctx)
    {
        _in.kickPressed = true;

        float now = Time.time;
        _kickPending = true;
        _kickHitOpensAt = now + kickWindupSeconds;
        _kickHitClosesAt = _kickHitOpensAt + Mathf.Max(0f, kickActiveWindowSeconds);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Gizmos.DrawWireSphere(origin + Vector3.down * groundCheckDistance, groundCheckRadius);
    }
}
