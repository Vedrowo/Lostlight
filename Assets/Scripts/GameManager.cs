using UnityEngine;

public enum GameState
{
    Exploration,
    Chased,
    Caught,
    Dragging,
    Blackout,
    GettingUp,     // NEW: player wakes and performs a getting-up sequence
    EscapeSequence
}

public class GameManager : MonoBehaviour
{
    public bool hasBeenCaught = false;

    public static GameManager Instance;

    public GameState currentState = GameState.Exploration;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void SetState(GameState newState)
    {
        currentState = newState;
        Debug.Log("Game State changed to: " + newState);
    }

    public GameState GetState()
    {
        return currentState;
    }
    }