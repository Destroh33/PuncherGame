using UnityEngine;

public class BoxerAnimatorView : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Header("Animator Params")]
    [SerializeField] private string runForwardParam = "forward";
    [SerializeField] private string runLeftParam = "left";
    [SerializeField] private string punchingParam = "punching";
    [SerializeField] private string blockingParam = "blocking";
    [SerializeField] private string kickTriggerParam = "kick";
    [SerializeField] private string runSpeedMultParam = "runspeed";

    [Header("Animator Layer Control")]
    [SerializeField] private string handsLayerName = "HandsLayer";

    [Header("Run State (for HandsLayer weight)")]
    [SerializeField] private string runBlendStateName = "RunBlend";

    private int _handsLayerIndex = -1;
    private BoxerCommandBuffer _cmd;

    private int _lastKickSeq = int.MinValue;

    private void Awake()
    {
        _cmd = GetComponent<BoxerCommandBuffer>();
        if (animator != null)
            _handsLayerIndex = animator.GetLayerIndex(handsLayerName);
    }

    private void Update()
    {
        if (animator == null || _cmd == null)
            return;

        int kickSeq = _cmd.NetKickSeq.Value;
        if (kickSeq != _lastKickSeq)
        {
            _lastKickSeq = kickSeq;
            if (kickSeq != 0 && !string.IsNullOrEmpty(kickTriggerParam))
            {
                animator.ResetTrigger(kickTriggerParam);
                animator.SetTrigger(kickTriggerParam);
            }
        }

        animator.SetFloat(runForwardParam, _cmd.NetRunForward.Value);
        animator.SetFloat(runLeftParam, _cmd.NetRunLeft.Value);
        animator.SetBool(punchingParam, _cmd.NetPunching.Value);
        animator.SetBool(blockingParam, _cmd.NetBlocking.Value);

        if (!string.IsNullOrEmpty(runSpeedMultParam))
            animator.SetFloat(runSpeedMultParam, _cmd.NetRunSpeedMult.Value);

        UpdateHandsLayerWeight();
    }

    private void UpdateHandsLayerWeight()
    {
        if (_handsLayerIndex < 0) return;

        if (IsBaseLayerInOrTransitioning(runBlendStateName))
        {
            if (!Mathf.Approximately(animator.GetLayerWeight(_handsLayerIndex), 1f))
                animator.SetLayerWeight(_handsLayerIndex, 1f);
        }
    }

    private bool IsBaseLayerInOrTransitioning(string stateName)
    {
        if (string.IsNullOrEmpty(stateName)) return false;

        var cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsName(stateName))
            return true;

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsName(stateName))
                return true;
        }

        return false;
    }
}
