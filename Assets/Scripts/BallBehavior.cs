using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallBehavior : MonoBehaviour
{
    private Rigidbody _rigidbody;
    public bool IsHit { get; private set; }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        ResetBall();
    }

    /// <summary>
    /// Activates full physics simulation on the ball after it is struck.
    /// </summary>
    public void OnHit()
    {
        IsHit = true;
        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
    }

    /// <summary>
    /// Freezes the ball at its current position and resets all physics state.
    /// </summary>
    public void ResetBall()
    {
        IsHit = false;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
    }
}
