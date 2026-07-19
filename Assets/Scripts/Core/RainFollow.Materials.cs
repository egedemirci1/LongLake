using UnityEngine;

// Partial: split for maintainability. Type identity unchanged.
public partial class RainFollow : MonoBehaviour
{
    private void CreateRainMaterials()
    {
        // Hazır asset material (doğru URP transparent blend) — runtime Shader.Find
        // ile kurulan material'lerde Src/Dst blend çoğu zaman opak kalıyordu.
        var streakAsset = Resources.Load<Material>(rainStreakMaterialPath);
        var splashAsset = Resources.Load<Material>(rainSplashMaterialPath);

        if (streakAsset != null)
        {
            // Asset'i bozmamak için instance; blend'i yine de zorla doğrula.
            _rainStreakMaterial = new Material(streakAsset) { name = streakAsset.name + " (Instance)" };
            _rainSplashMaterial = splashAsset != null
                ? new Material(splashAsset) { name = splashAsset.name + " (Instance)" }
                : _rainStreakMaterial;
            ApplyTransparentAlphaBlend(_rainStreakMaterial);
            if (_rainSplashMaterial != _rainStreakMaterial)
                ApplyTransparentAlphaBlend(_rainSplashMaterial);
            _ownsRainMaterials = true;
            return;
        }

        var streakTex = Resources.Load<Texture2D>(rainStreakTexturePath);
        var splashTex = Resources.Load<Texture2D>(rainSplashTexturePath);
        _rainStreakMaterial = CreateParticleMaterial("RainStreakMat", streakTex);
        _rainSplashMaterial = CreateParticleMaterial("RainSplashMat", splashTex != null ? splashTex : streakTex);
        _ownsRainMaterials = true;

        if (_rainStreakMaterial == null)
            Debug.LogWarning("[Rain] Particle material oluşturulamadı — Default-Particle opak çubuk görünür.");
        else if (streakTex == null)
            Debug.LogWarning($"[Rain] Streak texture bulunamadı: Resources/{rainStreakTexturePath}");
    }

    private static Material CreateParticleMaterial(string name, Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null)
            return null;

        var mat = new Material(shader) { name = name };
        if (texture != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", texture);
        }

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        ApplyTransparentAlphaBlend(mat);
        return mat;
    }

    private static void ApplyTransparentAlphaBlend(Material mat)
    {
        if (mat == null) return;

        // URP: Surface=Transparent + SrcAlpha/OneMinusSrcAlpha — yoksa partikül opak kare/çubuk olur.
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0f); // Alpha
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_AlphaClip"))
            mat.SetFloat("_AlphaClip", 0f);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha"))
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha"))
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

}
