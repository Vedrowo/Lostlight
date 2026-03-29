using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerHealth : MonoBehaviour
{
    public Volume globalVolume;

    [Header("Death Screen")]
    public CanvasGroup deathCanvasGroup;  // full screen death canvas
    public float fadeInDuration = 1.5f;

    private Vignette vignette;
    private bool isDead = false;
    private Rigidbody mainRigidbody;

    void Awake()
    {
        mainRigidbody = GetComponent<Rigidbody>();

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
        if (Input.GetKeyDown(KeyCode.K)) Die();

        if (isDead && vignette != null)
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, 0.7f, Time.deltaTime * 2f);
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;

        // disable movement
        if (TryGetComponent<PlayerMovement>(out var move)) move.enabled = false;

        // disable camera
        var playerCamComp = FindObjectOfType<PlayerCam>();
        if (playerCamComp != null) playerCamComp.enabled = false;

        var moveCam = FindObjectOfType<MoveCamera>();
        if (moveCam != null) moveCam.enabled = false;

        // fall over
        mainRigidbody.constraints = RigidbodyConstraints.None;
        mainRigidbody.AddRelativeTorque(Vector3.right * 10f, ForceMode.Impulse);

        // vignette
        if (vignette != null)
        {
            vignette.active = true;
            vignette.color.value = Color.black;
        }

        // unlock cursor and show death screen after short delay
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        // brief pause so the fall over is visible before blackout
        yield return new WaitForSeconds(1.2f);

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

    // called by the Try Again button
    public void TryAgain()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}