using UnityEngine;

/// <summary>
/// Tints notebook mesh materials (e.g. yellow "notebook-sari" reusing green mesh).
/// Safe: clones materials at runtime so the shared green asset is not permanently changed.
/// </summary>
public class NotebookTint : MonoBehaviour
{
    [SerializeField] private Color tintColor = new Color(0.95f, 0.78f, 0.22f, 1f);

    private void Awake()
    {
        ApplyTint();
    }

    [ContextMenu("Apply Tint")]
    public void ApplyTint()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", tintColor);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", tintColor);
                else if (mat.HasProperty("_MainColor"))
                    mat.SetColor("_MainColor", tintColor);
            }

            renderer.materials = mats;
        }
    }
}
