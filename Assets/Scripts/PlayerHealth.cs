using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerHealth : MonoBehaviour
{
    public Volume globalVolume; // Drag your Global Volume here in the Inspector
    private Vignette vignette;
    private bool isDead = false;
    private Rigidbody mainRigidbody;

    void Awake()
    {
        mainRigidbody = GetComponent<Rigidbody>();
        // Try to find the Vignette setting in your Global Volume
        if (globalVolume != null && globalVolume.profile.TryGet(out Vignette v))
        {
            vignette = v;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K)) Die();

        // Smoothly increase the blackout if we are dead
        if (isDead && vignette != null)
        {
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, 0.7f, Time.deltaTime * 2f);
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;

        // 1. Kill Movement
        if (TryGetComponent<PlayerMovement>(out var move)) move.enabled = false;

        // 2. Fall Over
        mainRigidbody.constraints = RigidbodyConstraints.None;
        mainRigidbody.AddRelativeTorque(Vector3.right * 10f, ForceMode.Impulse);

        // 3. Kill Camera Control (but let it fall with the body)
        var cam = GetComponentInChildren<PlayerCam>();
        if (cam != null) cam.enabled = false;

        // 4. Start the Blackout
        if (vignette != null)
        {
            vignette.active = true;
            vignette.color.value = Color.black;
        }
    }
}