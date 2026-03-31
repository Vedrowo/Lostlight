using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Ambient Sounds")]
    public AudioSource explorationAmbience;
    public AudioSource chasedAmbience;
    public AudioSource nightAmbience;
    public AudioSource caughtAmbience;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void UpdateAmbience(GameState state)
    {
        // stop all first
        explorationAmbience.Stop();
        chasedAmbience.Stop();
        nightAmbience.Stop();

        // play the correct ambient based on state
        switch (state)
        {
            case GameState.Exploration:
                explorationAmbience.Play();
                break;
            case GameState.Chased:
                chasedAmbience.Play();
                break;
            case GameState.EscapeSequence:
                nightAmbience.Play();
                break;
            case GameState.Caught:
                caughtAmbience.Play();
                break;
        }
    }
}