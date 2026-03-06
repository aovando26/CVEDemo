using UnityEngine;

/// <summary>
/// Manages the baseball's physics state. Frozen at spawn position until hit,
/// then full physics simulation takes over.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Baseball : MonoBehaviour
{
    [Tooltip("Backspin intensity relative to impulse magnitude.")]
    [SerializeField][Range(0f, 0.5f)] private float _backspinFactor = 0.2f;

    private Rigidbody _rigidbody;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    public bool IsHit { get; private set; }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        Freeze();
    }

    /// <summary>
    /// Applies a physics impulse to the ball and enables full simulation.
    /// </summary>
    public void Hit(Vector3 impulse)
    {
        if (IsHit) return;

        IsHit = true;
        _rigidbody.constraints = RigidbodyConstraints.None;
        _rigidbody.useGravity = true;
        _rigidbody.AddForce(impulse, ForceMode.Impulse);

        // Backspin on horizontal axis perpendicular to launch direction (realistic ball flight)
        Vector3 spinAxis = Vector3.Cross(impulse.normalized, Vector3.up).normalized;
        _rigidbody.angularVelocity = spinAxis * (impulse.magnitude * _backspinFactor);
    }

    /// <summary>
    /// Returns the ball to its spawn position and freezes it in place.
    /// </summary>
    public void ResetBall()
    {
        IsHit = false;
        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;
        Freeze();
    }

    private void Freeze()
    {
        _rigidbody.useGravity = false;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.constraints = RigidbodyConstraints.FreezeAll;
    }
}
