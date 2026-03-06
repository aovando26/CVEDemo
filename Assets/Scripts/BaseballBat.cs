using UnityEngine;
using TMPro;

/// <summary>
/// Handles bat collision detection, swing speed measurement, and sweet spot calculation.
/// Attach to a cube primitive scaled to look like a bat.
/// The bat is kinematic — driven externally by pose tracking (MediaPipe).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class BaseballBat : MonoBehaviour
{
    [Header("Bat Dimensions")]
    [Tooltip("Local axis that runs along the bat's length. Use Vector3.up if the cube is scaled tall on the Y axis.")]
    [SerializeField] private Vector3 _batLengthAxis = Vector3.up;

    [Header("Sweet Spot")]
    [Tooltip("Normalized position along bat length (0=handle end, 1=tip) where the sweet spot is centered. ~0.65 is realistic.")]
    [SerializeField][Range(0f, 1f)] private float _sweetSpotCenter = 0.65f;

    [Tooltip("Half-width of the sweet spot zone in normalized bat length. Hits within this range get full power.")]
    [SerializeField][Range(0.05f, 0.4f)] private float _sweetSpotRadius = 0.18f;

    [Header("Force")]
    [Tooltip("Scales swing speed (m/s) into the final impulse. Increase if hits feel weak.")]
    [SerializeField] private float _forceMultiplier = 8f;

    [Tooltip("Minimum power factor applied at the handle or tip (worst contact).")]
    [SerializeField][Range(0f, 1f)] private float _minPowerFactor = 0.2f;

    [Tooltip("Maximum power factor applied at the sweet spot (best contact).")]
    [SerializeField][Range(1f, 3f)] private float _maxPowerFactor = 1.5f;

    [Tooltip("Blends launch direction between collision normal (0) and swing direction (1).")]
    [SerializeField][Range(0f, 1f)] private float _swingDirectionBlend = 0.4f;

    [Header("References")]
    [SerializeField] private Baseball _ball;
    [SerializeField] private float _respawnDelay = 3f;
    [SerializeField] private TextMeshProUGUI _hitInfoText;

    private Rigidbody _rigidbody;
    private BoxCollider _boxCollider;
    private Vector3 _previousPosition;
    private bool _waitingForRespawn;
    private float _respawnTimer;

    private const float MS_TO_MPH = 2.237f;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _boxCollider = GetComponent<BoxCollider>();

        // Bat is driven externally by pose tracking — kinematic so gravity doesn't pull it down.
        // Kinematic Rigidbodies still receive OnCollisionEnter when hitting dynamic Rigidbodies.
        _rigidbody.isKinematic = true;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        _previousPosition = transform.position;
        SetHitText("Swing to hit!");
    }

    private void FixedUpdate()
    {
        // Snapshot position each physics step so OnCollisionEnter can compute velocity.
        _previousPosition = transform.position;
    }

    private void Update()
    {
        if (!_waitingForRespawn) return;

        _respawnTimer -= Time.deltaTime;
        if (_respawnTimer <= 0f)
            RespawnBall();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ball")) return;
        if (_ball == null || _ball.IsHit) return;

        ContactPoint contact = collision.contacts[0];
        Vector3 contactPoint = contact.point;

        // 1. Swing velocity from position delta — captures the real-world arm speed from pose tracking.
        Vector3 swingVelocity = (transform.position - _previousPosition) / Time.fixedDeltaTime;
        float swingSpeed = swingVelocity.magnitude;

        // 2. Normalize contact point along the bat's length axis (0 = handle, 1 = tip).
        Vector3 batAxisWorld = transform.TransformDirection(_batLengthAxis).normalized;
        float batWorldLength = _boxCollider.size.y * transform.lossyScale.y;
        Vector3 handleBase = transform.position - batAxisWorld * (batWorldLength * 0.5f);
        float contactT = Mathf.Clamp01(Vector3.Dot(contactPoint - handleBase, batAxisWorld) / batWorldLength);

        // 3. Sweet spot power factor — falls off smoothly from sweet spot toward handle/tip.
        float distFromSweet = Mathf.Abs(contactT - _sweetSpotCenter);
        float sweetFactor = Mathf.Clamp01(1f - (distFromSweet / _sweetSpotRadius));
        float powerFactor = Mathf.Lerp(_minPowerFactor, _maxPowerFactor, sweetFactor);

        // 4. Launch direction blends collision normal (physically correct) with swing direction (feel).
        Vector3 launchDir = Vector3.Lerp(-contact.normal, swingVelocity.normalized, _swingDirectionBlend).normalized;

        // 5. Apply impulse to ball.
        float finalForce = swingSpeed * powerFactor * _forceMultiplier;
        _ball.Hit(launchDir * finalForce);

        // 6. Display feedback.
        float swingMph = swingSpeed * MS_TO_MPH;
        string quality = GetHitQuality(sweetFactor, contactT);
        string zone = GetContactZone(contactT);
        SetHitText($"<b>{quality}</b>\nSwing: {swingMph:F1} mph  |  Zone: {zone}");

        Debug.Log($"[BaseballBat] Swing: {swingMph:F1} mph | T: {contactT:F2} | SweetFactor: {sweetFactor:F2} | Power: {powerFactor:F2}x | Impulse: {finalForce:F1}N");

        // 7. Schedule ball respawn.
        _waitingForRespawn = true;
        _respawnTimer = _respawnDelay;
    }

    private void RespawnBall()
    {
        _waitingForRespawn = false;
        _ball?.ResetBall();
        SetHitText("Swing to hit!");
    }

    private string GetHitQuality(float sweetFactor, float contactT)
    {
        if (sweetFactor >= 0.8f) return "PERFECT HIT!";
        if (sweetFactor >= 0.55f) return "GREAT HIT!";
        if (sweetFactor >= 0.3f) return "Good Hit";
        if (contactT < 0.15f || contactT > 0.9f) return "Edge Hit";
        return "Weak Hit";
    }

    private string GetContactZone(float contactT)
    {
        if (contactT < 0.2f) return "Handle";
        if (contactT < 0.45f) return "Lower Barrel";
        if (contactT < 0.78f) return "Sweet Spot";
        return "Tip";
    }

    private void SetHitText(string message)
    {
        if (_hitInfoText != null)
            _hitInfoText.text = message;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Vector3 batAxisWorld = transform.TransformDirection(_batLengthAxis).normalized;
        float batLength = col.size.y * transform.lossyScale.y;
        Vector3 handleBase = transform.position - batAxisWorld * (batLength * 0.5f);

        float sweetStart = Mathf.Max(0f, _sweetSpotCenter - _sweetSpotRadius) * batLength;
        float sweetEnd = Mathf.Min(1f, _sweetSpotCenter + _sweetSpotRadius) * batLength;

        // Green line = sweet spot zone
        Gizmos.color = Color.green;
        Gizmos.DrawLine(handleBase + batAxisWorld * sweetStart, handleBase + batAxisWorld * sweetEnd);

        // Yellow sphere = sweet spot center
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(handleBase + batAxisWorld * (_sweetSpotCenter * batLength), 0.04f);

        // Red sphere = handle end
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(handleBase, 0.03f);
    }
#endif
}
