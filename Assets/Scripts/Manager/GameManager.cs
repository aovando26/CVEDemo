using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField]
    private float timerCountdown = 60f; // Total game time in seconds
    private float maxTime; // Stored to easily calculate the UI slider percentage

    private int score = 0; // Track the player's score
    private bool gameIsActive = false; // Track if game is active

    public UnityEvent OnGamePlay = new UnityEvent();

    // Public properties so the UI can safely read these values
    public float CurrentTime => timerCountdown;
    public float MaxTime => maxTime;
    public int Score => score;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Removed: DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        maxTime = timerCountdown;
        gameIsActive = true;
        Debug.Log("✓ Game Started! Timer: " + maxTime + " seconds");
    }

    void Update()
    {
        // Countdown only if game is active
        if (gameIsActive && timerCountdown > 0)
        {
            timerCountdown -= Time.deltaTime;
        }
        else if (timerCountdown <= 0 && gameIsActive)
        {
            GameComplete();
        }
    }

    // Call this from your Bat script to increase the score
    public void AddScore(int pointsToAdd)
    {
        score += pointsToAdd;
        Debug.Log("➕ Score: " + score);
    }

    private void GameComplete()
    {
        gameIsActive = false;
        Debug.Log("⏰ Time's up! Final Score: " + score);
        SceneManager.LoadScene(0);
    }
}