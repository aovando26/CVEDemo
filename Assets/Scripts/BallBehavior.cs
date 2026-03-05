using UnityEngine;

public class BallBehavior : MonoBehaviour
{
    public bool ballIsHit = false;

    private void Start()
    {
        GetComponent<Rigidbody>().useGravity = false;
    }

    private void Update()
    {
        if (ballIsHit)
        {
            GetComponent<Rigidbody>().useGravity = true;
        }
    }
}
