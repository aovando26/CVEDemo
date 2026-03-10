using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameplayUIHandler : MonoBehaviour
{
    public TextMeshProUGUI timerText;
    public Slider timerSlider;
    public TextMeshProUGUI scoreText;

    void Update()
    {
        // Ensure GameManager exists before trying to read from it
        if (GameManager.Instance != null)
        {
            // Update Score Text
            if (scoreText != null)
            {
                scoreText.text = "Score: " + GameManager.Instance.Score.ToString();
            }

            // Update Timer Text (Mathf.CeilToInt rounds up so 0.1 seconds displays as 1)
            if (timerText != null)
            {
                timerText.text = Mathf.CeilToInt(GameManager.Instance.CurrentTime).ToString();
            }

            // Update Timer Slider (Calculates a value between 0 and 1)
            if (timerSlider != null)
            {
                timerSlider.value = GameManager.Instance.CurrentTime / GameManager.Instance.MaxTime;
            }
        }
    }
}