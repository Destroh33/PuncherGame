using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ServerPhysicsAuthority : NetworkBehaviour
{
    private Rigidbody _rb;
    private bool _originalKinematic;
    private bool _originalUseGravity;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _rb = GetComponent<Rigidbody>();
        _originalKinematic = _rb.isKinematic;
        _originalUseGravity = _rb.useGravity;

        ApplyAuthoritySettings();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyAuthoritySettings();
    }

    private void ApplyAuthoritySettings()
    {
        if (_rb == null) return;

        // Server runs physics.
        if (IsServerInitialized)
        {
            _rb.isKinematic = _originalKinematic;   // usually false
            _rb.useGravity = _originalUseGravity;   // usually true
        }
        // Clients do not simulate physics for networked rigidbodies.
        else
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }
}
