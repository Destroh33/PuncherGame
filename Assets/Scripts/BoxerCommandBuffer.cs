using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class BoxerCommandBuffer : NetworkBehaviour
{
    public struct InputCmd
    {
        public Vector2 move;
        public float lookX;
        public bool punchHeld;
        public bool blockHeld;
        public bool dashPressed;
        public bool kickPressed;
    }

    public InputCmd ServerCmd { get; private set; }

    public readonly SyncVar<float> NetRunForward = new SyncVar<float>();
    public readonly SyncVar<float> NetRunLeft = new SyncVar<float>();
    public readonly SyncVar<bool> NetPunching = new SyncVar<bool>();
    public readonly SyncVar<bool> NetBlocking = new SyncVar<bool>();
    public readonly SyncVar<bool> NetKickEdge = new SyncVar<bool>();
    public readonly SyncVar<float> NetRunSpeedMult = new SyncVar<float>(1f);

    private bool _dashQueued;
    private bool _kickQueued;

    private BoxerResourcesServer _resources;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _resources = GetComponent<BoxerResourcesServer>();
    }

    [ServerRpc]
    public void SubmitInputServerRpc(
        Vector2 move,
        float lookX,
        bool punchHeld,
        bool blockHeld,
        bool dashPressed,
        bool kickPressed)
    {
        // Queue edges first
        if (dashPressed) _dashQueued = true;
        if (kickPressed) _kickQueued = true;

        // Resource gating (server authoritative)
        bool allowBlock = _resources == null || _resources.HasStaminaForBlock;
        bool allowDash = _resources == null || _resources.HasStaminaForDash;
        bool allowKick = _resources == null || _resources.HasFullPower;

        bool gatedBlockHeld = blockHeld && allowBlock;
        bool gatedDashPressed = _dashQueued && allowDash;
        bool gatedKickPressed = _kickQueued && allowKick;

        ServerCmd = new InputCmd
        {
            move = move,
            lookX = lookX,
            punchHeld = punchHeld,
            blockHeld = gatedBlockHeld,
            dashPressed = gatedDashPressed,
            kickPressed = gatedKickPressed
        };

        NetRunForward.Value = Mathf.Clamp(move.y, -1f, 1f);
        NetRunLeft.Value = Mathf.Clamp(-move.x, -1f, 1f);
        NetPunching.Value = punchHeld;
        NetBlocking.Value = gatedBlockHeld;

        // Animator kick edge: only if actually allowed
        if (gatedKickPressed)
            NetKickEdge.Value = true;
    }

    [Server]
    public void ConsumeOneFrameButtons()
    {
        _dashQueued = false;
        _kickQueued = false;

        var c = ServerCmd;
        c.dashPressed = false;
        c.kickPressed = false;
        ServerCmd = c;

        if (NetKickEdge.Value)
            NetKickEdge.Value = false;
    }
}
