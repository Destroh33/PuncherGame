using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class KnockbackArenaHUD : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text topLeftNameText;
    [SerializeField] private TMP_Text topMiddleTimerText;
    [SerializeField] private TMP_Text rightLeaderboardText;

    [Header("Prefs")]
    [SerializeField] private string playerNamePrefsKey = "PLAYER_DISPLAY_NAME";

    [Header("Find")]
    [SerializeField] private GameManager gameManager;

    [Header("Refresh")]
    [SerializeField] private float leaderboardRefreshHz = 6f;

    private float _nextLbTime;

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
    }

    private void Update()
    {
        // Top-left: local saved name (what you typed before Play)
        if (topLeftNameText != null)
        {
            string myName = PlayerPrefs.GetString(playerNamePrefsKey, "").Trim();
            topLeftNameText.text = string.IsNullOrWhiteSpace(myName) ? "Unnamed" : myName;
        }

        // Top-middle: networked round timer from GameManager
        if (topMiddleTimerText != null && gameManager != null)
        {
            float t = Mathf.Max(0f, gameManager.RoundTimeRemaining);
            int secs = Mathf.CeilToInt(t);
            int m = secs / 60;
            int s = secs % 60;
            topMiddleTimerText.text = $"{m:0}:{s:00}";
        }

        // Right: leaderboard (don’t rebuild every frame)
        if (rightLeaderboardText != null && Time.unscaledTime >= _nextLbTime)
        {
            _nextLbTime = Time.unscaledTime + (1f / Mathf.Max(0.1f, leaderboardRefreshHz));
            rightLeaderboardText.text = BuildLeaderboardText();
        }
    }

    private string BuildLeaderboardText()
    {
        var players = FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None)
            .OrderByDescending(p => p.Knockouts)
            .ThenBy(p => p.DisplayName);

        var sb = new StringBuilder(256);
        sb.AppendLine("LEADERBOARD");

        int rank = 1;
        foreach (var p in players)
        {
            sb.Append(rank).Append(". ")
              .Append(p.DisplayName)
              .Append("  ")
              .Append(p.Knockouts)
              .AppendLine();
            rank++;
        }

        return sb.ToString();
    }
}
