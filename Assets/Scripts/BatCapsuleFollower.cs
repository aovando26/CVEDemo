using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BatCapsuleFollower : MonoBehaviour
{
    [SerializeField] private float _sensitivity = 50f;

    private Rigidbody _rigidbody;
    private BatCapsule _batFollower;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_batFollower == null) return;

        Transform target = _batFollower.transform;

        // Drive position via velocity so the physics engine sees a proper swing speed.
        Vector3 destination = target.position;
        _rigidbody.linearVelocity = (destination - _rigidbody.position) * _sensitivity;

        // Sync rotation to match the bat target each physics step.
        _rigidbody.MoveRotation(target.rotation);
    }

    /// <summary>
    /// Assigns the BatCapsule this follower should track.
    /// </summary>
    public void SetFollowTarget(BatCapsule batFollower)
    {
        _batFollower = batFollower;
    }
}

