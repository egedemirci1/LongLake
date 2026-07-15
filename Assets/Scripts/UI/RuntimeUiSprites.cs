using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime'da üretilen, yeniden kullanılabilir 9-slice yuvarlak köşeli sprite'lar.
/// Kodla kurulan HUD öğeleri editör builtin sprite'larına erişemediği için gerekli.
/// </summary>
public static class RuntimeUiSprites
{
    private static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

    public static Sprite GetRoundedSprite(int radius = 10)
    {
        radius = Mathf.Max(2, radius);
        if (cache.TryGetValue(radius, out var cached) && cached != null)
            return cached;

        int size = radius * 2 + 12;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Köşe merkezine olan mesafeden kenar yumuşatmalı alfa hesapla.
                float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        float border = radius + 2f;
        var sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        sprite.hideFlags = HideFlags.HideAndDontSave;

        cache[radius] = sprite;
        return sprite;
    }
}
