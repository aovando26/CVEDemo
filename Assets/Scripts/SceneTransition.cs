using com.rfilkov.components;
using com.rfilkov.kinect;
//using System.Collections;
//using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;
//using UnityEngine.UI;

public class SceneTransition : MonoBehaviour
{
    private SceneGestureListener gestureListener;
    //public Image image; 
    

    private void Awake()
    { 
        gestureListener = SceneGestureListener.Instance;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    //private void Start()
    //{
    //    FadeOut();
    //}

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
            //FadeAndLoad("GameScene", 1);
            LoadNextScene();
        }

        if (!gestureListener)
        {
            return;
        }

        if (gestureListener.IsSwipeUp())
        {
            //FadeAndLoad("GameScene", 1);
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

    //public void FadeAndLoad(string sceneName, float duration)
    //{ 
    //    StartCoroutine(Fader(sceneName, duration));
    //}

    //IEnumerator Fader(string sceneName, float duration)
    //{
    //    float t = 0;
    //    Color c = image.color;

    //    while (t < duration)
    //    { 
    //        t += Time.deltaTime;
    //        c.a = t / duration;
    //        yield return null;
    //    }

    //    SceneManager.LoadScene(sceneName);
    //}

    //IEnumerator FadeOut()
    //{
    //    float t = 0;
    //    Color c = image.color;

    //    while (t < 1)
    //    {
    //        t += Time.deltaTime;
    //        c.a = 1f - (t / 1f);
    //        image.color = c;
    //        yield return null;
    //    }
    //}
}