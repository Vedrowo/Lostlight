using UnityEngine;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Ambient Sounds")]
    public AudioSource explorationAmbience;
    public AudioSource chasedAmbience;
    public AudioSource nightAmbience;
    public AudioSource draggingAmbience;

    [Header("Fade Settings")]
    public float fadeDuration = 1.5f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void UpdateAmbience(GameState state)
    {
        AudioSource newAmbience = null;

        switch (state)
        {
            case GameState.Exploration:
                newAmbience = explorationAmbience;
                break;
            case GameState.Chased:
                newAmbience = chasedAmbience;
                break;
            case GameState.EscapeSequence:
                newAmbience = nightAmbience;
                break;
            case GameState.Dragging:
                newAmbience = draggingAmbience;
                break;
        }

        // Fade out everything EXCEPT the one we want to play
        FadeOutIfNot(newAmbience, explorationAmbience);
        FadeOutIfNot(newAmbience, chasedAmbience);
        FadeOutIfNot(newAmbience, nightAmbience);
        FadeOutIfNot(newAmbience, draggingAmbience);

        // Play the new one (no fade-in)
        if (newAmbience != null && !newAmbience.isPlaying)
        {
            newAmbience.Play();
        }
    }

    IEnumerator FadeOut(AudioSource source, float duration)
    {
        float startVolume = source.volume;

        while (source.volume > 0)
        {
            source.volume -= startVolume * Time.deltaTime / duration;
            yield return null;
        }

        source.Stop();
        source.volume = startVolume; 
    }

    void FadeOutIfNot(AudioSource target, AudioSource source)
    {
        if (source != target && source.isPlaying)
        {
            StartCoroutine(FadeOut(source, fadeDuration));
        }
    }
}