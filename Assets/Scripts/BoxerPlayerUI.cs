using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BoxerPlayerUI : MonoBehaviour
{
    [Header("Sliders (0..1)")]
    [SerializeField] private Slider staminaSlider;
    [SerializeField] private Slider powerSlider;

    [Header("Binding")]
    [Tooltip("How often (seconds) to search for the local player if not bound yet, or if binding becomes invalid.")]
    [SerializeField] private float rebindIntervalSeconds = 0.5f;

    [Tooltip("If true, this HUD hides itself until it is bound to the local player.")]
    [SerializeField] private bool hideUntilBound = true;

    [Header("Optional (recommended)")]
    [SerializeField] private CanvasGroup canvasGroup;

    private BoxerResourcesServer _resources;
    private NetworkObject _boundPlayerNob;

    private float _nextRebindAt;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        InitSlider(staminaSlider);
        InitSlider(powerSlider);

        if (hideUntilBound)
            SetVisible(false);
    }

    private void Update()
    {
        if (!IsBindingValid())
        {
            if (Time.time >= _nextRebindAt)
            {
                _nextRebindAt = Time.time + Mathf.Max(0.05f, rebindIntervalSeconds);
                TryBindToLocalPlayer();
            }

            if (hideUntilBound)
                SetVisible(false);

            return;
        }

        if (hideUntilBound)
            SetVisible(true);

        float stamina01 = _resources.NetStamina01.Value;
        float power01 = _resources.NetPower01.Value;

        if (staminaSlider != null) staminaSlider.value = stamina01;
        if (powerSlider != null) powerSlider.value = power01;
    }

    private void TryBindToLocalPlayer()
    {
        _resources = null;
        _boundPlayerNob = null;

        // Find the local player's NetworkObject (owned by this client)
        // and bind to its BoxerResourcesServer.
        NetworkObject[] allNobs = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        for (int i = 0; i < allNobs.Length; i++)
        {
            NetworkObject nob = allNobs[i];
            if (nob == null) continue;
            if (nob.Owner == null) continue;
            if (!nob.Owner.IsLocalClient) continue;

            BoxerResourcesServer res = nob.GetComponent<BoxerResourcesServer>();
            if (res == null) continue;

            _boundPlayerNob = nob;
            _resources = res;

            // Snap UI immediately
            if (staminaSlider != null) staminaSlider.value = _resources.NetStamina01.Value;
            if (powerSlider != null) powerSlider.value = _resources.NetPower01.Value;

            if (hideUntilBound)
                SetVisible(true);

            return;
        }
    }

    private bool IsBindingValid()
    {
        if (_resources == null) return false;

        // If player got despawned/destroyed, this will be null or invalid.
        if (_boundPlayerNob == null)
            _boundPlayerNob = _resources.GetComponent<NetworkObject>();

        if (_boundPlayerNob == null) return false;
        if (_boundPlayerNob.Owner == null) return false;
        if (!_boundPlayerNob.Owner.IsLocalClient) return false;

        return true;
    }

    private void InitSlider(Slider s)
    {
        if (s == null) return;
        s.minValue = 0f;
        s.maxValue = 1f;
        s.wholeNumbers = false;
        s.value = 0f;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
