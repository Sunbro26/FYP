using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject deathScreenPanel; // The panel with "YOU DIED" or "VICTORY"
    public TMP_Text resultText;         // The text component to change message

    [Header("Settings")]
    public float restartDelay = 3.0f;

    // Singleton instance for easy access
    public static GameManager Instance;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        if (deathScreenPanel != null) deathScreenPanel.SetActive(false);
    }

    public void TriggerGameOver(bool playerWon)
    {
        StartCoroutine(GameOverSequence(playerWon));
    }

    private IEnumerator GameOverSequence(bool playerWon)
    {
        // 1. Slow down time for dramatic effect
        Time.timeScale = 0.5f;
        
        // 2. Show UI
        if (deathScreenPanel != null)
        {
            deathScreenPanel.SetActive(true);
            if (resultText != null)
            {
                resultText.text = playerWon ? "VICTORY" : "YOU DIED";
                resultText.color = playerWon ? Color.yellow : Color.red;
            }
        }

        // 3. Wait (Realtime, ignoring timeScale)
        yield return new WaitForSeconds(restartDelay);

        // 4. Reset Game State (Simulation Mode)
        ResetSimulation();
    }

    private void ResetSimulation()
    {
        Time.timeScale = 1.0f;
        if (deathScreenPanel != null) deathScreenPanel.SetActive(false);

        // Find all CharacterStats and reset them
        CharacterStats[] allStats = FindObjectsByType<CharacterStats>(FindObjectsSortMode.None);
        foreach (var stat in allStats)
        {
            stat.ResetStats();
        }
        
        // Optional: Reset positions here if you want spawn points
        // ResetPositions(); 
    }
}