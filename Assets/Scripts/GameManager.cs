using UnityEngine;

public enum GameState
{
    Exploration,
    Chased,
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

    [Header("Time of Day")]
    [Tooltip("Time set when in Exploration state.")]
    public float explorationTime = 16f;
    [Tooltip("Time set when player wakes up (GettingUp onwards).")]
    public float nightTime = 24f;
    [Tooltip("How long the time transition takes in seconds.")]
    public float timeTrasitionDuration = 3f;

    SunlightControl sunlight;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        sunlight = FindObjectOfType<SunlightControl>();
        // set initial time immediately with no transition
        if (sunlight != null)
            sunlight.SetTimeOfDay(explorationTime);
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
                case GameState.Caught:
                case GameState.Dragging:
                case GameState.Blackout:
                    // still daytime during all pre-capture states
                    sunlight.SetTimeOfDay(explorationTime, 0f);
                    break;

                case GameState.GettingUp:
                    // transition to night as player wakes up
                    sunlight.SetTimeOfDay(nightTime, timeTrasitionDuration);
                    break;

                case GameState.EscapeSequence:
                case GameState.Escaped:
                    // already night, no change needed
                    break;
            }
        }
    }

    public GameState GetState()
    {
        return currentState;
    }
}