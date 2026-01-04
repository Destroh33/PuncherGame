using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class BoxerEliminationState : NetworkBehaviour
{
    [Header("Freeze Targets")]
    [SerializeField] private BoxerMotorServer motorServer;
    [SerializeField] private BoxerCombatServer combatServer;
    [SerializeField] private BoxerInputClient inputClient;
    [SerializeField] private BoxerAnimatorView animatorView;

    [Header("Visuals")]
    [Tooltip("If empty, we'll disable ALL renderers in children on eliminate.")]
    [SerializeField] private Renderer[] renderersToToggle;

    [Header("Physics")]
    [Tooltip("If empty, we'll disable ALL colliders in children on eliminate.")]
    [SerializeField] private Collider[] collidersToToggle;

    [Header("Last Attacker Credit")]
    [Tooltip("If KO happens long after last hit, no one gets credit.")]
    [SerializeField] private float lastAttackerValidSeconds = 6f;

    private Rigidbody _rb;

    private int _lastAttackerConnId = -1;
    private float _lastAttackerTime = -999f;

    private bool _eliminated;

    public bool Eliminated => _eliminated;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        if (motorServer == null) motorServer = GetComponent<BoxerMotorServer>();
        if (combatServer == null) combatServer = GetComponent<BoxerCombatServer>();
        if (inputClient == null) inputClient = GetComponent<BoxerInputClient>();
        if (animatorView == null) animatorView = GetComponent<BoxerAnimatorView>();

        if (renderersToToggle == null || renderersToToggle.Length == 0)
            renderersToToggle = GetComponentsInChildren<Renderer>(true);

        if (collidersToToggle == null || collidersToToggle.Length == 0)
            collidersToToggle = GetComponentsInChildren<Collider>(true);
    }

    // Called by BoxerCombatServer on server.
    [Server]
    public void ServerRecordLastAttacker(int attackerConnId)
    {
        _lastAttackerConnId = attackerConnId;
        _lastAttackerTime = Time.time;
    }

    [Server]
    public int ServerGetCreditedAttackerOrMinusOne()
    {
        if (_lastAttackerConnId < 0) return -1;
        if (Time.time - _lastAttackerTime > lastAttackerValidSeconds) return -1;
        return _lastAttackerConnId;
    }

    [Server]
    public void ServerEliminateToSpectator(Vector3 spectatorPos, Quaternion spectatorRot)
    {
        if (_eliminated) return;
        _eliminated = true;

        // Teleport and freeze hard.
        transform.SetPositionAndRotation(spectatorPos, spectatorRot);

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;

        // Disable gameplay on server.
        if (motorServer != null) motorServer.enabled = false;
        if (combatServer != null) combatServer.enabled = false;

        // Disable colliders so they don't interfere.
        for (int i = 0; i < collidersToToggle.Length; i++)
        {
            if (collidersToToggle[i] != null)
                collidersToToggle[i].enabled = false;
        }

        // Tell everyone to hide/freeze visuals (including the owner).
        ObserversSetVisuals(false);

        // Tell ONLY the owner to switch their Cinemachine follow to a fixed spectator anchor and disable local input.
        if (Owner != null)
            TargetEnterSpectator(Owner, spectatorPos, spectatorRot);
    }

    [Server]
    public void ServerRestoreForRound()
    {
        _eliminated = false;

        _rb.isKinematic = false;

        // Re-enable gameplay on server.
        if (motorServer != null) motorServer.enabled = true;
        if (combatServer != null) combatServer.enabled = true;

        // Re-enable colliders.
        for (int i = 0; i < collidersToToggle.Length; i++)
        {
            if (collidersToToggle[i] != null)
                collidersToToggle[i].enabled = true;
        }

        // Show visuals again.
        ObserversSetVisuals(true);

        // Owner camera returns to normal follow; input enabled again.
        if (Owner != null)
            TargetExitSpectator(Owner);
    }

    [ObserversRpc(BufferLast = true)]
    private void ObserversSetVisuals(bool visible)
    {
        if (animatorView != null)
            animatorView.enabled = visible;

        for (int i = 0; i < renderersToToggle.Length; i++)
        {
            if (renderersToToggle[i] != null)
                renderersToToggle[i].enabled = visible;
        }
    }

    [TargetRpc]
    private void TargetEnterSpectator(NetworkConnection ownerConn, Vector3 pos, Quaternion rot)
    {
        // Disable local input so no look/move.
        if (inputClient != null)
            inputClient.enabled = false;

        // Force Cinemachine to follow a fixed local anchor.
        var binder = GetComponent<LocalCinemachineBinder>();
        if (binder != null)
            binder.SetSpectatorFixed(pos, rot);
    }

    [TargetRpc]
    private void TargetExitSpectator(NetworkConnection ownerConn)
    {
        // Restore Cinemachine follow to player.
        var binder = GetComponent<LocalCinemachineBinder>();
        if (binder != null)
            binder.RestoreDefaultFollow();

        // Re-enable local input (only matters for the owner).
        if (inputClient != null)
            inputClient.enabled = true;
    }
}
