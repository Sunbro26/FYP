using UnityEngine;

namespace AdaptiveCombatFramework
{
    public class ControlsOverlay : MonoBehaviour
    {
        [Header("UI Settings")]
        public float panelWidth = 220f;
        public float panelHeight = 200f;
        public float margin = 20f;
        public int fontSize = 15;

        [Header("Controls Text")][TextArea(10, 15)][Tooltip("Type your controls here. You can use standard text or rich text like <b>bold</b>.")]
        public string controlsText = 
            "<b>PLAYER CONTROLS</b>\n\n" +
            "<b>W A S D</b> - Move\n" +
            "<b>Mouse</b> - Look Around\n\n" +
            "<b>Left Click</b> - Attack\n" +
            "<b>Right Click</b> - Hold to Block\n" +
            "<b>Q</b> - Parry\n" +
            "<b>Space</b> - Dodge Roll\n" +
            "<b>Middle Mouse</b> - Lock On";

        private GUIStyle _boxStyle;
        private GUIStyle _textStyle;

        void OnGUI()
        {
            // Set up the font and box styles on the first frame
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                
                _textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = this.fontSize,
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft
                };
                _textStyle.normal.textColor = Color.white;
            }

            // Calculate position (Top-Right corner of the screen)
            float xPos = Screen.width - panelWidth - margin;
            float yPos = margin;
            Rect panelRect = new Rect(xPos, yPos, panelWidth, panelHeight);

            // Draw the dark transparent background box
            GUI.Box(panelRect, "", _boxStyle);

            // Draw the text inside the box (with a 15-pixel padding)
            Rect textRect = new Rect(xPos + 15, yPos + 10, panelWidth - 30, panelHeight - 20);
            GUI.Label(textRect, controlsText, _textStyle);
        }
    }
}