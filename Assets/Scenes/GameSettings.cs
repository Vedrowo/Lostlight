using UnityEngine;

public class GameSettings : MonoBehaviour
{
    public static GameSettings Instance { get; private set; }

    public float masterVolume = 1f;
    public float mouseSensitivityX = 200f;
    public float mouseSensitivityY = 150f;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Add this static method
    public static void EnsureExists()
    {
        if (Instance == null)
        {
            new GameObject("_GameSettings").AddComponent<GameSettings>();
        }
    }
}