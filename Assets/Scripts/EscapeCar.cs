using UnityEngine;
using TMPro; // if you're using TextMeshPro

public class EscapeCar : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.E;
    public GameObject promptUI; // TMP Text object
    public float maxInteractDistance = 3f; // how close you need to be
    public float lookAngle = 30f; // degrees player must face car

    Transform playerCam;
    bool playerInRange = false;

    void Start()
    {
        if (promptUI != null)
            promptUI.SetActive(false);

        if (Camera.main != null)
            playerCam = Camera.main.transform;
    }

    void Update()
    {
        if (GameManager.Instance.GetState() != GameState.EscapeSequence) return;

        playerInRange = false;

        if (playerCam != null)
        {
            Vector3 dirToCar = transform.position - playerCam.position;
            float distance = dirToCar.magnitude;
            float angle = Vector3.Angle(playerCam.forward, dirToCar);

            // player is close enough AND looking at the car
            if (distance <= maxInteractDistance && angle <= lookAngle)
                playerInRange = true;
        }

        // show/hide prompt
        if (promptUI != null)
            promptUI.SetActive(playerInRange);

        // check input
        if (playerInRange && Input.GetKeyDown(interactKey))
            Escape();
    }

    void Escape()
    {
        Debug.Log("Player escaped!");
        // TODO: Trigger escape screen / animation
    }
}