using TMPro;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BatBehavior : MonoBehaviour
{
    [Header("Bat Configuration")]
    [SerializeField] private Transform sweetSpotTransform;
    [SerializeField] private float maxSweetSpotDistance = 0.3f;

    [Header("Force Tuning")]
    [Tooltip("Scales the raw bat velocity into a useful impulse range.")]
    [SerializeField] private float forceScale = 4f;
    [Tooltip("Blend factor (0=collision normal, 1=swing direction) for launch angle.")]
    [SerializeField][Range(0f, 1f)] private float swingDirectionBlend = 0.35f;

    [Header("References")]
    [SerializeField] private TextMeshProUGUI hitInfoText;
    [SerializeField] private BallSpawnPosition ballSpawner;

    private Rigidbody _batRb;
    private const float MS_TO_MPH = 2.237f;

    private void Start()
    {
        _batRb = GetComponent<Rigidbody>();

        if (hitInfoText != null)
            hitInfoText.text = "Waiting for hit...";
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ball")) return;

        BallBehavior ballBehavior = collision.gameObject.GetComponent<BallBehavior>();
        Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();

        if (ballRb == null || ballBehavior == null || ballBehavior.IsHit) return;

        ContactPoint contact = collision.contacts[0];
        Vector3 contactPoint = contact.point;

        // 1. Contact normal points from ball to bat — negate to get ball launch direction.
        Vector3 collisionNormal = -contact.normal;

        // 2. Bat swing velocity from the physics follower's Rigidbody.
        Vector3 batVelocity = _batRb.linearVelocity;
        float swingSpeed = batVelocity.magnitude;

        // 3. Effective speed: component of swing velocity along the launch direction.
        //    A glancing hit naturally produces less force than a clean square hit.
        float effectiveSpeed = Mathf.Max(0f, Vector3.Dot(batVelocity, collisionNormal));

        // 4. Blend collision normal with swing direction for a realistic launch angle.
        Vector3 launchDirection = Vector3.Lerp(
            collisionNormal,
            batVelocity.normalized,
            swingDirectionBlend
        ).normalized;

        // 5. Sweet spot factor: 0 at the handle/tip, 1 at the sweet spot.
        float distanceToSweetSpot = Vector3.Distance(contactPoint, sweetSpotTransform.position);
        float sweetSpotFactor = Mathf.Clamp01(1f - (distanceToSweetSpot / maxSweetSpotDistance));
        float powerMultiplier = Mathf.Lerp(0.25f, 1.2f, sweetSpotFactor);

        // 6. Final impulse — proportional to actual swing speed, no artificial floor.
        float finalForce = effectiveSpeed * powerMultiplier * forceScale;

        // 7. Activate ball physics, then apply impulse and backspin.
        ballBehavior.OnHit();
        ballRb.AddForce(launchDirection * finalForce, ForceMode.Impulse);

        Vector3 spinAxis = Vector3.Cross(launchDirection, Vector3.up).normalized;
        ballRb.angularVelocity = spinAxis * (finalForce * 0.15f);

        // 8. Display result and trigger respawn timer.
        float ballSpeedMph = ballRb.linearVelocity.magnitude * MS_TO_MPH;
        string hitQuality = DetermineHitQuality(sweetSpotFactor, ballSpeedMph);
        DisplayHitInfo(ballSpeedMph, swingSpeed, hitQuality);

        if (ballSpawner != null)
            ballSpawner.OnBallHit();
    }

    private string DetermineHitQuality(float sweetSpotFactor, float mph)
    {
        if (sweetSpotFactor >= 0.9f && mph >= 80f) return "PERFECT HIT!";
        if (sweetSpotFactor >= 0.7f && mph >= 60f) return "GREAT HIT!";
        if (sweetSpotFactor >= 0.5f && mph >= 40f) return "Good Hit";
        if (mph >= 20f) return "Fair Hit";
        return "Weak Hit";
    }

    private void DisplayHitInfo(float ballMph, float batSpeedMs, string quality)
    {
        float batMph = batSpeedMs * MS_TO_MPH;

        if (hitInfoText != null)
            hitInfoText.text = $"<b>Ball Hit!</b>\nBat: {batMph:F1} mph | Ball: {ballMph:F1} mph\n{quality}";

        Debug.Log($"[BatBehavior] Bat: {batMph:F1} mph | Ball Exit: {ballMph:F1} mph | Quality: {quality}");
    }
}