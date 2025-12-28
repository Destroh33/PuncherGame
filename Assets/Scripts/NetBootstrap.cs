using System;
using System.Threading;
using UnityEngine;
using FishNet;

public class NetBootstrap : MonoBehaviour
{
    [Header("Editor Transport")]
    [SerializeField] private ushort port = 7770;

    [Header("Role Selection")]
    [SerializeField] private string projectMutexName = "PuncherGame_FishNetHost";
    [SerializeField] private float clientConnectDelaySeconds = 0.25f;

    private static Mutex _mutex;
    private static bool _ownsMutex;

    private bool _started;
    private int _pid;

    private void Awake()
    {
        _pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        DontDestroyOnLoad(gameObject);

        Debug.Log($"[NetBootstrap][PID {_pid}] Awake()");
    }

    private void Start()
    {
        Debug.Log($"[NetBootstrap][PID {_pid}] Start()");
        AutoStartNoConfig();
    }

    private void AutoStartNoConfig()
    {
        if (_started)
            return;

        _started = true;

        if (InstanceFinder.NetworkManager == null)
        {
            Debug.LogError($"[NetBootstrap][PID {_pid}] ❌ No NetworkManager found");
            return;
        }

        var nm = InstanceFinder.NetworkManager;

        // Tugboat setup (editor)
        var tugboat = nm.GetComponent<FishNet.Transporting.Tugboat.Tugboat>();
        if (tugboat != null)
        {
            tugboat.SetPort(port);
            Debug.Log($"[NetBootstrap][PID {_pid}] Tugboat port set to {port}");
        }
        else
        {
            Debug.LogWarning($"[NetBootstrap][PID {_pid}] Tugboat not found (is it selected?)");
        }

        bool isHost = TryAcquireHostMutex();

        if (isHost)
        {
            Debug.Log($"[NetBootstrap][PID {_pid}] 🟢 I am HOST (mutex acquired)");
            Debug.Log($"[NetBootstrap][PID {_pid}] Starting Server");
            nm.ServerManager.StartConnection();

            Debug.Log($"[NetBootstrap][PID {_pid}] Starting Client (host)");
            nm.ClientManager.StartConnection();
        }
        else
        {
            Debug.Log($"[NetBootstrap][PID {_pid}] 🔵 I am CLIENT (mutex already owned)");
            Debug.Log($"[NetBootstrap][PID {_pid}] Will start client in {clientConnectDelaySeconds:0.00}s");

            Invoke(nameof(StartClientOnly), clientConnectDelaySeconds);
        }
    }

    private void StartClientOnly()
    {
        Debug.Log($"[NetBootstrap][PID {_pid}] Starting Client");
        InstanceFinder.NetworkManager.ClientManager.StartConnection();
    }

    private bool TryAcquireHostMutex()
    {
        string name = @"Global\" + projectMutexName;

        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: name, createdNew: out bool createdNew);
            _ownsMutex = true;

            Debug.Log($"[NetBootstrap][PID {_pid}] Mutex '{name}' acquired (createdNew={createdNew})");
            return true;
        }
        catch (Exception e)
        {
            Debug.Log($"[NetBootstrap][PID {_pid}] Mutex '{name}' NOT acquired → client\n{e.Message}");
            _ownsMutex = false;
            return false;
        }
    }

    private void OnApplicationQuit()
    {
        ReleaseMutex();
    }

    private void OnDestroy()
    {
        ReleaseMutex();
    }

    private void ReleaseMutex()
    {
        try
        {
            if (_mutex != null)
            {
                if (_ownsMutex)
                {
                    Debug.Log($"[NetBootstrap][PID {_pid}] Releasing mutex");
                    _mutex.ReleaseMutex();
                }

                _mutex.Dispose();
                _mutex = null;
                _ownsMutex = false;
            }
        }
        catch
        {
            // Ignore shutdown edge cases
        }
    }
}
