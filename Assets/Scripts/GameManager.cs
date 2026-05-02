using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject deathScreenPanel;
    public TMP_Text resultText;

    [Header("Settings")]
    public float restartDelay = 3.0f;

    public static GameManager Instance;

    private Coroutine _gameOverRoutine;
    private bool _isResetting;
    private bool _suppressGameOverFlow;

    public bool IsResetting => _isResetting;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        Time.timeScale = 1.0f;
        _isResetting = false;
        _suppressGameOverFlow = false;

        if (deathScreenPanel != null) deathScreenPanel.SetActive(false);
    }

    public void SetTrainingResetOverride(bool suppress)
    {
        _suppressGameOverFlow = suppress;

        if (!suppress)
        {
            return;
        }

        if (_gameOverRoutine != null)
        {
            StopCoroutine(_gameOverRoutine);
            _gameOverRoutine = null;
        }

        Time.timeScale = 1.0f;
        _isResetting = false;
        if (deathScreenPanel != null) deathScreenPanel.SetActive(false);
    }

    public void TriggerGameOver(bool playerWon)
    {
        if (_suppressGameOverFlow || _isResetting) return;

        if (_gameOverRoutine != null)
        {
            StopCoroutine(_gameOverRoutine);
        }

        _gameOverRoutine = StartCoroutine(GameOverSequence(playerWon));
    }

    private IEnumerator GameOverSequence(bool playerWon)
    {
        _isResetting = true;
        Time.timeScale = 0.5f;

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
        // 3. Wait exactly 5 real seconds (6.0s * 0.5 timescale )
        yield return new WaitForSeconds(6.0f * Time.timeScale);

        ResetSimulation();
        _gameOverRoutine = null;
    }

    public void ResetSimulationImmediate()
    {
        if (_gameOverRoutine != null)
        {
            StopCoroutine(_gameOverRoutine);
            _gameOverRoutine = null;
        }

        ResetSimulation();
    }

    private void ResetSimulation()
    {
        Time.timeScale = 1.0f;
        if (deathScreenPanel != null) deathScreenPanel.SetActive(false);

        CharacterStats[] allStats = FindObjectsByType<CharacterStats>(FindObjectsSortMode.None);
        foreach (var stat in allStats)
        {
            stat.ResetStats();
        }

        _isResetting = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Time.timeScale = 1.0f;
    }
}
