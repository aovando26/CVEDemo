using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallSpawnPosition : MonoBehaviour
{
    [SerializeField] private float respawnDelay = 3f;

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private Rigidbody _ballRb;
    private BallBehavior _ballBehavior;
    private float _timeSinceHit;
    private bool _ballWasHit;

    private void Start()
    {
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        _ballRb = GetComponent<Rigidbody>();
        _ballBehavior = GetComponent<BallBehavior>();
    }

    private void Update()
    {
        if (!_ballWasHit) return;

        _timeSinceHit += Time.deltaTime;
        if (_timeSinceHit >= respawnDelay)
            RespawnBall();
    }

    /// <summary>
    /// Called by BatBehavior when the ball is successfully hit.
    /// </summary>
    public void OnBallHit()
    {
        _ballWasHit = true;
        _timeSinceHit = 0f;
    }

    /// <summary>
    /// Returns the ball to its spawn position and resets all physics and game state.
    /// </summary>
    public void RespawnBall()
    {
        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        // ResetBall handles Rigidbody state (isKinematic, useGravity, velocity).
        if (_ballBehavior != null)
            _ballBehavior.ResetBall();

        _ballWasHit = false;
        _timeSinceHit = 0f;

        Debug.Log("[BallSpawnPosition] Ball respawned.");
    }
}

