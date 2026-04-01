using UnityEngine;

public enum GameState
{
    Exploration,
    Chased,
    StalkerSearching,
    Caught,
    Dragging,
    Blackout,
    GettingUp,
    EscapeSequence,
    Escaped
}

public class GameManager : MonoBehaviour
{
    public bool hasBeenCaught = false;
    public static GameManager Instance;
    public GameState currentState = GameState.Exploration;
    public AudioManager audioManager;

    [Header("Time of Day")]
    [Tooltip("Starting time in Exploration.")]
    public float explorationStartTime = 15f;
    [Tooltip("Maximum time during Exploration — cycle stops here.")]
    public float explorationMaxTime = 24f;
    [Tooltip("Time set when player wakes up after being caught.")]
    public float nightTime = 24f;
    [Tooltip("How long the day-to-night transition takes in seconds.")]
    public float timeTransitionDuration = 3f;

    SunlightControl sunlight;
    bool timeCapped = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        sunlight = FindObjectOfType<SunlightControl>();
        if (sunlight != null)
        {
            sunlight.SetTimeOfDay(explorationStartTime);
            sunlight.autoCycle = true;
        }
    }

    void Update()
    {
        if (timeCapped || sunlight == null) return;

        if ((currentState == GameState.Exploration || currentState == GameState.Chased)
            && sunlight.timeOfDay >= explorationMaxTime)
        {
            sunlight.autoCycle = false;
            sunlight.SetTimeOfDay(explorationMaxTime);
            timeCapped = true;
        }
    }

    public void SetState(GameState newState)
    {
        currentState = newState;
        Debug.Log("Game State changed to: " + newState);

        if (sunlight == null)
            sunlight = FindObjectOfType<SunlightControl>();

        if (sunlight != null)
        {
            switch (newState)
            {
                case GameState.Exploration:
                case GameState.Chased:
                    if (!timeCapped)
                        sunlight.autoCycle = true;
                    break;

                case GameState.Caught:
                case GameState.Dragging:
                case GameState.Blackout:
                    // freeze time during capture cinematic
                    sunlight.autoCycle = false;
                    break;

                case GameState.GettingUp:
                    // transition to night as player wakes
                    sunlight.autoCycle = false;
                    sunlight.SetTimeOfDay(nightTime, timeTransitionDuration);
                    break;

                case GameState.EscapeSequence:
                case GameState.Escaped:
                    break;
            }
        }

        if (AudioManager.Instance != null &&
        (newState == GameState.Exploration ||
        newState == GameState.Chased ||
        newState == GameState.EscapeSequence ||
        newState == GameState.Dragging))
        {
            AudioManager.Instance.UpdateAmbience(newState);
        }
    }

    public GameState GetState() => currentState;
}