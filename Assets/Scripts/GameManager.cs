using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public enum MatchState : byte
    {
        WaitingForPlayers = 0,
        InRound = 1,
        RoundEnd = 2
    }

    [Header("Spawn Settings")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Spectator Point")]
    [SerializeField] private Transform spectatorPoint;

    [Header("Round Rules")]
    [SerializeField] private float roundDurationSeconds = 60f;
    [SerializeField] private float roundEndDelaySeconds = 5f;
    [SerializeField] private int minPlayersToStart = 2;

    [Header("Options")]
    [SerializeField] private bool despawnOnDisconnect = true;

    private readonly SyncVar<MatchState> _state = new SyncVar<MatchState>();
    private readonly SyncVar<float> _roundTimeRemaining = new SyncVar<float>();

    public MatchState State => _state.Value;
    public float RoundTimeRemaining => _roundTimeRemaining.Value;

    private NetworkManager _nm;

    private readonly Dictionary<int, NetworkObject> _spawnedByConnId = new Dictionary<int, NetworkObject>(32);
    private readonly HashSet<int> _aliveConnIds = new HashSet<int>();
    private readonly HashSet<int> _pendingSpawnConnIds = new HashSet<int>();

    private Coroutine _roundRoutine;

    private void Awake()
    {
        _nm = InstanceFinder.NetworkManager;
        if (_nm == null)
            Debug.LogError("KnockbackArenaGameManager: NetworkManager not found.");
    }

    private void OnEnable()
    {
        if (_nm == null) return;
        _nm.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        _nm.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
    }

    private void OnDisable()
    {
        if (_nm == null) return;
        _nm.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        _nm.ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _state.Value = MatchState.WaitingForPlayers;
        _roundTimeRemaining.Value = 0f;
        TryStartRoundIfReady();
    }

    private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            _spawnedByConnId.Clear();
            _aliveConnIds.Clear();
            _pendingSpawnConnIds.Clear();
            _state.Value = MatchState.WaitingForPlayers;
            _roundTimeRemaining.Value = 0f;
        }
    }

    private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (_nm == null || !_nm.IsServerStarted) return;

        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            // If a round is active, they must wait.
            if (_state.Value == MatchState.InRound)
            {
                _pendingSpawnConnIds.Add(conn.ClientId);
                return;
            }

            _pendingSpawnConnIds.Add(conn.ClientId);
            TryStartRoundIfReady();
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            _pendingSpawnConnIds.Remove(conn.ClientId);
            _aliveConnIds.Remove(conn.ClientId);

            if (despawnOnDisconnect)
                DespawnFor(conn);

            if (_state.Value == MatchState.InRound)
                EvaluateRoundEndCondition();
            else
                TryStartRoundIfReady();
        }
    }

    [Server]
    private void TryStartRoundIfReady()
    {
        if (_state.Value == MatchState.InRound || _state.Value == MatchState.RoundEnd)
            return;

        int connectedCount = GetAuthenticatedClientCount();
        if (connectedCount < minPlayersToStart)
        {
            _state.Value = MatchState.WaitingForPlayers;
            _roundTimeRemaining.Value = 0f;
            return;
        }

        if (_roundRoutine != null)
            StopCoroutine(_roundRoutine);

        _roundRoutine = StartCoroutine(RoundLoop());
    }

    [Server]
    private IEnumerator RoundLoop()
    {
        _state.Value = MatchState.InRound;

        // Spawn pending.
        SpawnPendingConnectionsIntoRound();

        // Alive set = everyone spawned.
        _aliveConnIds.Clear();
        foreach (var kvp in _spawnedByConnId)
            _aliveConnIds.Add(kvp.Key);

        _roundTimeRemaining.Value = roundDurationSeconds;

        while (_roundTimeRemaining.Value > 0f && _state.Value == MatchState.InRound)
        {
            _roundTimeRemaining.Value -= Time.deltaTime;
            EvaluateRoundEndCondition();
            yield return null;
        }

        _state.Value = MatchState.RoundEnd;
        _roundTimeRemaining.Value = 0f;

        yield return new WaitForSeconds(roundEndDelaySeconds);

        // End round: despawn all players.
        DespawnAllPlayers();

        _state.Value = MatchState.WaitingForPlayers;

        // Everyone connected goes into next round.
        CacheAllConnectedAsPending();
        TryStartRoundIfReady();

        _roundRoutine = null;
    }

    [Server]
    private void EvaluateRoundEndCondition()
    {
        if (_aliveConnIds.Count <= 1)
            _roundTimeRemaining.Value = 0f;
    }

    [Server]
    private void SpawnPendingConnectionsIntoRound()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("KnockbackArenaGameManager: playerPrefab is null.");
            return;
        }

        var pendingList = _pendingSpawnConnIds.ToList();
        foreach (int clientId in pendingList)
        {
            NetworkConnection conn = TryGetConn(clientId);
            if (conn == null)
            {
                _pendingSpawnConnIds.Remove(clientId);
                continue;
            }

            if (_spawnedByConnId.ContainsKey(clientId))
            {
                _pendingSpawnConnIds.Remove(clientId);
                continue;
            }

            SpawnFor(conn);
            _pendingSpawnConnIds.Remove(clientId);
        }
    }

    [Server]
    private void CacheAllConnectedAsPending()
    {
        if (_nm?.ServerManager == null) return;

        foreach (var kvp in _nm.ServerManager.Clients)
        {
            int clientId = kvp.Key;
            if (!_spawnedByConnId.ContainsKey(clientId))
                _pendingSpawnConnIds.Add(clientId);
        }
    }

    [Server]
    private void SpawnFor(NetworkConnection conn)
    {
        Transform sp = GetSpawnPoint(conn.ClientId);
        Vector3 pos = sp != null ? sp.position : Vector3.zero;
        Quaternion rot = sp != null ? sp.rotation : Quaternion.identity;

        NetworkObject nob = Instantiate(playerPrefab, pos, rot);
        _nm.ServerManager.Spawn(nob, conn);
        _spawnedByConnId[conn.ClientId] = nob;

        // Just in case (shouldn't matter because it's a fresh spawn).
        if (nob.TryGetComponent<BoxerEliminationState>(out var elim))
            elim.ServerRestoreForRound();
    }

    [Server]
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
    }

    [Server]
    private void DespawnAllPlayers()
    {
        foreach (var kvp in _spawnedByConnId.ToList())
        {
            if (kvp.Value != null)
                _nm.ServerManager.Despawn(kvp.Value);
        }

        _spawnedByConnId.Clear();
        _aliveConnIds.Clear();
    }

    private Transform GetSpawnPoint(int clientId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        int idx = Mathf.Abs(clientId) % spawnPoints.Length;
        return spawnPoints[idx];
    }

    private int GetAuthenticatedClientCount()
    {
        if (_nm?.ServerManager == null) return 0;
        return _nm.ServerManager.Clients.Count;
    }

    private NetworkConnection TryGetConn(int clientId)
    {
        if (_nm?.ServerManager == null) return null;
        _nm.ServerManager.Clients.TryGetValue(clientId, out var conn);
        return conn;
    }

    // =========================
    // KO event entrypoint
    // =========================

    [Server]
    public void ServerOnPlayerKnockedOut(BoxerKnockoutTrigger victimTrigger)
    {
        if (_state.Value != MatchState.InRound)
            return;

        if (victimTrigger == null)
            return;

        var victimNob = victimTrigger.GetComponent<NetworkObject>();
        if (victimNob == null || victimNob.Owner == null)
            return;

        int victimConnId = victimNob.Owner.ClientId;

        if (!_aliveConnIds.Contains(victimConnId))
            return;

        // Determine credited attacker from last-hit tracking.
        int attackerConnId = -1;
        var elim = victimTrigger.GetComponent<BoxerEliminationState>();
        if (elim != null)
            attackerConnId = elim.ServerGetCreditedAttackerOrMinusOne();

        // Eliminate victim.
        _aliveConnIds.Remove(victimConnId);

        if (spectatorPoint != null && elim != null)
        {
            elim.ServerEliminateToSpectator(spectatorPoint.position, spectatorPoint.rotation);
        }

        // Award attacker KO + stamina refill.
        if (attackerConnId >= 0 && _spawnedByConnId.TryGetValue(attackerConnId, out var attackerObj) && attackerObj != null)
        {
            // KO counter (if you have a PlayerIdentity, use that; otherwise ignore).
            var pid = attackerObj.GetComponent<PlayerIdentity>();
            if (pid != null)
                pid.ServerAddKnockout(1);

            // Stamina refill (typed call unknown, so SendMessage safely).
            attackerObj.gameObject.SendMessage("RefillToMax", SendMessageOptions.DontRequireReceiver);
            attackerObj.gameObject.SendMessage("SetToMax", SendMessageOptions.DontRequireReceiver);
            attackerObj.gameObject.SendMessage("RefreshToMax", SendMessageOptions.DontRequireReceiver);
        }

        EvaluateRoundEndCondition();
    }
}
