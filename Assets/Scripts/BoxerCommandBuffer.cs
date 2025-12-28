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

    // NEW: buffer one-frame buttons so FixedUpdate can’t miss them
    private bool _dashQueued;
    private bool _kickQueued;

    [ServerRpc]
    public void SubmitInputServerRpc(
        Vector2 move,
        float lookX,
        bool punchHeld,
        bool blockHeld,
        bool dashPressed,
        bool kickPressed)
    {
        // Queue edges
        if (dashPressed) _dashQueued = true;
        if (kickPressed) _kickQueued = true;

        ServerCmd = new InputCmd
        {
            move = move,
            lookX = lookX,
            punchHeld = punchHeld,
            blockHeld = blockHeld,
            // expose queued states to server sim
            dashPressed = _dashQueued,
            kickPressed = _kickQueued
        };

        NetRunForward.Value = Mathf.Clamp(move.y, -1f, 1f);
        NetRunLeft.Value = Mathf.Clamp(-move.x, -1f, 1f);
        NetPunching.Value = punchHeld;
        NetBlocking.Value = blockHeld;

        if (kickPressed)
            NetKickEdge.Value = true;
    }

    [Server]
    public void ConsumeOneFrameButtons()
    {
        // called from server FixedUpdate AFTER reading ServerCmd
        _dashQueued = false;
        _kickQueued = false;

        // update cmd to reflect consumed state (optional but keeps debug sane)
        var c = ServerCmd;
        c.dashPressed = false;
        c.kickPressed = false;
        ServerCmd = c;

        if (NetKickEdge.Value)
            NetKickEdge.Value = false;
    }
}
