using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField]
    private float timerCountdown = 60f;
    private float maxTime;

    private int score = 0;
    private bool gameIsActive = false;

    public UnityEvent OnGamePlay = new UnityEvent();

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
        // DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        maxTime = timerCountdown;
        gameIsActive = true;
    }

    void Update()
    {
        if (gameIsActive && timerCountdown > 0)
        {
            timerCountdown -= Time.deltaTime;
        }
        else if (timerCountdown <= 0 && gameIsActive)
        {
            GameComplete();
        }
    }

    public void AddScore(int pointsToAdd)
    {
        score += pointsToAdd;
    }

    private void GameComplete()
    {
        gameIsActive = false;
        SceneManager.LoadScene(0);
    }
}