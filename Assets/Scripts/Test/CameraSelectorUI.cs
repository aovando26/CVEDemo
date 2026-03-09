using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using com.rfilkov.kinect;
using System.Collections.Generic;
using TMPro;

public class CameraSelectorUI : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown cameraDropdown;
    [SerializeField] private Button nextSceneButton;
    [SerializeField] private UniCamInterface unicamInterface;
    [SerializeField] private string nextSceneName = "GameScene";

    private List<string> availableCameras = new List<string>();
    private int selectedCameraIndex = 0;

    private void Start()
    {
        // Validate all required components
        ValidateComponents();

        // Initialize camera list
        RefreshCameraList();

        // Setup dropdown listener - fires when user selects a camera
        cameraDropdown.onValueChanged.AddListener(OnCameraSelected);

        // Setup button listener - fires when user clicks next scene button
        nextSceneButton.onClick.AddListener(OnNextSceneButtonClicked);

        Debug.Log("✓ Camera Selector UI initialized");
    }

    /// <summary>
    /// Validates that all required components are assigned in Inspector
    /// </summary>
    private void ValidateComponents()
    {
        if (cameraDropdown == null)
        {
            Debug.LogError("❌ Camera dropdown not assigned in Inspector!");
            enabled = false;
            return;
        }

        if (unicamInterface == null)
        {
            Debug.LogError("❌ UniCamInterface not assigned in Inspector!");
            enabled = false;
            return;
        }

        if (nextSceneButton == null)
        {
            Debug.LogError("❌ Next Scene button not assigned in Inspector!");
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("❌ Next scene name is empty!");
            enabled = false;
            return;
        }

        Debug.Log("✓ All components validated");
    }

    /// <summary>
    /// Refreshes the list of available cameras from WebCamTexture
    /// </summary>
    public void RefreshCameraList()
    {
        availableCameras.Clear();
        cameraDropdown.options.Clear();

        // Get all available webcam devices connected to the system
        var devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogWarning("⚠️ No cameras found on this device!");
            cameraDropdown.options.Add(new TMP_Dropdown.OptionData("No Cameras Available"));
            nextSceneButton.interactable = false;
            return;
        }

        // Populate dropdown with camera names
        foreach (var device in devices)
        {
            // Create friendly name with camera type indicator
            string cameraName = device.name;
            if (device.isFrontFacing)
                cameraName += " (Front)";
            else
                cameraName += " (Back)";

            availableCameras.Add(device.name);
            cameraDropdown.options.Add(new TMP_Dropdown.OptionData(cameraName));

            Debug.Log($"  Found camera {availableCameras.Count}: {device.name} {(device.isFrontFacing ? "(Front)" : "(Back)")}");
        }

        // Set default to first camera
        cameraDropdown.value = 0;
        selectedCameraIndex = 0;

        // Ensure UniCamInterface knows about the default selection
        if (unicamInterface != null)
        {
            unicamInterface.deviceIndex = 0;
        }

        // Enable button now that we have cameras
        nextSceneButton.interactable = true;

        Debug.Log($"✓ Found {devices.Length} camera(s)");
    }

    /// <summary>
    /// Called when user selects a camera from dropdown
    /// </summary>
    private void OnCameraSelected(int index)
    {
        if (index < 0 || index >= availableCameras.Count)
        {
            Debug.LogWarning($"⚠️ Invalid camera index: {index}");
            return;
        }

        selectedCameraIndex = index;

        // Update UniCamInterface with selected camera index
        if (unicamInterface != null)
        {
            unicamInterface.deviceIndex = index;
            Debug.Log($"✓ Camera selected: {availableCameras[index]} (Index: {index})");
        }
        else
        {
            Debug.LogError("❌ UniCamInterface is null!");
        }
    }

    /// <summary>
    /// Called when user clicks "Next Scene" button
    /// Preserves camera selection across scene loads
    /// </summary>
    private void OnNextSceneButtonClicked()
    {
        // Validate scene name before proceeding
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("❌ Next scene name is not set!");
            return;
        }

        // Log current camera selection
        Debug.Log($"───────────────────────────────────────");
        Debug.Log($"Proceeding to next scene: {nextSceneName}");
        Debug.Log($"Selected camera: {GetSelectedCameraName()}");
        Debug.Log($"Camera device index: {selectedCameraIndex}");
        Debug.Log($"───────────────────────────────────────");

        // CRITICAL: Preserve UniCamInterface across scene load
        // This ensures the next scene uses the same camera selection
        if (unicamInterface != null)
        {
            DontDestroyOnLoad(unicamInterface.gameObject);
            Debug.Log($"✓ UniCamInterface persisted across scene load");
        }
        else
        {
            Debug.LogError("❌ Cannot persist UniCamInterface - it is null!");
            return;
        }

        // Load the next scene (additive: false means unload current scene)
        SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }

    /// <summary>
    /// Gets the currently selected camera index
    /// </summary>
    public int GetSelectedCameraIndex()
    {
        return selectedCameraIndex;
    }

    /// <summary>
    /// Gets the currently selected camera name
    /// </summary>
    public string GetSelectedCameraName()
    {
        if (selectedCameraIndex >= 0 && selectedCameraIndex < availableCameras.Count)
            return availableCameras[selectedCameraIndex];
        return "Unknown Camera";
    }

    /// <summary>
    /// Gets the UniCamInterface reference (for external access if needed)
    /// </summary>
    public UniCamInterface GetUniCamInterface()
    {
        return unicamInterface;
    }
}