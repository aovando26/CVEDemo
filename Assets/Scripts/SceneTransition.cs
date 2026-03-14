using com.rfilkov.components;
using com.rfilkov.kinect;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransition : MonoBehaviour
{
    private SceneGestureListener gestureListener;

    private void Awake()
    { 
        gestureListener = SceneGestureListener.Instance;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Refresh the KinectGestureManager's listener list first,
        // so it picks up the new SceneGestureListener instance in this scene
        KinectManager kinectManager = KinectManager.Instance;
        if (kinectManager != null && kinectManager.gestureManager != null)
        {
            kinectManager.gestureManager.RefreshGestureListeners();
            Debug.Log("✓ KinectGestureManager listeners refreshed");
        }
        else
        {
            Debug.LogWarning("KinectManager or gestureManager not available during scene load");
        }

        // Now grab the fresh SceneGestureListener instance
        SceneGestureListener oldListener = gestureListener;
        gestureListener = SceneGestureListener.Instance;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            LoadNextScene();
        }

        if (!gestureListener)
        {
            return;
        }

        if (gestureListener.IsSwipeLeft())
        {
            LoadNextScene();
        }
    }

    private void LoadNextScene()
    {
        Debug.Log($"Current Scene: {SceneManager.GetActiveScene().name}");
        SceneManager.LoadScene(1);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}