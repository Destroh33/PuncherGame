using UnityEngine;

[DisallowMultipleComponent]
public class BoxerGroundGizmoClient : MonoBehaviour
{
    [Header("Ground Check Visual")]
    [SerializeField] private float groundCheckRadius = 0.1f;
    [SerializeField] private float groundCheckDistance = 0.05f;
    [SerializeField] private LayerMask groundMask = ~0;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Gizmos.DrawWireSphere(
            origin + Vector3.down * groundCheckDistance,
            groundCheckRadius
        );
    }
    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        return Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }
    private void Update()
    {
        if (IsGrounded())
            Debug.Log("Grounded");
    }
}
