using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [Header("References")]
    public GameObject pauseMenuCanvas;
    public string mainMenuScene;

    [Header("Sliders")]
    public Slider volumeSlider;
    public Slider sensitivityXSlider;
    public Slider sensitivityYSlider;

    bool isPaused = false;

    void Update()
    {
        // only allow pausing during gameplay states
        if (GameManager.Instance != null)
        {
            var state = GameManager.Instance.GetState();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) Resume();
            else Pause();
        }
    }

    void Pause()
    {
        isPaused = true;
        pauseMenuCanvas.SetActive(true);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        isPaused = false;
        pauseMenuCanvas.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void Restart()
    {
        Time.timeScale = 1f; // always reset before loading
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public GameObject settingsPanel; // assign your settings UI panel here
    public void ToggleSettings()
    {
        if (settingsPanel != null)
        {
            bool opening = !settingsPanel.activeSelf;
            settingsPanel.SetActive(opening);

            if (opening)
                InitSliders();
        }
    }

    void InitSliders()
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

    public void MainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuScene);
    }

    public void Quit()
    {
        Time.timeScale = 1f;
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}