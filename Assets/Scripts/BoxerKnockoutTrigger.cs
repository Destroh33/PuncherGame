using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
public class BoxerKnockoutTrigger : NetworkBehaviour
{
    [SerializeField] private string knockoutTag = "Knockout";

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServerInitialized) return;
        if (other == null) return;

        if (!string.IsNullOrEmpty(knockoutTag) && other.CompareTag(knockoutTag))
        {
            var gm = FindFirstObjectByType<GameManager>();
            if (gm != null)
                gm.ServerOnPlayerKnockedOut(this);
        }
    }
}