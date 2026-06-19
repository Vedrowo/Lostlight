using UnityEngine;

public class MapToggle : MonoBehaviour
{
    public GameObject mapPanel;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            mapPanel.SetActive(!mapPanel.activeSelf);
        }
    }
}