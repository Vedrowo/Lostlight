using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class menuScript : MonoBehaviour
{
    [Header("UI Panels")]
    public IntroPanel introPanelScript;

    [Header("UI Sliders")]
    public Slider volumeSlider;
    public Slider sensitivityXSlider;
    public Slider sensitivityYSlider;

    void Start()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveAllListeners();
            volumeSlider.SetValueWithoutNotify(GameSettings.Instance.masterVolume);
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
        if (sensitivityXSlider != null)
        {
            sensitivityXSlider.onValueChanged.RemoveAllListeners();
            sensitivityXSlider.SetValueWithoutNotify(GameSettings.Instance.mouseSensitivityX);
            sensitivityXSlider.onValueChanged.AddListener(SetSensitivityX);
        }
        if (sensitivityYSlider != null)
        {
            sensitivityYSlider.onValueChanged.RemoveAllListeners();
            sensitivityYSlider.SetValueWithoutNotify(GameSettings.Instance.mouseSensitivityY);
            sensitivityYSlider.onValueChanged.AddListener(SetSensitivityY);
        }
    }

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

    public void SetVolume(float volume)
    {
        GameSettings.Instance.masterVolume = volume;
        AudioListener.volume = volume;
    }

    public void SetSensitivityX(float sensitivity)
    {
        GameSettings.Instance.mouseSensitivityX = sensitivity;
    }

    public void SetSensitivityY(float sensitivity)
    {
        GameSettings.Instance.mouseSensitivityY = sensitivity;
    }
}