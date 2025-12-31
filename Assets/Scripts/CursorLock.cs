using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CursorLockWithUI : MonoBehaviour
{
    [SerializeField] private bool lockOnStart = false;

    void Start()
    {
        if (lockOnStart) LockCursor();
        else UnlockCursor();
    }

    void Update()
    {
        // ESC always unlocks so you can use UI
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            UnlockCursor();

        // Only lock when you click AND you are NOT clicking UI
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!IsPointerOverUI())
                LockCursor();
        }
    }

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
