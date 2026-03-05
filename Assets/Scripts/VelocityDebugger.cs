using UnityEngine;

public class VelocityDebugger : MonoBehaviour
{
    [SerializeField]
    private float maxVelocity = 50f; // Increased from 20 to see higher velocities

    void Update()
    {
        GetComponent<Renderer>().material.color = ColorForVelocity(); 
    }

    private Color ColorForVelocity()
    { 
        float velocity = GetComponent<Rigidbody>().linearVelocity.magnitude;
        Debug.Log($"Bat Velocity: {velocity:F2}"); // Shows velocity in console
        return Color.Lerp(Color.green, Color.red, velocity / maxVelocity);
    }
}
