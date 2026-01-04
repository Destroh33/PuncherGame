using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EdgegapPlayClient : MonoBehaviour
{
    [Header("Backend Configuration")]
    [SerializeField] private string backendBaseUrl = "https://edgegap-backend.vercel.app/";
    [SerializeField] private float pollInterval = 2.0f;

    [Header("UI References")]
    [SerializeField] private GameObject canvasRoot;
    [SerializeField] private Button startServerButton;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Name Gate")]
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private string playerNamePrefsKey = "PLAYER_DISPLAY_NAME";

    [Header("FishNet References")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Ready Delay")]
    [Tooltip("Seconds to wait (and countdown) after host/port appear before enabling Play.")]
    [SerializeField] private int readyCountdownSeconds = 5;

    private string _currentRequestId;
    private bool _isPolling;

    private Coroutine _readyCountdownRoutine;
    private bool _readyCountdownCompleted;

    private void Start()
    {
        playButton.interactable = false;
        playButton.gameObject.SetActive(true);
        startServerButton.interactable = true;

        startServerButton.onClick.AddListener(OnStartServerPressed);
        playButton.onClick.AddListener(OnPlayPressed);

        if (nameInputField != null)
        {
            nameInputField.text = PlayerPrefs.GetString(playerNamePrefsKey, "");
            nameInputField.onValueChanged.AddListener(_ => RefreshPlayInteractable());
        }

        SetStatus("Ready to Start");
        RefreshPlayInteractable();
    }

    private void OnEnable()
    {
        if (networkManager?.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
    }

    private void OnDisable()
    {
        if (networkManager?.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;

        if (nameInputField != null)
            nameInputField.onValueChanged.RemoveAllListeners();
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("<color=green>[Edgegap]</color> Connected successfully! Hiding UI.");
            if (canvasRoot) canvasRoot.SetActive(false);
            StopAllCoroutines();
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            if (canvasRoot) canvasRoot.SetActive(true);
            SetStatus("Disconnected");
            RefreshPlayInteractable();
        }
    }

    private void RefreshPlayInteractable()
    {
        bool hasName = HasValidName();
        bool canPlay = _readyCountdownCompleted && hasName;
        playButton.interactable = canPlay;
        playButton.gameObject.SetActive(_readyCountdownCompleted);
    }

    private bool HasValidName()
    {
        if (nameInputField == null) return true;
        return !string.IsNullOrWhiteSpace(nameInputField.text);
    }

    public void OnStartServerPressed()
    {
        if (_isPolling) return;

        startServerButton.interactable = false;
        playButton.interactable = false;
        playButton.gameObject.SetActive(false);
        _readyCountdownCompleted = false;

        if (_readyCountdownRoutine != null)
        {
            StopCoroutine(_readyCountdownRoutine);
            _readyCountdownRoutine = null;
        }

        StartCoroutine(StartServerFlow());
    }

    private IEnumerator StartServerFlow()
    {
        SetStatus("Requesting Server...");
        string url = CombineUrl(backendBaseUrl, "/api/play");

        yield return PostJson(url, "{}", (ok, res) =>
        {
            if (!ok)
            {
                SetStatus("Request Failed");
                startServerButton.interactable = true;
                return;
            }

            BackendResponse data = null;
            try { data = JsonUtility.FromJson<BackendResponse>(res); } catch { }

            if (data == null)
            {
                SetStatus("Backend Error");
                startServerButton.interactable = true;
                return;
            }

            _currentRequestId = data.request_id;

            if (!string.IsNullOrEmpty(data.host) && data.port > 0)
            {
                BeginReadyCountdownIfNeeded();
            }
            else
            {
                if (!_isPolling) StartCoroutine(PollStatusLoop());
            }
        });
    }

    private IEnumerator PollStatusLoop()
    {
        _isPolling = true;
        float timeout = 120f;
        float timer = 0f;

        while (timer < timeout)
        {
            string url = CombineUrl(backendBaseUrl, "/api/status?request_id=" + _currentRequestId);
            bool serverReady = false;

            yield return Get(url, (ok, res) =>
            {
                if (!ok) return;

                var data = JsonUtility.FromJson<BackendResponse>(res);

                if (!string.IsNullOrEmpty(data.host) && data.port > 0)
                {
                    serverReady = true;
                }
                else
                {
                    SetStatus($"Status: {data.status}");
                }
            });

            if (serverReady)
            {
                BeginReadyCountdownIfNeeded();
                break;
            }

            yield return new WaitForSeconds(pollInterval);
            timer += pollInterval;
        }

        if (timer >= timeout)
        {
            SetStatus("Timed Out. Try again.");
            startServerButton.interactable = true;
        }

        _isPolling = false;
    }

    private void BeginReadyCountdownIfNeeded()
    {
        if (_readyCountdownCompleted) return;
        if (_readyCountdownRoutine != null) return;

        playButton.interactable = false;
        playButton.gameObject.SetActive(false);

        _readyCountdownRoutine = StartCoroutine(ReadyCountdownRoutine());
    }

    private IEnumerator ReadyCountdownRoutine()
    {
        int t = Mathf.Max(0, readyCountdownSeconds);

        while (t > 0)
        {
            SetStatus($"Server found. Waiting {t}...");
            yield return new WaitForSeconds(1f);
            t--;
        }

        _readyCountdownCompleted = true;
        SetStatus("Server Ready! Enter name + Press Play.");
        RefreshPlayInteractable();

        _readyCountdownRoutine = null;
    }

    public void OnPlayPressed()
    {
        if (!_readyCountdownCompleted)
            return;

        if (!HasValidName())
        {
            SetStatus("Enter a name first.");
            RefreshPlayInteractable();
            return;
        }

        if (nameInputField != null)
            PlayerPrefs.SetString(playerNamePrefsKey, nameInputField.text.Trim());

        playButton.interactable = false;
        SetStatus("Refreshing Connection Info...");
        StartCoroutine(RefreshAndConnectSequence());
    }

    private IEnumerator RefreshAndConnectSequence()
    {
        string url = CombineUrl(backendBaseUrl, "/api/play");
        string freshHost = "";
        int freshPort = 0;
        bool requestSuccess = false;

        yield return PostJson(url, "{}", (ok, res) =>
        {
            if (ok)
            {
                var data = JsonUtility.FromJson<BackendResponse>(res);
                if (!string.IsNullOrEmpty(data.host) && data.port > 0)
                {
                    freshHost = data.host;
                    freshPort = data.port;
                    requestSuccess = true;
                }
            }
        });

        if (!requestSuccess)
        {
            SetStatus("Error: Server not found.");
            ResetToFindServerState("Connection Fail, Try Finding Server Again");
            yield break;
        }

        SetStatus($"Connecting to {freshHost}:{freshPort}...");
        yield return ConnectOnce(freshHost, (ushort)freshPort);
    }

    private IEnumerator ConnectOnce(string host, ushort port)
    {
        var transport = networkManager.TransportManager.Transport;
        string cleanHost = host.Replace("wss://", "").Replace("ws://", "")
                               .Replace("http://", "").Replace("https://", "").TrimEnd('/');

        transport.SetClientAddress(cleanHost);
        transport.SetPort(port);

        networkManager.ClientManager.StartConnection();

        yield return new WaitForSeconds(2.0f);

        if (networkManager.ClientManager.Connection.IsActive)
            yield break;

        networkManager.ClientManager.StopConnection();

        SetStatus("Connection Failed. Click Find Server to try again.");
        ResetToFindServerState("Find Server");
    }

    private void ResetToFindServerState(string status)
    {
        _readyCountdownCompleted = false;
        if (_readyCountdownRoutine != null)
        {
            StopCoroutine(_readyCountdownRoutine);
            _readyCountdownRoutine = null;
        }

        playButton.interactable = false;
        playButton.gameObject.SetActive(false);

        startServerButton.interactable = true;
        SetStatus(status);
    }

    private void SetStatus(string s)
    {
        if (statusText) statusText.text = s;
    }

    private IEnumerator PostJson(string url, string json, Action<bool, string> cb)
    {
        using var req = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        cb?.Invoke(req.result == UnityWebRequest.Result.Success, req.downloadHandler.text);
    }

    private IEnumerator Get(string url, Action<bool, string> cb)
    {
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        cb?.Invoke(req.result == UnityWebRequest.Result.Success, req.downloadHandler.text);
    }

    private string CombineUrl(string b, string p) => b.TrimEnd('/') + "/" + p.TrimStart('/');

    [Serializable]
    private class BackendResponse
    {
        public string request_id;
        public string host;
        public int port;
        public string status;
    }
}
