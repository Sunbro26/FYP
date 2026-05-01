using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Text;

/// <summary>
/// Post-fight survey — 3 research-backed questions:
///   Q1 - Challenge Calibration     (GEQ framework — IJsselsteijn et al. 2013)
///   Q2 - Forced Style Change       (Adaptive AI pressure — Yannakakis & Togelius 2011)
///   Q3 - Flow State / Immersion    (GameFlow — Sweetser & Wyeth 2005)
/// </summary>
public class SurveyUI : MonoBehaviour
{
    [Header("Root Panel — the GameObject to show/hide for the whole survey")]
    [SerializeField] private GameObject surveyPanel;

    [Header("Q1 — Challenge Calibration (5 buttons, labeled 1 to 5)")]
    [SerializeField] private Button[] q1Buttons;          // 5 buttons
    [SerializeField] private TMP_Text q1SelectionLabel;   // shows current selection


    [Header("Q2 — Forced Style Change (5 buttons, 1=Strongly Disagree, 5=Strongly Agree)")]
    [SerializeField] private Button[] q2Buttons;
    [SerializeField] private TMP_Text q2SelectionLabel;


    [Header("Q3 — Flow State (5 buttons, 1=Strongly Disagree, 5=Strongly Agree)")]
    [SerializeField] private Button[] q3Buttons;
    [SerializeField] private TMP_Text q3SelectionLabel;

    [Header("Submit Button and Feedback Text")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text feedbackText;  // "Please answer all" or "Saved!"

    [Header("FightLogger Reference (drag the same GameObject that has FightLogger)")]
    [SerializeField] private FightLogger fightLogger;

    // Internal
    private int _q1 = 0;
    private int _q2 = 0;
    private int _q3 = 0;
    private int _surveyNumber = 0;
    private string _csvPath;

    private const string CSV_HEADER =
        "survey_num,persona,challenge_calibration_1to5,forced_style_change_1to5,flow_state_1to5";

    void Awake()
    {
        _csvPath = Path.Combine(Application.persistentDataPath, "survey_log.csv");
        if (!File.Exists(_csvPath))
            File.WriteAllText(_csvPath, CSV_HEADER + "\n", Encoding.UTF8);

        if (surveyPanel != null) surveyPanel.SetActive(false);
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        // Always start hidden and with clean state
        _q1 = 0; _q2 = 0; _q3 = 0;
        if (surveyPanel != null) surveyPanel.SetActive(false);
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        WireButtons(q1Buttons, v => { _q1 = v; UpdateLabels(); });
        WireButtons(q2Buttons, v => { _q2 = v; UpdateLabels(); });
        WireButtons(q3Buttons, v => { _q3 = v; UpdateLabels(); });
        if (submitButton != null) submitButton.onClick.AddListener(OnSubmit);
    }

    // Called by FightLogger after each fight ends 
    public void ShowSurvey()
    {
        _q1 = 0; _q2 = 0; _q3 = 0;
        UpdateLabels();
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        if (surveyPanel != null) surveyPanel.SetActive(true);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;  // ADD THIS
        Cursor.visible = true;                  // ADD THIS
    }

    // Helpers 
    private void WireButtons(Button[] buttons, System.Action<int> onSelect)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            int rating = i + 1;
            buttons[i].onClick.AddListener(() => onSelect(rating));
        }
    }

    private void UpdateLabels()
    {
        if (q1SelectionLabel != null)
            q1SelectionLabel.text = _q1 > 0 ? $"Your answer: {_q1} / 5" : "Not answered yet";
        if (q2SelectionLabel != null)
            q2SelectionLabel.text = _q2 > 0 ? $"Your answer: {_q2} / 5" : "Not answered yet";
        if (q3SelectionLabel != null)
            q3SelectionLabel.text = _q3 > 0 ? $"Your answer: {_q3} / 5" : "Not answered yet";
    }

    private void OnSubmit()
    {
        if (_q1 == 0 || _q2 == 0 || _q3 == 0)
        {
            ShowFeedback("Please answer all 3 questions before submitting.");
            return;
        }
        SaveRow();
        ShowFeedback("Response saved. Thank you!");
        Invoke(nameof(CloseSurvey), 1.5f);
    }

    private void ShowFeedback(string msg)
    {
        if (feedbackText == null) return;
        feedbackText.text = msg;
        feedbackText.gameObject.SetActive(true);
    }

    private void CloseSurvey()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked; // ADD THIS
        Cursor.visible = false;                  // ADD THIS
        if (surveyPanel != null) surveyPanel.SetActive(false);
    }

    private void SaveRow()
    {
        _surveyNumber++;
        string persona = fightLogger != null ? fightLogger.GetPersonaLabel() : "Unknown";
        string row = $"{_surveyNumber},{persona},{_q1},{_q2},{_q3}";
        File.AppendAllText(_csvPath, row + "\n", Encoding.UTF8);
        Debug.Log($"[SurveyUI] Saved — Persona:{persona} Q1:{_q1} Q2:{_q2} Q3:{_q3}");
    }
}