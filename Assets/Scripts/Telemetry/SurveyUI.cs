using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine.Networking;

/// <summary>
/// Timed experiment flow:
/// 1. Run Heuristic Only for 3 minutes.
/// 2. Show Survey 1 for Heuristic.
/// 3. Run Inference Only for 3 minutes.
/// 4. Show Survey 2 for Inference.
/// 5. End the game / experiment.
/// </summary>
public class SurveyUI : MonoBehaviour
{
    private const string LogPrefix = "[SurveyUI]";

    private enum ExperimentPhase
    {
        NotStarted,
        HeuristicGameplay,
        HeuristicSurvey,
        InferenceGameplay,
        InferenceSurvey,
        Complete
    }

    [Header("Experiment Flow")]
    [SerializeField] private bool startExperimentAutomatically = true;
    [SerializeField] private float gameplayDurationSeconds = 5f; // 3 minutes
    [SerializeField] private bool useRealtimeForGameplayTimer = false;

[Header("Cloud Logging")]
[SerializeField] private bool enableCloudLogging = true;
[SerializeField] private string cloudWebhookURL = "YOUR_GOOGLE_SCRIPT_URL_HERE";

    [Header("Root Panel")]
    [SerializeField] private GameObject surveyPanel;

    [Header("Optional End Screen")]
    [SerializeField] private GameObject experimentCompletePanel;
    [SerializeField] private TMP_Text experimentCompleteText;

    [Header("Q1 - Challenge Calibration")]
    [SerializeField] private Button[] q1Buttons;
    [SerializeField] private TMP_Text q1SelectionLabel;

    [Header("Q2 - Forced Style Change")]
    [SerializeField] private Button[] q2Buttons;
    [SerializeField] private TMP_Text q2SelectionLabel;

    [Header("Q3 - Flow State")]
    [SerializeField] private Button[] q3Buttons;
    [SerializeField] private TMP_Text q3SelectionLabel;

    [Header("Submit")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Close Settings")]
    [SerializeField] private float closeDelayAfterSubmit = 1.5f;

    [Header("FightLogger Reference - Optional")]
    [SerializeField] private FightLogger fightLogger;

    [Header("Enemy Mode Switcher")]
    [SerializeField] private EnemyControlModeSwitcher enemyModeSwitcher;

    private int _q1;
    private int _q2;
    private int _q3;
    private int _surveyNumber;

    private string _csvPath;
    private bool _isSubmitting;
    private bool _surveyClosed;
    private Coroutine _closeRoutine;
    private Coroutine _experimentRoutine;

    private ExperimentPhase _phase = ExperimentPhase.NotStarted;
    private string _currentSurveyAgentType = "Unknown";

    private const string CSV_HEADER =
        "survey_num,agent_type,persona," +
        "challenge_calibration_1to5,forced_style_change_1to5,flow_state_1to5";

    private void Awake()
    {
        _csvPath = Path.Combine(Application.persistentDataPath, "survey_log.csv");

        EnsureCsvExists();
        LoadSurveyNumber();

        HideSurveyImmediate();

        if (experimentCompletePanel != null)
            experimentCompletePanel.SetActive(false);

        Debug.Log($"{LogPrefix} Initialized.");
        Debug.Log($"{LogPrefix} CSV path: {_csvPath}");
        Debug.Log($"{LogPrefix} Starting from survey #{_surveyNumber + 1}");
        Debug.Log($"{LogPrefix} EnemyModeSwitcher assigned: {enemyModeSwitcher != null}");
        Debug.Log($"{LogPrefix} Gameplay duration per phase: {gameplayDurationSeconds} seconds.");
    }

    private void Start()
    {
        WireButtons(q1Buttons, value =>
        {
            _q1 = value;
            Debug.Log($"{LogPrefix} Q1 selected: {_q1}");
            UpdateLabels();
        });

        WireButtons(q2Buttons, value =>
        {
            _q2 = value;
            Debug.Log($"{LogPrefix} Q2 selected: {_q2}");
            UpdateLabels();
        });

        WireButtons(q3Buttons, value =>
        {
            _q3 = value;
            Debug.Log($"{LogPrefix} Q3 selected: {_q3}");
            UpdateLabels();
        });

        if (submitButton != null)
        {
            submitButton.onClick.AddListener(OnSubmit);
            Debug.Log($"{LogPrefix} Submit button wired.");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} Submit button is not assigned.");
        }

        if (startExperimentAutomatically)
        StartCoroutine(StartExperimentNextFrame());
    }

