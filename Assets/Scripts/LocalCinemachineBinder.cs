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

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.Owner == null || !base.Owner.IsLocalClient)
            return;

        if (followTarget == null) followTarget = transform;
        if (lookAtTarget == null) lookAtTarget = transform;

        Bind();
    }

    private void Bind()
    {
        GameObject vcamGO = virtualCameraObject;

        if (vcamGO == null)
        {
            // Try common names first (fast path).
            var byName = GameObject.Find("CM vcam1") ?? GameObject.Find("Virtual Camera") ?? GameObject.Find("PlayerVcam");
            if (byName != null) vcamGO = byName;

            // Fallback: find any object with a component that has Follow/LookAt props.
            if (vcamGO == null)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb == null) continue;
                    if (TrySetFollowLookAt(mb, followTarget, lookAtTarget, dryRun: true))
                    {
                        vcamGO = mb.gameObject;
                        break;
                    }
                }
            }
        }

        if (vcamGO == null)
        {
            Debug.LogWarning("LocalCinemachineBinder: Could not find a virtual camera to bind.");
            return;
        }

        bool boundAny = false;
        foreach (var mb in vcamGO.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (TrySetFollowLookAt(mb, followTarget, lookAtTarget, dryRun: false))
                boundAny = true;
        }

        if (!boundAny)
            Debug.LogWarning($"LocalCinemachineBinder: Found '{vcamGO.name}' but did not find a component with Follow/LookAt properties.");
    }

    private bool TrySetFollowLookAt(MonoBehaviour component, Transform follow, Transform lookAt, bool dryRun)
    {
        var t = component.GetType();

        // Many Cinemachine components expose public properties "Follow" and "LookAt".
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

        Debug.Log($"LocalCinemachineBinder: Bound vcam '{component.gameObject.name}' ({t.Name}) Follow={follow.name} LookAt={lookAt.name}");
        return true;
    }
}
