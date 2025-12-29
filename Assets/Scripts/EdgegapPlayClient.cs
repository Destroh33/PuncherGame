using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EdgegapPlayClient : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string backendBaseUrl = "https://edgegap-backend.vercel.app";
    [SerializeField] private float pollIntervalSeconds = 1.5f;
    [SerializeField] private float overallTimeoutSeconds = 120f;

    [Header("Connect")]
    [Tooltip("How long to wait for FishNet to report Started after we begin connecting.")]
    [SerializeField] private float connectTimeoutSeconds = 10f;

    [Header("Auto Re-Click Play")]
    [Tooltip("After the server becomes READY, if we don't connect, we will literally call OnPlayPressed() again.")]
    [SerializeField] private bool autoRepressPlayOnce = true;

    [Tooltip("Delay before we auto-call OnPlayPressed() again.")]
    [SerializeField] private float autoRepressDelaySeconds = 1.0f;

    [Header("UI")]
    [SerializeField] private GameObject canvasRoot;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusText;

    [Header("FishNet")]
    [SerializeField] private NetworkManager networkManager;

    private Coroutine _routine;
    private bool _busy;

    private bool _connectSucceeded;
    private bool _autoRepressedAlready;

    private void OnEnable()
    {
        if (networkManager != null && networkManager.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
    }

    private void OnDisable()
    {
        if (networkManager != null && networkManager.ClientManager != null)
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        Debug.Log($"[EdgegapPlayClient] ClientConnectionState: {args.ConnectionState}");
        if (args.ConnectionState == LocalConnectionState.Started)
            _connectSucceeded = true;
    }

    // Hook this to your Play button OnClick.
    public void OnPlayPressed()
    {
        if (_busy) return;

        if (networkManager == null)
        {
            SetStatus("Missing NetworkManager.");
            return;
        }

        if (statusText == null)
        {
            Debug.LogError("[EdgegapPlayClient] Missing statusText.");
            return;
        }

        if (string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            SetStatus("Missing backendBaseUrl.");
            return;
        }

        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        _busy = true;
        _connectSucceeded = false;

        // Only reset this when user manually initiates (first ever press).
        // If you want it to reset on each manual click, move reset into OnPlayPressed and detect manual.
        if (!_autoRepressedAlready)
        {
            // keep as-is
        }

        if (playButton != null)
            playButton.interactable = false;

        string playUrl = CombineUrl(backendBaseUrl, "/api/play");

        string requestId = null;
        string host = null;
        int port = 0;

        SetStatus("Finding server...");

        // 1) /api/play
        yield return PostJson(playUrl, "{}", (ok, bodyOrErr) =>
        {
            if (!ok)
            {
                Debug.LogError($"[EdgegapPlayClient] /api/play failed: {bodyOrErr}");
                return;
            }

            try
            {
                var pr = JsonUtility.FromJson<PlayResponse>(bodyOrErr);
                requestId = pr.request_id;
                host = pr.host;
                port = pr.port;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EdgegapPlayClient] /api/play JSON parse error: {e.Message}\n{bodyOrErr}");
            }
        });

        if (string.IsNullOrWhiteSpace(requestId))
        {
            FinishFail("Backend play failed.");
            yield break;
        }

        // If /play returned host/port, connect now.
        if (!string.IsNullOrWhiteSpace(host) && port > 0)
        {
            yield return ConnectThenMaybeRepress(host, (ushort)port);
            yield break;
        }

        // 2) Poll /api/status until host/port exists
        string statusUrl = CombineUrl(backendBaseUrl, $"/api/status?request_id={Uri.EscapeDataString(requestId)}");

        float started = Time.time;
        while (Time.time - started < overallTimeoutSeconds)
        {
            yield return Get(statusUrl, (ok, bodyOrErr) =>
            {
                if (!ok)
                {
                    Debug.LogWarning($"[EdgegapPlayClient] /api/status transient error: {bodyOrErr}");
                    return;
                }

                try
                {
                    var sr = JsonUtility.FromJson<StatusResponse>(bodyOrErr);

                    if (!string.IsNullOrWhiteSpace(sr.status))
                        SetStatus($"Server: {sr.status}");

                    if (!string.IsNullOrWhiteSpace(sr.host) && sr.port > 0)
                    {
                        host = sr.host;
                        port = sr.port;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EdgegapPlayClient] /api/status JSON parse error: {e.Message}\n{bodyOrErr}");
                }
            });

            if (!string.IsNullOrWhiteSpace(host) && port > 0)
            {
                yield return ConnectThenMaybeRepress(host, (ushort)port);
                yield break;
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        FinishFail("Timed out starting server.");
    }

    private IEnumerator ConnectThenMaybeRepress(string host, ushort port)
    {
        SetStatus($"Connecting to {host}:{port}...");

        // Start connection
        bool ok = networkManager.ClientManager.StartConnection(host, port);
        if (!ok)
            Debug.LogWarning("[EdgegapPlayClient] StartConnection returned false.");

        // Wait up to connectTimeoutSeconds for Started
        float t0 = Time.time;
        while (Time.time - t0 < connectTimeoutSeconds)
        {
            if (_connectSucceeded)
            {
                SetStatus("Connected!");
                if (canvasRoot != null)
                    canvasRoot.SetActive(false);

                FinishSuccess();
                yield break;
            }

            yield return null;
        }

        // If we didn't connect in time, literally "press Play again" once.
        if (autoRepressPlayOnce && !_autoRepressedAlready)
        {
            _autoRepressedAlready = true;

            SetStatus("Server warmed. Retrying Play...");

            // Stop the current routine cleanly before re-invoking.
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _busy = false; // allow OnPlayPressed to run
            yield return new WaitForSeconds(autoRepressDelaySeconds);

            OnPlayPressed();
            yield break;
        }

        FinishFail("Connect timed out. Try again.");
    }

    private void FinishSuccess()
    {
        _routine = null;
        _busy = false;
        // keep play disabled; you're in-game
    }

    private void FinishFail(string msg)
    {
        _routine = null;
        _busy = false;
        if (playButton != null)
            playButton.interactable = true;
        SetStatus(msg);
    }

    private void SetStatus(string s)
    {
        if (statusText != null)
            statusText.text = s;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (string.IsNullOrEmpty(baseUrl)) return path ?? "";
        if (string.IsNullOrEmpty(path)) return baseUrl;

        baseUrl = baseUrl.TrimEnd('/');
        path = path.StartsWith("/") ? path : "/" + path;
        return baseUrl + path;
    }

    private IEnumerator PostJson(string url, string jsonBody, Action<bool, string> onDone)
    {
        using var req = new UnityWebRequest(url, "POST");
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(jsonBody ?? "{}");
        req.uploadHandler = new UploadHandlerRaw(payload);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onDone?.Invoke(false, $"{req.responseCode} {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        onDone?.Invoke(true, req.downloadHandler.text);
    }

    private IEnumerator Get(string url, Action<bool, string> onDone)
    {
        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onDone?.Invoke(false, $"{req.responseCode} {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        onDone?.Invoke(true, req.downloadHandler.text);
    }

    [Serializable]
    private class PlayResponse
    {
        public bool reused;
        public string request_id;
        public string status;
        public string host;
        public int port;
    }

    [Serializable]
    private class StatusResponse
    {
        public string request_id;
        public string status;
        public string host;
        public int port;
    }

    // Optional: call this when you disconnect / return to menu so auto-press can happen again.
    public void ResetAutoRepress()
    {
        _autoRepressedAlready = false;
    }
}
