using UnityEngine;

public class PlayerMapMarker : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public RectTransform mapImage;
    public RectTransform rect;

    [Header("Terrain (MANUAL)")]
    public Terrain targetTerrain;

    private Vector3 terrainPos;
    private Vector3 terrainSize;

    void Start()
    {
        if (targetTerrain == null)
        {
            Debug.LogError("No terrain assigned to PlayerMapMarker!");
            return;
        }

        terrainPos = targetTerrain.transform.position;
        terrainSize = targetTerrain.terrainData.size;
    }

    void Update()
    {
        if (targetTerrain == null) return;

        Vector3 p = player.position;

        float normalizedX = (p.x - terrainPos.x) / terrainSize.x;
        float normalizedY = (p.z - terrainPos.z) / terrainSize.z;

        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        Vector2 size = mapImage.rect.size;

        rect.anchoredPosition = new Vector2(
            (normalizedX - 0.5f) * size.x,
            (normalizedY - 0.5f) * size.y
        );
        Debug.Log($"X: {normalizedX}, Y: {normalizedY}");
    }
}