using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class BoxerInputClient : NetworkBehaviour
{
    [SerializeField] private float inputLerpSpeed = 18f;

    private InputSystem_Actions _actions;
    private Vector2 _moveRaw;
    private Vector2 _moveSmoothed;
    private Vector2 _look;
    private bool _punchHeld;
    private bool _blockHeld;
    private bool _dashPressed;
    private bool _kickPressed;

    private BoxerCommandBuffer _cmd;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _cmd = GetComponent<BoxerCommandBuffer>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (base.Owner != null && base.Owner.IsLocalClient)
            EnableInput();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        DisableInput();
    }

    private void Update()
    {
        if (base.Owner == null || !base.Owner.IsLocalClient || _cmd == null)
            return;

        float t = 1f - Mathf.Exp(-inputLerpSpeed * Time.deltaTime);
        _moveSmoothed = Vector2.Lerp(_moveSmoothed, _moveRaw, t);

        _cmd.SubmitInputServerRpc(
            _moveSmoothed,
            _look.x,
            _punchHeld,
            _blockHeld,
            _dashPressed,
            _kickPressed
        );

        _dashPressed = false;
        _kickPressed = false;
        _look = Vector2.zero;
    }

    private void EnableInput()
    {
        if (_actions != null) return;

        _actions = new InputSystem_Actions();
        _actions.Enable();

        _actions.Player.Move.performed += OnMove;
        _actions.Player.Move.canceled += OnMove;

        _actions.Player.Look.performed += OnLook;
        _actions.Player.Look.canceled += OnLook;

        _actions.Player.Jump.performed += OnJump;

        _actions.Player.Punch.started += OnPunch;
        _actions.Player.Punch.canceled += OnPunch;

        _actions.Player.Block.started += OnBlock;
        _actions.Player.Block.canceled += OnBlock;

        _actions.Player.Kick.started += OnKick;
    }

    private void DisableInput()
    {
        if (_actions == null) return;

        _actions.Player.Move.performed -= OnMove;
        _actions.Player.Move.canceled -= OnMove;

        _actions.Player.Look.performed -= OnLook;
        _actions.Player.Look.canceled -= OnLook;

        _actions.Player.Jump.performed -= OnJump;

        _actions.Player.Punch.started -= OnPunch;
        _actions.Player.Punch.canceled -= OnPunch;

        _actions.Player.Block.started -= OnBlock;
        _actions.Player.Block.canceled -= OnBlock;

        _actions.Player.Kick.started -= OnKick;

        _actions.Disable();
        _actions = null;
    }

    private void OnMove(InputAction.CallbackContext ctx) => _moveRaw = ctx.ReadValue<Vector2>();
    private void OnLook(InputAction.CallbackContext ctx) => _look = ctx.ReadValue<Vector2>();
    private void OnJump(InputAction.CallbackContext ctx) => _dashPressed = true;
    private void OnPunch(InputAction.CallbackContext ctx) => _punchHeld = ctx.phase != InputActionPhase.Canceled;
    private void OnBlock(InputAction.CallbackContext ctx) => _blockHeld = ctx.phase != InputActionPhase.Canceled;
    private void OnKick(InputAction.CallbackContext ctx) => _kickPressed = true;
}
