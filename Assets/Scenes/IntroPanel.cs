using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

public class IntroPanel : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject introPanel;
    public TMP_Text introText;

    [Header("Story Lines")]
    [TextArea(3, 10)]
    public string[] storyLines;
    public string gameSceneName;

    [Header("Fade Settings")]
    public float fadeDuration = 0.5f;

    private int currentLine = 0;
    private bool isStarted = false;

    void Awake()
    {
        // Ensure panel is hidden and text is invisible at the very start
        if (introPanel != null) introPanel.SetActive(false);
        Color c = introText.color;
        c.a = 0;
        introText.color = c;
    }

    // This is called by the MenuScript
    public void StartIntro()
    {
        introPanel.SetActive(true);
        isStarted = true;
        currentLine = 0;
        StartCoroutine(FadeTextIn(storyLines[currentLine]));
    }

    void Update()
    {
        if (!isStarted) return;

        // Detect click to progress
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            NextLine();
        }
    }

    void NextLine()
    {
        StopAllCoroutines();
        currentLine++;

        if (currentLine < storyLines.Length)
        {
            StartCoroutine(FadeTextOutAndIn(storyLines[currentLine]));
        }
        else
        {
            StartCoroutine(FadeOutAndLoad());
        }
    }

    IEnumerator FadeTextIn(string line)
    {
        introText.text = line;
        yield return Fade(0, 1);
    }

    IEnumerator FadeTextOutAndIn(string nextLine)
    {
        yield return Fade(1, 0); // Fade current text out
        introText.text = nextLine;
        yield return Fade(0, 1); // Fade new text in
    }

    IEnumerator Fade(float startAlpha, float endAlpha)
    {
        float t = 0f;
        Color c = introText.color;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(startAlpha, endAlpha, t / fadeDuration);
            introText.color = c;
            yield return null;
        }
        c.a = endAlpha;
        introText.color = c;
    }

    IEnumerator FadeOutAndLoad()
    {
        yield return Fade(1, 0);
        SceneManager.LoadScene(gameSceneName);
    }
}