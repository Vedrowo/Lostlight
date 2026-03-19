using UnityEngine;

public class FlashlightController : MonoBehaviour
{
    [Header("Assign the Flashlight (Spot Light) here")]
    public Light flashlight;

    private bool _isOn = false;

    void Start()
    {
        // If you forgot to drag it in, this is a safety backup
        if (flashlight == null)
        {
            flashlight = GetComponent<Light>();
        }

        if (flashlight != null)
        {
            flashlight.enabled = _isOn;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            ToggleFlashlight();
        }
    }

    void ToggleFlashlight()
    {
        if (flashlight == null) return;

        _isOn = !_isOn;
        flashlight.enabled = _isOn;
    }
}