    private IEnumerator StartExperimentNextFrame()
{
    Debug.Log($"{LogPrefix} Waiting one frame before starting experiment.");

    // Allows SkeletonAI.Start(), ML-Agent initialization, Animator, NavMeshAgent, etc. to initialize.
    yield return null;

    StartExperiment();
}

    private void OnEnable()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void StartExperiment()
    {
        Debug.Log($"{LogPrefix} StartExperiment invoked.");

        if (_experimentRoutine != null)
        {
            StopCoroutine(_experimentRoutine);
            Debug.LogWarning($"{LogPrefix} Existing experiment routine stopped.");
        }

        _experimentRoutine = StartCoroutine(ExperimentFlowRoutine());
    }

    private IEnumerator ExperimentFlowRoutine()
{
    Debug.Log($"{LogPrefix} Experiment flow started.");

    yield return null;

    // ---------------- Phase 1: Heuristic ----------------
    _phase = ExperimentPhase.InferenceGameplay;
    SwitchEnemyToInference();

    if (fightLogger != null)
    {
        Debug.Log($"{LogPrefix} Starting FightLogger for HeuristicOnly.");
        fightLogger.StartLoggingSegment("HeuristicOnly");
    }
    else
    {
        Debug.LogWarning($"{LogPrefix} FightLogger is not assigned. Heuristic fight data will not be logged.");
    }

    Debug.Log($"{LogPrefix} Heuristic gameplay started for {gameplayDurationSeconds} seconds.");
    yield return WaitGameplayDuration();

    if (fightLogger != null)
    {
        Debug.Log($"{LogPrefix} Stopping FightLogger for HeuristicOnly.");
        fightLogger.StopLoggingSegment("TimedSurvey_Heuristic");
    }

    _phase = ExperimentPhase.HeuristicSurvey;
    ShowSurveyForAgent("HeuristicOnly");

    Debug.Log($"{LogPrefix} Waiting for Heuristic survey submission.");
    yield return new WaitUntil(() => _surveyClosed);

    // ---------------- Phase 2: Inference ----------------
    _phase = ExperimentPhase.InferenceGameplay;
    SwitchEnemyToInference();

    if (fightLogger != null)
    {
        Debug.Log($"{LogPrefix} Starting FightLogger for InferenceOnly.");
        fightLogger.StartLoggingSegment("InferenceOnly");
    }
    else
    {
        Debug.LogWarning($"{LogPrefix} FightLogger is not assigned. Inference fight data will not be logged.");
    }

    Debug.Log($"{LogPrefix} Inference gameplay started for {gameplayDurationSeconds} seconds.");
    yield return WaitGameplayDuration();

    if (fightLogger != null)
    {
        Debug.Log($"{LogPrefix} Stopping FightLogger for InferenceOnly.");
        fightLogger.StopLoggingSegment("TimedSurvey_Inference");
    }

    _phase = ExperimentPhase.InferenceSurvey;
    ShowSurveyForAgent("InferenceOnly");

    Debug.Log($"{LogPrefix} Waiting for Inference survey submission.");
    yield return new WaitUntil(() => _surveyClosed);

    EndExperiment();
}
    private IEnumerator WaitGameplayDuration()
    {
        if (useRealtimeForGameplayTimer)
        {
            yield return new WaitForSecondsRealtime(gameplayDurationSeconds);
        }
        else
        {
            yield return new WaitForSeconds(gameplayDurationSeconds);
        }
    }

    private void ShowSurveyForAgent(string agentType)
    {
        _currentSurveyAgentType = agentType;
        _surveyClosed = false;

        Debug.Log($"{LogPrefix} Showing survey for agent type: {_currentSurveyAgentType}");

        ShowSurvey();
    }

    public void ShowSurvey()
    {
        Debug.Log($"{LogPrefix} ShowSurvey invoked. Phase: {_phase}");

        if (_closeRoutine != null)
        {
            StopCoroutine(_closeRoutine);
            _closeRoutine = null;
            Debug.Log($"{LogPrefix} Existing close routine cancelled.");
        }

        _q1 = 0;
        _q2 = 0;
        _q3 = 0;
        _isSubmitting = false;
        _surveyClosed = false;

        UpdateLabels();
        SetSurveyInteractable(true);

        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);

