using UnityEngine;
using System.IO;

public class MapPhoto : MonoBehaviour
{
    public Camera mapCamera;
    public RenderTexture renderTexture;

    public string fileName = "map.png";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) // press P to capture
        {
            CaptureMap();
        }
    }

    void CaptureMap()
    {
        RenderTexture.active = renderTexture;

        Texture2D tex = new Texture2D(
            renderTexture.width,
            renderTexture.height,
            TextureFormat.RGB24,
            false
        );

        tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex.Apply();

        byte[] bytes = tex.EncodeToPNG();

        string path = Path.Combine(Application.dataPath, fileName);
        File.WriteAllBytes(path, bytes);

        Debug.Log("Map saved to: " + path);

        RenderTexture.active = null;
    }
}