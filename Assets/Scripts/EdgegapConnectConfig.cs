using UnityEngine;

[CreateAssetMenu(menuName = "Networking/Edgegap Connect Config")]
public class EdgegapConnectConfig : ScriptableObject
{
    [Header("UDP / Tugboat")]
    public string udpHost = "127.0.0.1";
    [Tooltip("Edgegap EXTERNAL port (not internal container port). Example: 32285")]
    public ushort udpPort = 7770;

    [Header("WebSocket / Bayou (later)")]
    [Tooltip("Example: ws://127.0.0.1:7770 or wss://xxxx.edgegap.net:PORT")]
    public string wsUrl = "";
}
