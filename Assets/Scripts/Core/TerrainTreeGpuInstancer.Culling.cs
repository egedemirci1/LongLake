using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Partial: split for maintainability. Type identity unchanged.
public partial class TerrainTreeGpuInstancer : MonoBehaviour
{
    private void RebuildCullListsFromGrid(Vector3 camPos)
    {
        float nearMaxDist = maxDrawDistance;
        float nearMaxDistSq = nearMaxDist * nearMaxDist;
        float bgMaxDist = backgroundMaxDrawDistance > 0.01f ? backgroundMaxDrawDistance : maxDrawDistance;
        float bgMaxDistSq = bgMaxDist * bgMaxDist;
        float lodDistSq = lodSwitchDistance * lodSwitchDistance;
        float shadowDistSq = shadowDistance * shadowDistance;
        bool useShadows = castShadows;
        int nearFullLodBudget = Mathf.Max(0, maxNearFullLod);
        int bgBudget = Mathf.Max(1023, maxBackgroundDraw);

        float cullRadius = Mathf.Max(nearMaxDist, bgMaxDist);
        int camCx = Mathf.Clamp(Mathf.FloorToInt((camPos.x - _gridOriginX) * _invCellSize), 0, _gridW - 1);
        int camCz = Mathf.Clamp(Mathf.FloorToInt((camPos.z - _gridOriginZ) * _invCellSize), 0, _gridH - 1);
        int maxRing = Mathf.CeilToInt(cullRadius * _invCellSize) + 1;

        float camX = camPos.x;
        float camY = camPos.y;
        float camZ = camPos.z;

        for (int b = 0; b < _batches.Length; b++)
        {
            PrototypeBatch batch = _batches[b];
            if (batch.matrices == null || batch.cellStart == null)
                continue;

            int s0 = 0, s1 = 0, s2 = 0;
            int candCount = 0;
            Matrix4x4[] buf0 = batch.lod0Shadow;
            Matrix4x4[] buf1 = batch.lod0NoShadow;
            Matrix4x4[] buf2 = batch.lod1Visible;
            Vector4[] colorBuf = batch.lod1Colors;
            Vector4[] colors = batch.colors;
            Vector3[] positions = batch.positions;
            Matrix4x4[] matrices = batch.matrices;
            int[] cellStart = batch.cellStart;
            int[] cellTrees = batch.cellTreeIndices;
            bool isBackground = batch.isBackground;
            float maxDistSq = isBackground ? bgMaxDistSq : nearMaxDistSq;
            bool tintBg = isBackground && colorBuf != null && colors != null && backgroundColorVariation > 0.001f;
            bool tintNear = !isBackground && colorBuf != null && colors != null;

            // Kamera hücresinden dışa doğru halka — yakın orman önce dolar, bütçe aşımında uzak kesilir.
            for (int ring = 0; ring <= maxRing; ring++)
            {
                if (isBackground && s2 >= bgBudget)
                    break;

                int z0 = camCz - ring;
                int z1 = camCz + ring;
                int x0 = camCx - ring;
                int x1 = camCx + ring;

                for (int cz = z0; cz <= z1; cz++)
                {
                    if (cz < 0 || cz >= _gridH)
                        continue;
                    int row = cz * _gridW;
                    bool onZEdge = cz == z0 || cz == z1;

                    for (int cx = x0; cx <= x1; cx++)
                    {
                        if (cx < 0 || cx >= _gridW)
                            continue;
                        // İç halkadaki hücreleri tekrar tarama.
                        if (ring > 0 && !onZEdge && cx != x0 && cx != x1)
                            continue;

                        if (isBackground && s2 >= bgBudget)
                            break;

                        int cell = row + cx;
                        int start = cellStart[cell];
                        int end = cellStart[cell + 1];
                        for (int t = start; t < end; t++)
                        {
                            int i = cellTrees[t];
                            float dx = positions[i].x - camX;
                            float dy = positions[i].y - camY;
                            float dz = positions[i].z - camZ;
                            float distSq = dx * dx + dy * dy + dz * dz;
                            if (distSq > maxDistSq)
                                continue;

                            if (isBackground)
                            {
                                if (s2 >= bgBudget)
                                    break;
                                buf2[s2] = matrices[i];
                                if (tintBg)
                                    colorBuf[s2] = colors[i];
                                s2++;
                                continue;
                            }

                            if (distSq <= lodDistSq && candCount < _nearCandidates.Length)
                            {
                                _nearCandidates[candCount++] = new NearCandidate { index = i, distSq = distSq };
                            }
                            else
                            {
                                buf2[s2] = matrices[i];
                                if (tintNear)
                                    colorBuf[s2] = colors[i];
                                s2++;
                            }
                        }
                    }
                }
            }

            // En yakın N ağaç LOD0; kalanı billboard — yüksek-poly bütçesi.
            if (!isBackground && candCount > 0)
            {
                int keep = Mathf.Min(candCount, nearFullLodBudget);
                if (candCount > keep)
                    System.Array.Sort(_nearCandidates, 0, candCount, NearCandidateComparer.Instance);

                for (int c = 0; c < candCount; c++)
                {
                    int i = _nearCandidates[c].index;
                    float distSq = _nearCandidates[c].distSq;
                    if (c < keep)
                    {
                        if (useShadows && distSq <= shadowDistSq)
                            buf0[s0++] = matrices[i];
                        else
                            buf1[s1++] = matrices[i];
                    }
                    else
                    {
                        buf2[s2] = matrices[i];
                        if (tintNear)
                            colorBuf[s2] = colors[i];
                        s2++;
                    }
                }
            }

            batch.lod0ShadowCount = s0;
            batch.lod0NoShadowCount = s1;
            batch.lod1Count = s2;
            _batches[b] = batch;
        }
    }

    private void BuildGridForTrees(Vector3[] positions, int cellCount, out int[] cellStart, out int[] cellTreeIndices)
    {
        int[] counts = new int[cellCount];
        int n = positions.Length;

        for (int i = 0; i < n; i++)
        {
            int cell = CellIndex(positions[i].x, positions[i].z);
            counts[cell]++;
        }

        cellStart = new int[cellCount + 1];
        int sum = 0;
        for (int c = 0; c < cellCount; c++)
        {
            cellStart[c] = sum;
            sum += counts[c];
        }

        cellStart[cellCount] = sum;
        cellTreeIndices = new int[n];
        System.Array.Clear(counts, 0, counts.Length);

        for (int i = 0; i < n; i++)
        {
            int cell = CellIndex(positions[i].x, positions[i].z);
            int slot = cellStart[cell] + counts[cell]++;
            cellTreeIndices[slot] = i;
        }
    }

    private int CellIndex(float worldX, float worldZ)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt((worldX - _gridOriginX) * _invCellSize), 0, _gridW - 1);
        int cz = Mathf.Clamp(Mathf.FloorToInt((worldZ - _gridOriginZ) * _invCellSize), 0, _gridH - 1);
        return cz * _gridW + cx;
    }

}
