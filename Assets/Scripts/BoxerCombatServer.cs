using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerCombatServer : NetworkBehaviour
{
    [Header("Knockback Base")]
    [SerializeField] private float punchForwardImpulse = 1.5f;
    [SerializeField] private float punchUpImpulse = 1f;
    [SerializeField] private float kickForwardImpulse = 6f;
    [SerializeField] private float kickUpImpulse = 6f;

    [Header("Momentum Scaling")]
    [SerializeField] private float knockbackSpeedToMultiplier = 0.08f;
    [SerializeField] private float knockbackMultiplierMin = 0.5f;
    [SerializeField] private float knockbackMultiplierMax = 1.4f;
    [SerializeField] private float forwardKnockbackMultWeight = 0.7f;
    [SerializeField] private float upKnockbackMultWeight = 0.7f;

    [Header("Rules (Punch)")]
    [SerializeField] private float punchHitCooldownPerTarget = 0.2f;

    [Header("Rules (Both)")]
    [SerializeField] private string hittableTag = "Player";

    [Header("Kick Timing")]
    [SerializeField] private float kickWindupSeconds = 0.1f;
    [SerializeField] private float kickActiveWindowSeconds = 0.12f;

    [Header("Hitstun (movement lock)")]
    [SerializeField] private float hitstunSeconds = 0.12f;

    private Rigidbody _rb;
    private BoxerCommandBuffer _cmd;
    private BoxerResourcesServer _resources;

    private readonly Dictionary<int, float> _nextAllowedPunchHitTimeByTarget = new Dictionary<int, float>(32);
    private readonly HashSet<int> _kickHitTargetsThisSwing = new HashSet<int>();

    private bool _kickPending;
    private float _kickOpensAt;
    private float _kickClosesAt;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _rb = GetComponent<Rigidbody>();
        _cmd = GetComponent<BoxerCommandBuffer>();
        _resources = GetComponent<BoxerResourcesServer>();
    }

    private void Update()
    {
        if (!IsServerInitialized || _cmd == null)
            return;

        if (_cmd.ServerCmd.kickPressed)
        {
            if (_resources != null)
            {
                if (!_resources.TryConsumeKickPower())
                    return;
            }

            float now = Time.time;
            _kickPending = true;
            _kickOpensAt = now + kickWindupSeconds;
            _kickClosesAt = _kickOpensAt + Mathf.Max(0f, kickActiveWindowSeconds);

            _kickHitTargetsThisSwing.Clear();
        }

        if (_kickPending && Time.time > _kickClosesAt)
            _kickPending = false;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsServerInitialized) return;
        if (other == null) return;
        if (other.attachedRigidbody == _rb) return;

        if (!string.IsNullOrEmpty(hittableTag) && !other.CompareTag(hittableTag))
            return;

        Rigidbody targetRb = other.attachedRigidbody;
        if (targetRb == null) return;

        NetworkObject targetNob = targetRb.GetComponent<NetworkObject>();
        if (targetNob == null) return;

        int targetId = targetNob.ObjectId;
        float now = Time.time;

        bool kickReady = _kickPending && now >= _kickOpensAt && now <= _kickClosesAt;
        bool punchReady = _cmd.ServerCmd.punchHeld;

        if (!kickReady && !punchReady)
            return;

        if (kickReady)
        {
            if (_kickHitTargetsThisSwing.Contains(targetId))
                return;

            ApplyKnockbackToTarget(targetRb, useKick: true);
            _kickHitTargetsThisSwing.Add(targetId);
            return;
        }

        if (_nextAllowedPunchHitTimeByTarget.TryGetValue(targetId, out float nextAllowed) && now < nextAllowed)
            return;

        ApplyKnockbackToTarget(targetRb, useKick: false);
        _nextAllowedPunchHitTimeByTarget[targetId] = now + punchHitCooldownPerTarget;

        if (_resources != null)
            _resources.OnPunchLanded();
    }

    private void ApplyKnockbackToTarget(Rigidbody targetRb, bool useKick)
    {
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

        Vector3 impulse = pushDir * (baseForward * forwardScale) + Vector3.up * (baseUp * upScale);

        // NEW: record "last attacker" on the victim, server-side.
        // Attacker is THIS object's owner connection id.
        int attackerConnId = (Owner != null) ? Owner.ClientId : -1;
        BoxerEliminationState victimElim = targetRb.GetComponent<BoxerEliminationState>();
        if (victimElim != null && attackerConnId >= 0)
            victimElim.ServerRecordLastAttacker(attackerConnId);

        BoxerMotorServer victimMotor = targetRb.GetComponent<BoxerMotorServer>();
        if (victimMotor != null)
            victimMotor.ApplyExternalImpulse(impulse, hitstunSeconds);
        else
            targetRb.AddForce(impulse, ForceMode.Impulse);
    }
}
