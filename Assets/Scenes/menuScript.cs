using UnityEngine;
using UnityEngine.SceneManagement;

public class menuScript : MonoBehaviour
{
    [Header("Settings")]
    public float masterVolume = 1f;
    public float mouseSensitivity = 1f;

    [Header("UI Panels")]
    public IntroPanel introPanelScript;

    // Called by Play button
    public void PlayGame()
    {
        // Show intro panel
        introPanelScript.StartIntro();
    }

    // Called by Quit button
    public void QuitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // stops play mode in editor
        #else
        Application.Quit(); // quits build
        #endif
    }

    // Called by Settings button (toggle something or open panel)
    public GameObject settingsPanel; // assign your settings UI panel here
    public void ToggleSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    // Example for slider changes
    public void SetVolume(float volume)
    {
        masterVolume = volume;
        // you can hook this into AudioListener or AudioMixer later
        AudioListener.volume = masterVolume;
    }

    public void SetSensitivity(float sensitivity)
    {
        mouseSensitivity = sensitivity;
        // store this for your player controller later
    }
}