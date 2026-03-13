using com.rfilkov.components;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransition : MonoBehaviour
{
    private SceneGestureListener gestureListener;

    private void Start()
    {
        // get the gestures listener
        gestureListener = SceneGestureListener.Instance;
    }

    private void Update()
    {
        // Check if L key is pressed
        if (Input.GetKeyDown(KeyCode.L))
        {
            LoadNextScene();
        }

        // Don't run if there is no gesture listener
        if (!gestureListener)
            return;

        // Check for swipe right gesture
        if (gestureListener.IsSwipeRight())
        {
            LoadNextScene();
        }
    }

    private void LoadNextScene()
    {
        SceneManager.LoadScene(1);
    }
}