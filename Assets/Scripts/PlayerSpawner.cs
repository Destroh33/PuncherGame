using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Options")]
    [SerializeField] private bool despawnOnDisconnect = true;

    private NetworkManager _nm;

    private readonly Dictionary<int, NetworkObject> _spawnedByConnId = new Dictionary<int, NetworkObject>(32);

    private void Awake()
    {
        _nm = InstanceFinder.NetworkManager;
        if (_nm == null)
            Debug.LogError("PlayerSpawner: NetworkManager not found (InstanceFinder.NetworkManager is null).");
    }

    private void OnEnable()
    {
        if (_nm == null) return;

        // Signature in FishNet 4.6.x: (NetworkConnection conn, RemoteConnectionStateArgs args)
        _nm.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        _nm.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
    }

    private void OnDisable()
    {
        if (_nm == null) return;

        _nm.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        _nm.ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
    }

    private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped)
            _spawnedByConnId.Clear();
    }

    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        // Only run on server.
        if (_nm == null || !_nm.IsServerStarted)
            return;

        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            SpawnFor(conn);
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            if (despawnOnDisconnect)
                DespawnFor(conn);
        }
    }

    private void SpawnFor(NetworkConnection conn)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawner: playerPrefab is null.");
            return;
        }

        if (conn == null)
        {
            Debug.LogError("PlayerSpawner: conn is null.");
            return;
        }

        // Prevent double spawn.
        if (_spawnedByConnId.ContainsKey(conn.ClientId))
            return;

        Transform sp = GetSpawnPoint(conn.ClientId);
        Vector3 pos = sp != null ? sp.position : Vector3.zero;
        Quaternion rot = sp != null ? sp.rotation : Quaternion.identity;

        NetworkObject nob = Instantiate(playerPrefab, pos, rot);

        // IMPORTANT: Spawn with ownership.
        _nm.ServerManager.Spawn(nob, conn);

        _spawnedByConnId[conn.ClientId] = nob;

        Debug.Log($"PlayerSpawner: Spawned player for ClientId={conn.ClientId} at {pos}");
    }

    private void DespawnFor(NetworkConnection conn)
    {
        if (conn == null) return;

        if (!_spawnedByConnId.TryGetValue(conn.ClientId, out NetworkObject nob) || nob == null)
        {
            _spawnedByConnId.Remove(conn.ClientId);
            return;
        }

        _nm.ServerManager.Despawn(nob);
        _spawnedByConnId.Remove(conn.ClientId);

        Debug.Log($"PlayerSpawner: Despawned player for ClientId={conn.ClientId}");
    }

    private Transform GetSpawnPoint(int clientId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        int idx = Mathf.Abs(clientId) % spawnPoints.Length;
        return spawnPoints[idx];
    }
}
