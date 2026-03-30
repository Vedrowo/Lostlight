using System.Collections;
using UnityEngine;
using TMPro;

public class EscapeCar : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.E;
    public GameObject promptUI;
    public float maxInteractDistance = 3f;
    public float lookAngle = 30f;

    [Header("Escape Screen")]
    public CanvasGroup escapeCanvasGroup;
    public TextMeshProUGUI escapedText;
    public CanvasGroup creditsCanvasGroup;
    public TextMeshProUGUI creditsText;

    [Header("Timing")]
    public float fadeInDuration = 2f;
    public float holdBeforeCredits = 2.5f;
    public float creditsFadeDuration = 2f;

    [Header("References")]
    [Tooltip("Drag the stalker GameObject here.")]
    public StalkerMove stalker;

    Transform playerCam;
    Transform playerBody;
    bool playerInRange = false;
    bool escaped = false;

    void Start()
    {
        if (promptUI != null)
            promptUI.SetActive(false);

        var playerCamComp = FindObjectOfType<PlayerCam>();
        playerCam = playerCamComp != null ? playerCamComp.transform : Camera.main?.transform;

        var pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) playerBody = pm.transform;

        // auto-find stalker if not assigned in inspector
        if (stalker == null)
            stalker = FindObjectOfType<StalkerMove>();

        if (escapeCanvasGroup != null)
        {
            escapeCanvasGroup.alpha = 0f;
            escapeCanvasGroup.gameObject.SetActive(false);
        }

        if (creditsCanvasGroup != null)
            creditsCanvasGroup.alpha = 0f;

        if (creditsText != null && string.IsNullOrEmpty(creditsText.text))
        {
            creditsText.text =
                "A game by Vedran Gvozderac\n\n" +
                "Made with Unity\n\n" +
                "Thank you for playing";
        }
    }

    void Update()
    {
        if (escaped) return;

        // only show prompt once stalker is activated
        if (stalker == null || !stalker.activated)
        {
            if (promptUI != null) promptUI.SetActive(false);
            return;
        }

        playerInRange = false;

        if (playerCam != null && playerBody != null)
        {
            float distance = Vector3.Distance(playerBody.position, transform.position);
            Vector3 dirToCar = (transform.position - playerCam.position).normalized;
            float angle = Vector3.Angle(playerCam.forward, dirToCar);

            if (distance <= maxInteractDistance && angle <= lookAngle)
                playerInRange = true;
        }

        if (promptUI != null)
            promptUI.SetActive(playerInRange);

        if (playerInRange && Input.GetKeyDown(interactKey))
            TriggerEscape();
    }

    void TriggerEscape()
    {
        escaped = true;

        if (promptUI != null) promptUI.SetActive(false);

        var pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        var playerCamComp = FindObjectOfType<PlayerCam>();
        if (playerCamComp != null) playerCamComp.enabled = false;

        var moveCamera = FindObjectOfType<MoveCamera>();
        if (moveCamera != null) moveCamera.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (GameManager.Instance != null)
            GameManager.Instance.SetState(GameState.Escaped);

        StartCoroutine(EscapeRoutine());
    }

    IEnumerator EscapeRoutine()
    {
        if (escapeCanvasGroup != null)
            escapeCanvasGroup.gameObject.SetActive(true);

        if (creditsCanvasGroup != null)
            creditsCanvasGroup.alpha = 0f;

        yield return FadeCanvasGroup(escapeCanvasGroup, 0f, 1f, fadeInDuration);
        yield return new WaitForSeconds(holdBeforeCredits);
        yield return FadeCanvasGroup(creditsCanvasGroup, 0f, 1f, creditsFadeDuration);

        yield return new WaitForSeconds(5f);

        Time.timeScale = 1f;
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;

        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        cg.alpha = to;
    }
}