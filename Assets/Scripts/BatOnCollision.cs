using UnityEngine;

public class BatOnCollision : MonoBehaviour
{
    private Vector3 previousPosition;
    private Rigidbody batRb;

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
            BallBehavior ballBehavior = collision.gameObject.GetComponent<BallBehavior>();
            if (ballBehavior == null) return;

            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
            
            // Calculate bat velocity based on movement
            Vector3 batVelocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
            float batSpeed = batVelocity.magnitude;

            // Get hit direction from bat forward
            Vector3 hitDirection = -transform.forward;

            // Apply force scaled by bat velocity (fast swing = big force, slow swing = small force)
            float forceMultiplier = Mathf.Clamp(batSpeed, 1f, 100f); // Scale between 1-100
            ballRb.AddForce(hitDirection * forceMultiplier * 10f, ForceMode.Impulse);

            // Enable gravity on the ball
            ballBehavior.ballIsHit = true;

            Debug.Log($"Bat Speed: {batSpeed:F2} | Ball Force: {forceMultiplier * 10f:F2}");
        }
    }
}
    