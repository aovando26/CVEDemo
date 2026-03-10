using DamageNumbersPro;
using UnityEngine;

public class BatHitForce : MonoBehaviour
{
    [Header("Force")]
    [SerializeField] private Vector3 hitDirection = new Vector3(0f, 0.5f, 1f);
    [SerializeField] private float hitForce = 15f;

    [Header("Effects")]
    [SerializeField] private ParticleSystem hitParticlePrefab;

    [Header("Cooldown")]
    [SerializeField] private float hitCooldown = 0.5f;

    private float _lastHitTime = float.NegativeInfinity;

    [SerializeField] private Vector3 damageTextOffset = new Vector3(0.7646f, 1.5f, 1.59919f);  // Position from Inspector
    public DamageNumber hitDamage;

    private void OnTriggerEnter(Collider other)
    {
        // 1. Cooldown & Null Check
        if (Time.time - _lastHitTime < hitCooldown) return;

        BallBehavior ball = other.GetComponent<BallBehavior>();
        if (ball == null || ball.IsHit) return;

        // 2. Get the other components
        Rigidbody ballRb = other.GetComponent<Rigidbody>();
        BallSpawnPosition spawner = other.GetComponent<BallSpawnPosition>();

        // 3. Logic: Wake up the ball and push it
        ball.OnHit();
        if (ballRb != null)
        {
            ballRb.AddForce(hitDirection.normalized * hitForce, ForceMode.Impulse);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddScore(10); // Change '10' to whatever point value you want!
        }

        // 4. Start the respawn timer
        if (spawner != null)
        {
            spawner.OnBallHit();
        }

        // 5. Sound & Particles
        AudioManager.Instance?.Play("ball_hit");

        if (hitParticlePrefab != null)
        {
            // Spawn at the ball's current position
            ParticleSystem effect = Instantiate(hitParticlePrefab, other.transform.position, Quaternion.identity);

            DamageNumber hitDamageText = hitDamage.Spawn(transform.position); // Slightly above the ball
                                                                            // 
            Destroy(effect.gameObject, 2f); // Clean up after 2 seconds
        }

        _lastHitTime = Time.time;
        Debug.Log("[Bat] Trigger Hit Success!");
    }
}