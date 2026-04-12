using UnityEngine;

public class PlayerCam : MonoBehaviour
{
    public Transform orientation;

    float xRotation;
    float yRotation;

    void Awake()
    {
        GameSettings.EnsureExists();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * GameSettings.Instance.mouseSensitivityX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * GameSettings.Instance.mouseSensitivityY;

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        orientation.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    public void SetRotation(Quaternion rot)
    {
        Vector3 e = rot.eulerAngles;

        float pitch = e.x;
        if (pitch > 180f) pitch -= 360f;

        xRotation = Mathf.Clamp(pitch, -90f, 90f);
        yRotation = e.y;
        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);

        if (orientation != null)
            orientation.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }
}
