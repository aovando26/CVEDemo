using UnityEngine;

public class BatBehavior : MonoBehaviour
{
    [SerializeField] private Transform sweetSpotTransform;
    [SerializeField] private float swingSpeed = 20f;
    [SerializeField] private float maxSweetSpotDistance = 0.3f;
    
    private Rigidbody batRb;
    private Vector3 previousPosition;

    private void Start()
    {
        batRb = GetComponent<Rigidbody>();
        previousPosition = transform.position;
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

            // 1. Get collision normal (direction ball will move away from bat)
            Vector3 hitDirection = collision.contacts[0].normal;
            Vector3 contactPoint = collision.contacts[0].point;

            // 2. Calculate Sweet Spot factor (1.0 at sweet spot, lower elsewhere)
            float distanceToSweetSpot = Vector3.Distance(contactPoint, sweetSpotTransform.position);
            float sweetSpotFactor = Mathf.Clamp01(1.0f - (distanceToSweetSpot / maxSweetSpotDistance));
            
            // 3. Calculate bat velocity for more realistic impact
            Vector3 batVelocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
            float velocityMagnitude = Vector3.Dot(batVelocity, hitDirection);
            
            // 4. Apply force based on swing speed, sweet spot, and bat velocity
            float powerMultiplier = Mathf.Lerp(0.3f, 1.2f, sweetSpotFactor);
            float finalForce = Mathf.Max(velocityMagnitude, swingSpeed) * powerMultiplier;
            
            ballRb.linearVelocity = Vector3.zero; // Reset to prevent double forces
            ballRb.AddForce(hitDirection * -finalForce, ForceMode.Impulse);
            
            // 5. Add spin to the ball based on contact point offset from center
            Vector3 spinAxis = Vector3.Cross(hitDirection, contactPoint - ballRb.transform.position).normalized;
            ballRb.angularVelocity = spinAxis * (finalForce * 0.1f);
        }
    }
}   