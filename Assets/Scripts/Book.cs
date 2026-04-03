using UnityEngine;
using TMPro;
using System.Collections;

public class Book : MonoBehaviour
{
    [Header("Interaction Settings")]
    public KeyCode interactKey = KeyCode.E;
    public KeyCode exitKey = KeyCode.Escape;
    public GameObject promptUI;
    public float maxInteractDistance = 3f;
    public float lookAngle = 30f;

    [Header("Book Content")]
    public CanvasGroup bookCanvas;
    public TextMeshProUGUI bookText;

    [Header("Audio Settings")]
    public AudioSource screamSource; 
    public AudioSource monsterSource;
    private bool hasBeenRead = false;
    public TextMeshProUGUI helpText;

    Transform playerCam;
    Transform playerBody;
    bool playerInRange = false;

    void Start()
    {
        if (promptUI != null)
            promptUI.SetActive(true);

        var playerCamComp = FindObjectOfType<PlayerCam>();
        playerCam = playerCamComp != null ? playerCamComp.transform : Camera.main?.transform;

        var pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) playerBody = pm.transform;
    }

    void Update()
    {    
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
            TriggerRead();
    }

    void TriggerRead()
    {

        if (bookCanvas != null)
            if (bookCanvas.gameObject.activeSelf)
            {
                playerCam.GetComponent<PlayerCam>().enabled = true;
                playerBody.GetComponent<PlayerMovement>().enabled = true;
                bookCanvas.gameObject.SetActive(false);
                if (!hasBeenRead && screamSource != null)
                {
                    StartCoroutine(ScreamSequence());
                    hasBeenRead = true;
                }
            }
            else
            {
                playerCam.GetComponent<PlayerCam>().enabled = false;
                playerBody.GetComponent<PlayerMovement>().enabled = false;
                bookCanvas.gameObject.SetActive(true);
            }
        
    }

    IEnumerator ScreamSequence()
    {
        yield return new WaitForSeconds(1.5f);

        if (screamSource != null)
            screamSource.Play();

        yield return new WaitForSeconds(1.0f);
        if (monsterSource != null)
            monsterSource.Play();

        yield return new WaitForSeconds(4.5f);

        if (helpText != null)
        {
            helpText.text = "I need to help them...";
            helpText.gameObject.SetActive(true);

            yield return new WaitForSeconds(3.0f);
            helpText.gameObject.SetActive(false);
        }
    }
}
