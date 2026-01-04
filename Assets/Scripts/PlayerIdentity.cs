using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class PlayerIdentity : NetworkBehaviour
{
    [Header("Name")]
    [SerializeField] private string playerNamePrefsKey = "PLAYER_DISPLAY_NAME";
    [SerializeField] private int maxNameLength = 16;
    [SerializeField] private TMP_Text nameTag;

    private readonly SyncVar<string> _displayName = new SyncVar<string>();
    private readonly SyncVar<int> _knockouts = new SyncVar<int>();

    private string _lastAppliedName = "";

    public string DisplayName =>
        string.IsNullOrWhiteSpace(_displayName.Value)
            ? $"Player {OwnerId}"
            : _displayName.Value;

    public int Knockouts => _knockouts.Value;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            if (nameTag != null)
                nameTag.gameObject.SetActive(false);

            string n = PlayerPrefs.GetString(playerNamePrefsKey, "").Trim();
            if (!string.IsNullOrWhiteSpace(n))
                ServerSetName(n);
        }
        else
        {
            if (nameTag != null)
                nameTag.gameObject.SetActive(true);
        }

        ApplyNameTagIfChanged();
    }

    private void Update()
    {
        ApplyNameTagIfChanged();
    }

    [ServerRpc(RequireOwnership = true)]
    private void ServerSetName(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.Length > maxNameLength)
            name = name.Substring(0, maxNameLength);

        _displayName.Value = name;
    }

    private void ApplyNameTagIfChanged()
    {
        if (nameTag == null) return;
        if (IsOwner) return;

        string current = DisplayName;
        if (current == _lastAppliedName) return;

        _lastAppliedName = current;
        nameTag.text = current;
    }

    [Server]
    public void ServerAddKnockout(int amount = 1)
    {
        _knockouts.Value = Mathf.Max(0, _knockouts.Value + amount);
    }
}
