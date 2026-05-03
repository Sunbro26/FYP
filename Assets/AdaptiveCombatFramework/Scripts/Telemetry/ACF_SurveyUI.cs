using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Text;

/// <summary>
///   Q1 - Challenge Calibration  (GEQ � IJsselsteijn et al. 2013)
///   Q2 - Forced Style Change    (Adaptive AI � Yannakakis & Togelius 2011)
///   Q3 - Flow State             (GameFlow � Sweetser & Wyeth 2005)
/// </summary>
namespace AdaptiveCombatFramework {
    public class SurveyUI : MonoBehaviour
    {
        [Header("Root Panel � the GameObject to show/hide for the whole survey")]
        [SerializeField] private GameObject surveyPanel;

        // "The boss felt appropriately challenging for my skill level."
        [Header("Q1 � Challenge Calibration (5 buttons labeled 1 to 5)")]
        [SerializeField] private Button[] q1Buttons;
        [SerializeField] private TMP_Text q1SelectionLabel;

        // "The boss forced me to change my fighting style during the fight."
        [Header("Q2 � Forced Style Change (5 buttons, 1=Strongly Disagree, 5=Strongly Agree)")]
        [SerializeField] private Button[] q2Buttons;
        [SerializeField] private TMP_Text q2SelectionLabel;

        // "I was fully focused on the fight and lost track of time."
        [Header("Q3 � Flow State (5 buttons, 1=Strongly Disagree, 5=Strongly Agree)")]
        [SerializeField] private Button[] q3Buttons;
        [SerializeField] private TMP_Text q3SelectionLabel;

        [Header("Submit")]
        [SerializeField] private Button submitButton;
        [SerializeField] private TMP_Text feedbackText;   // "Please answer all" / "Saved!"

        [Header("FightLogger Reference")]
        [SerializeField] private FightLogger fightLogger;

        //  Internal state
        private int _q1 = 0;
        private int _q2 = 0;
        private int _q3 = 0;
        private int _surveyNumber = 0;

        private string _csvPath;

        // survey_log.csv � agent_type added so survey scores split by Heuristic vs Trained
        private const string CSV_HEADER =
            "survey_num,agent_type,persona," +
            "challenge_calibration_1to5,forced_style_change_1to5,flow_state_1to5";

        void Awake()
        {
            _csvPath = Path.Combine(Application.persistentDataPath, "survey_log.csv");
            if (!File.Exists(_csvPath))
            {
                File.WriteAllText(_csvPath, CSV_HEADER + "\n", Encoding.UTF8);
                _surveyNumber = 0;
            }
            else
            {
                // Count existing data rows (excluding header) to continue numbering correctly
                string[] lines = File.ReadAllLines(_csvPath);
                _surveyNumber = Mathf.Max(0, lines.Length - 1); // minus 1 for header row
            }

            if (surveyPanel != null) surveyPanel.SetActive(false);
            if (feedbackText != null) feedbackText.gameObject.SetActive(false);
            Debug.Log($"[SurveyUI] Starting from survey #{_surveyNumber + 1}");
        }

        void OnEnable()
        {
            // Always reset to clean hidden state when scene starts
            // Prevents stale state from previous Play sessions
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

        // Public API called by FightLogger after surveyDelay seconds

        public void ShowSurvey()
        {
            _q1 = 0; _q2 = 0; _q3 = 0;
            UpdateLabels();
            if (feedbackText != null) feedbackText.gameObject.SetActive(false);
            if (surveyPanel != null) surveyPanel.SetActive(true);

            // Freeze game and unlock cursor so player can click buttons
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }


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
                q1SelectionLabel.text = _q1 > 0 ? $"Selected: {_q1} / 5" : "Not answered";
            if (q2SelectionLabel != null)
                q2SelectionLabel.text = _q2 > 0 ? $"Selected: {_q2} / 5" : "Not answered";
            if (q3SelectionLabel != null)
                q3SelectionLabel.text = _q3 > 0 ? $"Selected: {_q3} / 5" : "Not answered";
        }

        private void OnSubmit()
        {
            if (_q1 == 0 || _q2 == 0 || _q3 == 0)
            {
                ShowFeedback("Please answer all 3 questions.");
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
            // Resume game and re-lock cursor
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (surveyPanel != null) surveyPanel.SetActive(false);

            // Tell FightLogger survey is done � it cancels fallback timer and restarts fight
            if (fightLogger != null)
                fightLogger.OnSurveyComplete();
        }

        private void SaveRow()
        {
            _surveyNumber++;

            // Both agent type and persona are read from FightLogger automatically
            // No manual input required from tester
            string agentType = fightLogger != null ? fightLogger.GetAgentType() : "Unknown";
            string persona = fightLogger != null ? fightLogger.GetPersonaLabel() : "Unknown";

            string row = $"{_surveyNumber},{agentType},{persona},{_q1},{_q2},{_q3}";
            File.AppendAllText(_csvPath, row + "\n", Encoding.UTF8);

            Debug.Log($"[SurveyUI] Survey {_surveyNumber} saved | " +
                    $"AgentType:{agentType} | Persona:{persona} | " +
                    $"Q1:{_q1} Q2:{_q2} Q3:{_q3}");
        }
    }
}