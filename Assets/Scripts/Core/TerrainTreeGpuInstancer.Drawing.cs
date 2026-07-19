using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Partial: split for maintainability. Type identity unchanged.
public partial class TerrainTreeGpuInstancer : MonoBehaviour
{
    private void DrawCached(LodMesh lod, Matrix4x4[] matrices, int count, bool shadows)
    {
        int offset = 0;
        while (offset < count)
        {
            int chunk = Mathf.Min(MaxInstancesPerBatch, count - offset);
            FlushChunk(lod, matrices, offset, chunk, shadows);
            offset += chunk;
        }
    }

    private void DrawCachedColored(LodMesh lod, Matrix4x4[] matrices, Vector4[] colors, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int chunk = Mathf.Min(MaxInstancesPerBatch, count - offset);
            FlushChunkColored(lod, matrices, colors, offset, chunk);
            offset += chunk;
        }
    }

    private void FlushChunk(LodMesh lod, Matrix4x4[] matrices, int offset, int count, bool shadows)
    {
        System.Array.Copy(matrices, offset, _chunkBuffer, 0, count);

        int subMeshCount = Mathf.Max(1, lod.mesh.subMeshCount);
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            Material mat = lod.materials[Mathf.Min(sub, lod.materials.Length - 1)];
            if (mat == null)
                continue;

            if (!mat.enableInstancing)
                mat.enableInstancing = true;

            var rp = new RenderParams(mat)
            {
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = receiveShadows,
                layer = gameObject.layer,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                camera = null,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            Graphics.RenderMeshInstanced(rp, lod.mesh, sub, _chunkBuffer, count);
        }
    }

    private void FlushChunkColored(LodMesh lod, Matrix4x4[] matrices, Vector4[] colors, int offset, int count)
    {
        System.Array.Copy(matrices, offset, _chunkBuffer, 0, count);
        for (int i = 0; i < count; i++)
            _colorChunk[i] = colors != null ? colors[offset + i] : DarkForestBase;

        // MaterialPropertyBlock: SoftOcc _Color (koyu) × _InstanceColor (hafif çarpan).
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();
        _mpb.Clear();
        _mpb.SetVectorArray("_InstanceColor", _colorChunk);

        int subMeshCount = Mathf.Max(1, lod.mesh.subMeshCount);
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            Material mat = lod.materials[Mathf.Min(sub, lod.materials.Length - 1)];
            if (mat == null)
                continue;

            if (!mat.enableInstancing)
                mat.enableInstancing = true;

            var rp = new RenderParams(mat)
            {
                matProps = _mpb,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                camera = null,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            Graphics.RenderMeshInstanced(rp, lod.mesh, sub, _chunkBuffer, count);
        }
    }

}
