using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[DisallowMultipleComponent]
public class BoxerResourcesServer : NetworkBehaviour
{
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaDecayPerSecond = 2f;     // slow decay over time
    [SerializeField] private float blockDrainPerSecond = 12f;      // extra drain while blocking
    [SerializeField] private float dashStaminaCost = 25f;          // chunk cost
    [SerializeField] private float staminaMinToAllowBlock = 1f;    // threshold to allow
    [SerializeField] private float staminaMinToAllowDash = 25f;    // can set == dash cost

    [Header("Power")]
    [SerializeField] private int punchesToFullPower = 3;

    // UI-friendly normalized values (0..1)
    public readonly SyncVar<float> NetStamina01 = new SyncVar<float>(1f);
    public readonly SyncVar<float> NetPower01 = new SyncVar<float>(0f);

    // Server state
    private float _stamina;
    private int _punchesLandedSinceKick;

    public float Stamina => _stamina;
    public float MaxStamina => maxStamina;

    public bool HasStaminaForBlock => _stamina >= staminaMinToAllowBlock;
    public bool HasStaminaForDash => _stamina >= staminaMinToAllowDash;
    public bool HasFullPower => _punchesLandedSinceKick >= punchesToFullPower;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        // Server initializes; clients will receive SyncVars.
        if (IsServerInitialized)
        {
            _stamina = maxStamina;
            _punchesLandedSinceKick = 0;
            PushNet();
        }
    }

    private void Update()
    {
        if (!IsServerInitialized)
            return;

        // Always-decay stamina over time
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (staminaDecayPerSecond > 0f)
        {
            _stamina = Mathf.Max(0f, _stamina - staminaDecayPerSecond * dt);
            PushNet();
        }
    }

    [Server]
    public void DrainForBlocking(float dt)
    {
        if (dt <= 0f) return;
        if (blockDrainPerSecond <= 0f) return;

        _stamina = Mathf.Max(0f, _stamina - blockDrainPerSecond * dt);
        PushNet();
    }

    [Server]
    public bool TrySpendDash()
    {
        if (_stamina < dashStaminaCost || _stamina < staminaMinToAllowDash)
            return false;

        _stamina = Mathf.Max(0f, _stamina - dashStaminaCost);
        PushNet();
        return true;
    }

    [Server]
    public void OnPunchLanded()
    {
        _punchesLandedSinceKick = Mathf.Min(_punchesLandedSinceKick + 1, punchesToFullPower);
        PushNet();
    }

    [Server]
    public bool TryConsumeKickPower()
    {
        if (!HasFullPower)
            return false;

        _punchesLandedSinceKick = 0;
        PushNet();
        return true;
    }

    private void PushNet()
    {
        // normalized bars
        NetStamina01.Value = (maxStamina <= 0f) ? 0f : Mathf.Clamp01(_stamina / maxStamina);
        NetPower01.Value = Mathf.Clamp01(punchesToFullPower <= 0 ? 0f : (float)_punchesLandedSinceKick / punchesToFullPower);
    }
}
