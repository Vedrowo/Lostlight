using UnityEngine;

public class PlayerCam : MonoBehaviour
{
    public float sensX;
    public float sensY;

    public Transform orientation;

    float xRotation;
    float yRotation;


    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        orientation.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    // Force the internal rotation state and transform to the given rotation.
    // This keeps internal xRotation/yRotation in sync when CaptureSequence manipulates camera.
    public void SetRotation(Quaternion rot)
    {
        Vector3 e = rot.eulerAngles;
        // convert Unity euler (0..360) to signed pitch in -180..180 so clamping behaves consistently
        float pitch = e.x;
        if (pitch > 180f) pitch -= 360f;

        xRotation = Mathf.Clamp(pitch, -90f, 90f);
        yRotation = e.y;
        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);

        if (orientation != null)
            orientation.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }
}
