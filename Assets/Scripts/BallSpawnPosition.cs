using UnityEngine;

public class BallSpawnPosition : MonoBehaviour
{
    [SerializeField] private float respawnDelay = 3f; // Time before ball respawns after being hit
    
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Rigidbody ballRb;
    private float timeSinceHit = float.MaxValue;
    private bool ballWasHit = false;

    private void Start()
    {
        // Store the initial spawn position and rotation
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        ballRb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // Check if enough time has passed to respawn
        if (ballWasHit)
        {
            timeSinceHit += Time.deltaTime;
            if (timeSinceHit >= respawnDelay)
            {
                RespawnBall();
            }
        }
    }

    /// <summary>
    /// Called by BatBehavior when the ball is hit
    /// </summary>
    public void OnBallHit()
    {
        ballWasHit = true;
        timeSinceHit = 0f;
    }

    /// <summary>
    /// Resets the ball to its spawn position with zero velocity
    /// </summary>
    public void RespawnBall()
    {
        // Reset position and rotation
        transform.position = spawnPosition;
        transform.rotation = spawnRotation;
        
        // Reset velocity and angular velocity
        if (ballRb != null)
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
        }
        
        // Reset hit tracking
        ballWasHit = false;
        timeSinceHit = float.MaxValue;
        
        Debug.Log("Ball respawned at original position");
    }
}
