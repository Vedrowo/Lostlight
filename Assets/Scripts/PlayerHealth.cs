using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerHealth : MonoBehaviour
{
    public Volume globalVolume;

    [Header("Health Settings")]
    public int maxHealth = 3;
    private int currentHealth;

    [Header("Hit Flash Effect")]
    public float hitVignetteIntensity = 0.5f;
    public float hitVignetteDuration = 0.4f;

    [Header("Death Screen")]
    public CanvasGroup deathCanvasGroup;
    public float fadeInDuration = 1.5f;

    private Vignette vignette;
    private bool isDead = false;
    private bool isFlashing = false;
    private Rigidbody mainRigidbody;

    void Awake()
    {
        mainRigidbody = GetComponent<Rigidbody>();
        currentHealth = maxHealth;

        if (globalVolume != null && globalVolume.profile.TryGet(out Vignette v))
            vignette = v;

        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.alpha = 0f;
            deathCanvasGroup.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K)) TakeDamage(1);

        if (isDead && vignette != null)
            vignette.intensity.value = Mathf.MoveTowards(
                vignette.intensity.value, 0.7f, Time.deltaTime * 0.5f);
    }

    public void TakeDamage(int amount)
    {
        if (isDead) return;

        currentHealth -= amount;
        Debug.Log($"[PlayerHealth] Hit! Health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
        else
        {
            // Play a quick red vignette flash to signal damage
            if (!isFlashing)
                StartCoroutine(HitFlash());
        }
    }

    IEnumerator HitFlash()
    {
        isFlashing = true;

        if (vignette != null)
        {
            vignette.active = true;
            vignette.color.value = Color.red;
            vignette.intensity.value = hitVignetteIntensity;
        }

        yield return new WaitForSeconds(hitVignetteDuration);

        if (vignette != null && !isDead)
        {
            // Fade vignette back out
            float elapsed = 0f;
            float startIntensity = vignette.intensity.value;
            while (elapsed < hitVignetteDuration)
            {
                vignette.intensity.value = Mathf.Lerp(startIntensity, 0f, elapsed / hitVignetteDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            vignette.intensity.value = 0f;
            vignette.active = false;
        }

        isFlashing = false;
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;

        // Disable movement
        if (TryGetComponent<PlayerMovement>(out var move)) move.enabled = false;

        // Disable camera
        var playerCamComp = FindObjectOfType<PlayerCam>();
        if (playerCamComp != null) playerCamComp.enabled = false;
        var moveCam = FindObjectOfType<MoveCamera>();
        if (moveCam != null) moveCam.enabled = false;

        // Fall over
        mainRigidbody.constraints = RigidbodyConstraints.None;
        mainRigidbody.AddRelativeTorque(Vector3.right * 10f, ForceMode.Impulse);

        // Start death vignette (switch to black)
        if (vignette != null)
        {
            vignette.active = true;
            vignette.color.value = Color.black;
        }

        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(1.2f);

        Debug.Log($"[PlayerHealth] DeathRoutine: deathCanvasGroup={deathCanvasGroup}");

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.gameObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                deathCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            deathCanvasGroup.alpha = 1f;
        }
    }

    public void TryAgain()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}