        if (surveyPanel != null)
        {
            surveyPanel.SetActive(true);
            Debug.Log($"{LogPrefix} Survey panel shown.");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} surveyPanel is not assigned.");
        }

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log($"{LogPrefix} Game paused for survey. Cursor unlocked.");
    }

    public void OnSubmit()
    {
        Debug.Log($"{LogPrefix} Submit clicked. Current survey agent: {_currentSurveyAgentType}");

        if (_isSubmitting)
        {
            Debug.LogWarning($"{LogPrefix} Submit ignored because submission is already in progress.");
            return;
        }

        if (_q1 == 0 || _q2 == 0 || _q3 == 0)
        {
            Debug.LogWarning($"{LogPrefix} Submit blocked. Missing answers. Q1:{_q1}, Q2:{_q2}, Q3:{_q3}");
            ShowFeedback("Please answer all 3 questions.");
            return;
        }

        _isSubmitting = true;
        SetSurveyInteractable(false);

        bool saved = TrySaveRow();

        if (!saved)
        {
            _isSubmitting = false;
            SetSurveyInteractable(true);
            ShowFeedback("Could not save response. Check console.");
            return;
        }

        ShowFeedback("Response saved. Thank you!");

        Debug.Log($"{LogPrefix} Response saved. Closing survey after {closeDelayAfterSubmit} real seconds.");

        
        if (_closeRoutine != null)
            StopCoroutine(_closeRoutine);

        _closeRoutine = StartCoroutine(CloseSurveyAfterDelay());
    }

    private IEnumerator CloseSurveyAfterDelay()
    {
        yield return new WaitForSecondsRealtime(closeDelayAfterSubmit);
        CloseSurvey();
    }

    public void CloseSurvey()
    {
        Debug.Log($"{LogPrefix} CloseSurvey invoked. Phase: {_phase}");

        _closeRoutine = null;

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log($"{LogPrefix} Game resumed. Cursor locked.");

        if (surveyPanel != null)
        {
            surveyPanel.SetActive(false);
            Debug.Log($"{LogPrefix} Survey panel hidden.");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} surveyPanel is not assigned.");
        }

        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);

        _isSubmitting = false;
        _surveyClosed = true;

        Debug.Log($"{LogPrefix} Survey closed for agent type: {_currentSurveyAgentType}");
    }

    private void SwitchEnemyToHeuristic()
    {
        Debug.Log($"{LogPrefix} Switching enemy to Heuristic Only.");

        if (enemyModeSwitcher != null)
        {
            enemyModeSwitcher.SwitchToHeuristicOnly();
            Debug.Log($"{LogPrefix} Enemy switched to Heuristic Only.");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} enemyModeSwitcher is not assigned. Cannot switch to Heuristic.");
        }
    }

    private void SwitchEnemyToInference()
    {
        Debug.Log($"{LogPrefix} Switching enemy to Inference Only.");

        if (enemyModeSwitcher != null)
        {
            enemyModeSwitcher.SwitchToInferenceOnly();
            Debug.Log($"{LogPrefix} Enemy switched to Inference Only.");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} enemyModeSwitcher is not assigned. Cannot switch to Inference.");
        }
    }

    private void EndExperiment()
    {
        _phase = ExperimentPhase.Complete;

        Debug.Log($"{LogPrefix} Experiment complete. Ending game.");

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (experimentCompletePanel != null)
        {
            experimentCompletePanel.SetActive(true);

            if (experimentCompleteText != null)
                experimentCompleteText.text = "Experiment Complete";
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} experimentCompletePanel is not assigned. Game is paused as fallback.");
        }

        _experimentRoutine = null;
    }

    private void WireButtons(Button[] buttons, Action<int> onSelect)
    {
        if (buttons == null || buttons.Length == 0)
        {
            Debug.LogWarning($"{LogPrefix} Button group is empty or not assigned.");
            return;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            int rating = i + 1;

            if (buttons[i] == null)
            {
                Debug.LogWarning($"{LogPrefix} Rating button {rating} is missing.");
                continue;
            }

            buttons[i].onClick.AddListener(() => onSelect(rating));
        }
    }

    private void UpdateLabels()
    {
        if (q1SelectionLabel != null)
            q1SelectionLabel.text = _q1 > 0 ? $"Selected: {_q1} / 5" : "Not answered";

        if (q2SelectionLabel != null)
            q2SelectionLabel.text = _q2 > 0 ? $"Selected: {_q2} / 5" : "Not answered";

        if (q3SelectionLabel != null)
            q3SelectionLabel.text = _q3 > 0 ? $"Selected: {_q3} / 5" : "Not answered";
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText == null)
        {
            Debug.LogWarning($"{LogPrefix} feedbackText is not assigned. Message was: {message}");
            return;
        }

        feedbackText.text = message;
        feedbackText.gameObject.SetActive(true);

        Debug.Log($"{LogPrefix} Feedback shown: {message}");
    }

    private void SetSurveyInteractable(bool interactable)
    {
        SetButtonsInteractable(q1Buttons, interactable);
        SetButtonsInteractable(q2Buttons, interactable);
        SetButtonsInteractable(q3Buttons, interactable);

        if (submitButton != null)
            submitButton.interactable = interactable;
    }

    private void SetButtonsInteractable(Button[] buttons, bool interactable)
    {
        if (buttons == null)
            return;

        foreach (Button button in buttons)
        {
            if (button != null)
                button.interactable = interactable;
        }
    }

    private void HideSurveyImmediate()
    {
        if (surveyPanel != null)
            surveyPanel.SetActive(false);

        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
    }

    private void EnsureCsvExists()
    {
        try
        {
            if (!File.Exists(_csvPath))
            {
                File.WriteAllText(_csvPath, CSV_HEADER + "\n", Encoding.UTF8);
                Debug.Log($"{LogPrefix} Created survey CSV.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix} Failed to create survey CSV. Path: {_csvPath}. Error: {ex.Message}");
        }
    }

    private void LoadSurveyNumber()
    {
        try
        {
            if (!File.Exists(_csvPath))
            {
                _surveyNumber = 0;
                return;
            }

            string[] lines = File.ReadAllLines(_csvPath);
            int dataRows = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    dataRows++;
            }

            _surveyNumber = Mathf.Max(0, dataRows);
        }
        catch (Exception ex)
        {
            _surveyNumber = 0;
            Debug.LogError($"{LogPrefix} Failed to read existing survey CSV. Starting from 0. Error: {ex.Message}");
        }
    }

    private bool TrySaveRow()
    {
        try
        {
            _surveyNumber++;

            string persona = fightLogger != null ? fightLogger.GetPersonaLabel() : "Unknown";

            string row =
                $"{_surveyNumber}," +
                $"{EscapeCsv(_currentSurveyAgentType)}," +
                $"{EscapeCsv(persona)}," +
                $"{_q1},{_q2},{_q3}";

            File.AppendAllText(_csvPath, row + "\n", Encoding.UTF8);

            Debug.Log(
                $"{LogPrefix} Survey {_surveyNumber} saved | " +
                $"AgentType:{_currentSurveyAgentType} | Persona:{persona} | " +
                $"Q1:{_q1} Q2:{_q2} Q3:{_q3}"
            );

            StartCoroutine(PostDataToCloud("SurveyLog", row));
            return true;
        }
        catch (Exception ex)
        {
            _surveyNumber = Mathf.Max(0, _surveyNumber - 1);
            Debug.LogError($"{LogPrefix} Failed to save survey row. Error: {ex.Message}");
            return false;
        }
        
    }

    private IEnumerator PostDataToCloud(string logType, string dataRow)
{
    if (!enableCloudLogging)
        yield break;

    if (string.IsNullOrWhiteSpace(cloudWebhookURL) ||
        cloudWebhookURL == "YOUR_GOOGLE_SCRIPT_URL_HERE")
    {
        Debug.LogWarning($"{LogPrefix} Cloud webhook URL is not configured.");
        yield break;
    }

    WWWForm form = new WWWForm();
    form.AddField("logType", logType);
    form.AddField("dataRow", dataRow);

    using UnityWebRequest request = UnityWebRequest.Post(cloudWebhookURL, form);

    yield return request.SendWebRequest();

    if (request.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"{LogPrefix} Survey cloud upload failed. Error: {request.error}");
    }
    else
    {
            Debug.Log($"{LogPrefix} Survey cloud upload successful.");
    }
}

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool mustQuote =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!mustQuote)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}