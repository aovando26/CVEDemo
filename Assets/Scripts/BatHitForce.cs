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

    [SerializeField] private Vector3 damageTextOffset = new Vector3(0.7646f, 1.5f, 1.59919f);
    public DamageNumber hitDamage;

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - _lastHitTime < hitCooldown) return;

        BallBehavior ball = other.GetComponent<BallBehavior>();

        Rigidbody ballRb = other.GetComponent<Rigidbody>();
        BallSpawnPosition spawner = other.GetComponent<BallSpawnPosition>();

        ball.OnHit();

        ballRb.AddForce(hitDirection.normalized * hitForce, ForceMode.Impulse);

        GameManager.Instance.AddScore(10);

        spawner.OnBallHit();

        AudioManager.Instance?.Play("ball_hit");

        ParticleSystem effect = Instantiate(hitParticlePrefab, other.transform.position, Quaternion.identity);

        DamageNumber hitDamageText = hitDamage.Spawn(transform.position);
        Destroy(effect.gameObject, 2f); 

        _lastHitTime = Time.time;
    }
}