using UnityEngine;

public class WaterFlow : MonoBehaviour
{
    // Hýz ayarlarýný buradan yapacaðýz
    public float scrollSpeedX = 0.05f;
    public float scrollSpeedY = 0.05f;

    private Renderer rend;

    void Start()
    {
        // Objenin üzerindeki Renderer bileþenini al
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        // Zamanla artan bir kaydýrma deðeri hesapla
        float offsetX = Time.time * scrollSpeedX;
        float offsetY = Time.time * scrollSpeedY;

        // Materyalin ofsetini (konumunu) deðiþtir
        if (rend != null)
        {
            rend.material.mainTextureOffset = new Vector2(offsetX, offsetY);
        }
    }
}