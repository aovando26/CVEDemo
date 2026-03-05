using UnityEngine;

public class BatCapsuleFollower : MonoBehaviour
{
    private Rigidbody _rigidbody;
    [SerializeField] private float _sensitivity = 50f;
    private BatCapsule _batFollower;
    private Vector3 _velocity; // Missing field declaration

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        // Add null check to prevent errors if target isn't set
        if (_batFollower == null) return;

        Vector3 destination = _batFollower.transform.position;
        _rigidbody.transform.rotation = transform.rotation;

        _velocity = (destination - _rigidbody.transform.position) * _sensitivity;

        _rigidbody.linearVelocity = _velocity;
    }

    public void SetFollowTarget(BatCapsule batFollower)
    {
        _batFollower = batFollower;
    }
}
