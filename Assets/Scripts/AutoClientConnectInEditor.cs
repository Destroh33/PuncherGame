using FishNet;
using UnityEngine;

public class AutoClientConnectInEditor : MonoBehaviour
{
    [SerializeField] private EdgegapConnectConfig config;

    [Header("Editor-only convenience")]
    [SerializeField] private bool autoConnectInEditor = true;

    private void Start()
    {
#if UNITY_SERVER
        // Dedicated server build: do nothing here; ServerManager "Start On Headless" handles it.
        return;
#else
#if UNITY_EDITOR
        if (!autoConnectInEditor)
            return;

        if (config == null)
        {
            Debug.LogError("[AutoClientConnectInEditor] Missing EdgegapConnectConfig.");
            return;
        }

        var nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogError("[AutoClientConnectInEditor] InstanceFinder.NetworkManager is null.");
            return;
        }

        var transport = nm.TransportManager.Transport;
        if (transport == null)
        {
            Debug.LogError("[AutoClientConnectInEditor] Transport is null. Check TransportManager.");
            return;
        }

        // Detect transport by type name to avoid hard dependencies.
        string transportName = transport.GetType().Name;

        // If you're using Bayou later, you'll feed wsUrl. If you're using Tugboat now, feed udpHost/udpPort.
        if (transportName.Contains("Bayou"))
        {
            if (string.IsNullOrWhiteSpace(config.wsUrl))
            {
                Debug.LogError("[AutoClientConnectInEditor] Transport is Bayou but config.wsUrl is empty.");
                return;
            }

            transport.SetClientAddress(config.wsUrl);
            Debug.Log($"[AutoClientConnectInEditor] (Bayou) Connecting to {config.wsUrl}");
        }
        else
        {
            // Tugboat / UDP-style: set host + port.
            if (string.IsNullOrWhiteSpace(config.udpHost))
            {
                Debug.LogError("[AutoClientConnectInEditor] config.udpHost is empty.");
                return;
            }

            transport.SetClientAddress(config.udpHost);

            // FishNet transports expose SetPort on the base Transport in most versions;
            // if your version doesn't, the compiler will tell us and we'll swap to the transport-specific API.
            transport.SetPort(config.udpPort);

            Debug.Log($"[AutoClientConnectInEditor] ({transportName}) Connecting UDP to {config.udpHost}:{config.udpPort}");
        }

        nm.ClientManager.StartConnection();
#endif
#endif
    }
}
