using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerBlockServer : NetworkBehaviour
{
    [Header("Block Mult")]
    [SerializeField] private float blockMult = 3f;

    [Header("Animator Layer Control (state names)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string kickStateName = "Kick";

    private Rigidbody _rb;
    private BoxerCommandBuffer _cmd;
    private BoxerMotorServer _motor;

    private float _originalMass;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _rb = GetComponent<Rigidbody>();
        _cmd = GetComponent<BoxerCommandBuffer>();
        _motor = GetComponent<BoxerMotorServer>();
        _originalMass = _rb.mass;
    }

    private void FixedUpdate()
    {
        if (!IsServerInitialized || _cmd == null)
            return;

        bool blockHeld = _cmd.ServerCmd.blockHeld;
        bool inKick = IsInKickState_Server();

        if (blockHeld && !inKick)
        {
            _rb.mass = _originalMass * blockMult;

            if (_motor != null)
                _motor.SetMoveSpeedMultiplier(1f / blockMult);

            _cmd.NetRunSpeedMult.Value = 1f / blockMult;
        }
        else
        {
            _rb.mass = _originalMass;

            if (_motor != null)
                _motor.SetMoveSpeedMultiplier(1f);

            _cmd.NetRunSpeedMult.Value = 1f;
        }
    }

    private bool IsInKickState_Server()
    {
        if (animator == null)
            return false;

        // This checks the server's animator state; that matches your original behavior.
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
}
