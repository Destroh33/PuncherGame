using System.Reflection;
using FishNet.Object;
using UnityEngine;

public class LocalCinemachineBinder : NetworkBehaviour
{
    [Header("Targets")]
    [Tooltip("If null, defaults to this transform.")]
    [SerializeField] private Transform followTarget;

    [Tooltip("If null, defaults to this transform.")]
    [SerializeField] private Transform lookAtTarget;

    [Header("Camera Find")]
    [Tooltip("Optional: assign your virtual camera object here. If null, we will search the scene.")]
    [SerializeField] private GameObject virtualCameraObject;

    private GameObject _vcamGO;
    private Transform _defaultFollow;
    private Transform _defaultLookAt;

    private Transform _spectatorAnchor; // local-only dummy transform

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.Owner == null || !base.Owner.IsLocalClient)
            return;

        if (followTarget == null) followTarget = transform;
        if (lookAtTarget == null) lookAtTarget = transform;

        _defaultFollow = followTarget;
        _defaultLookAt = lookAtTarget;

        BindDefaults();
    }

    private void BindDefaults()
    {
        _vcamGO = FindVcam();
        if (_vcamGO == null)
        {
            Debug.LogWarning("LocalCinemachineBinder: Could not find a virtual camera to bind.");
            return;
        }

        BindTo(_defaultFollow, _defaultLookAt);
    }

    private GameObject FindVcam()
    {
        if (virtualCameraObject != null)
            return virtualCameraObject;

        // Fast path by common names.
        var byName = GameObject.Find("CM vcam1") ?? GameObject.Find("Virtual Camera") ?? GameObject.Find("PlayerVcam");
        if (byName != null) return byName;

        // Fallback: find any MB with Follow/LookAt properties.
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            if (TrySetFollowLookAt(mb, followTarget, lookAtTarget, dryRun: true))
                return mb.gameObject;
        }

        return null;
    }

    private void BindTo(Transform follow, Transform lookAt)
    {
        if (_vcamGO == null) return;

        bool boundAny = false;
        foreach (var mb in _vcamGO.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (TrySetFollowLookAt(mb, follow, lookAt, dryRun: false))
                boundAny = true;
        }

        if (!boundAny)
            Debug.LogWarning($"LocalCinemachineBinder: Found '{_vcamGO.name}' but did not find a component with Follow/LookAt properties.");
    }

    private bool TrySetFollowLookAt(MonoBehaviour component, Transform follow, Transform lookAt, bool dryRun)
    {
        var t = component.GetType();

        var followProp = t.GetProperty("Follow", BindingFlags.Instance | BindingFlags.Public);
        var lookAtProp = t.GetProperty("LookAt", BindingFlags.Instance | BindingFlags.Public);

        bool ok = false;

        if (followProp != null && followProp.PropertyType == typeof(Transform) && followProp.CanWrite)
            ok = true;

        if (lookAtProp != null && lookAtProp.PropertyType == typeof(Transform) && lookAtProp.CanWrite)
            ok = true;

        if (!ok) return false;
        if (dryRun) return true;

        if (followProp != null && followProp.PropertyType == typeof(Transform) && followProp.CanWrite)
            followProp.SetValue(component, follow);

        if (lookAtProp != null && lookAtProp.PropertyType == typeof(Transform) && lookAtProp.CanWrite)
            lookAtProp.SetValue(component, lookAt);

        return true;
    }

    // =========================
    // Spectator mode (local-only)
    // =========================

    public void SetSpectatorFixed(Vector3 pos, Quaternion rot)
    {
        if (base.Owner == null || !base.Owner.IsLocalClient)
            return;

        if (_vcamGO == null)
            _vcamGO = FindVcam();

        if (_spectatorAnchor == null)
        {
            var go = new GameObject("SpectatorCamAnchor_LOCAL");
            _spectatorAnchor = go.transform;
        }

        _spectatorAnchor.SetPositionAndRotation(pos, rot);
        BindTo(_spectatorAnchor, _spectatorAnchor);
    }

    public void RestoreDefaultFollow()
    {
        if (base.Owner == null || !base.Owner.IsLocalClient)
            return;

        if (_vcamGO == null)
            _vcamGO = FindVcam();

        BindTo(_defaultFollow, _defaultLookAt);
    }
}
