using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BatBehavior : MonoBehaviour
{
    [SerializeField] private Transform sweetSpotTransform;
    [SerializeField] private float swingSpeed = 20f;
    [SerializeField] private float maxSweetSpotDistance = 0.3f;
    [SerializeField] private TextMeshProUGUI hitInfoText;
    [SerializeField] private BallSpawnPosition ballSpawner; // Assign the ball in Inspector
    
    private Rigidbody batRb;
    private Vector3 previousPosition;
    private const float FORCE_TO_MPH = 2.237f;

    private void Start()
    {           
        batRb = GetComponent<Rigidbody>();
        previousPosition = transform.position;
        
        if (hitInfoText != null)
            hitInfoText.text = "Waiting for hit...";
    }

    private void FixedUpdate()
    {
        previousPosition = transform.position;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ball"))
        {
            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
            if (ballRb == null) return;

            // 1. Get collision normal
            Vector3 hitDirection = collision.contacts[0].normal;
            Vector3 contactPoint = collision.contacts[0].point;

            // 2. Calculate Sweet Spot factor
            float distanceToSweetSpot = Vector3.Distance(contactPoint, sweetSpotTransform.position);
            float sweetSpotFactor = Mathf.Clamp01(1.0f - (distanceToSweetSpot / maxSweetSpotDistance));
            
            // 3. Calculate bat velocity
            Vector3 batVelocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
            float velocityMagnitude = Vector3.Dot(batVelocity, hitDirection);
            
            // 4. Apply force
            float powerMultiplier = Mathf.Lerp(0.3f, 1.2f, sweetSpotFactor);
            float finalForce = Mathf.Max(velocityMagnitude, swingSpeed) * powerMultiplier;
            
            ballRb.linearVelocity = Vector3.zero;
            ballRb.AddForce(hitDirection * finalForce, ForceMode.Impulse);
            
            // 5. Add spin
            Vector3 spinAxis = Vector3.Cross(hitDirection, contactPoint - ballRb.transform.position).normalized;
            ballRb.angularVelocity = spinAxis * (finalForce * 0.1f);
            
            // 6. Calculate speed and display hit info
            float ballSpeedMs = ballRb.linearVelocity.magnitude;
            float ballSpeedMph = ballSpeedMs * FORCE_TO_MPH;
            string hitQuality = DetermineHitQuality(sweetSpotFactor, ballSpeedMph);
            
            DisplayHitInfo(ballSpeedMph, hitQuality);
            
            // 7. Trigger ball respawn
            if (ballSpawner != null)
            {
                ballSpawner.OnBallHit();
            }
        }
    }

    private string DetermineHitQuality(float sweetSpotFactor, float mph)
    {
        if (sweetSpotFactor >= 0.9f && mph >= 80f)
            return "PERFECT HIT! ⭐";
        else if (sweetSpotFactor >= 0.7f && mph >= 60f)
            return "GREAT HIT! ✓";
        else if (sweetSpotFactor >= 0.5f && mph >= 40f)
            return "Good Hit";
        else if (mph >= 20f)
            return "Fair Hit";
        else
            return "Weak Hit";
    }

    private void DisplayHitInfo(float mph, string quality)
    {
        if (hitInfoText != null)
        {
            hitInfoText.text = $"<b>Ball Hit!</b>\nSpeed: {mph:F1} mph\n{quality}";
        }
        
        Debug.Log($"Ball Hit - Speed: {mph:F1} mph - Quality: {quality}");
    }
}