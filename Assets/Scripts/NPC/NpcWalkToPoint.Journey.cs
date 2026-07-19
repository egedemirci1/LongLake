using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

// Partial: split for maintainability. Type identity unchanged.
public partial class NpcWalkToPoint : NetworkBehaviour
{
    /// <summary>
    /// Kapı, diyalog veya quest gibi herhangi bir sistemden yürüyüşü başlatır.
    /// </summary>
    public void StartJourney()
    {
        if (!IsSpawned) return;

        if (IsServer)
            TryStartJourneyOnServer();
        else
            StartJourneyServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartJourneyServerRpc()
    {
        TryStartJourneyOnServer();
    }

    private void ResolveNpc()
    {
        if (npcOverride != null)
        {
            _npc = npcOverride;
            return;
        }

        GameObject found = GameObject.Find(npcObjectName);
        if (found != null)
            _npc = found.transform;
        else
            Debug.LogError($"[NpcWalkToPoint] NPC '{npcObjectName}' bulunamadı.");
    }

    private void ResolveWatchDoor()
    {
        if (watchDoor != null || !autoStartWhenKnockDoorOpens) return;

        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!door.IsQuestKnockDoor) continue;
            watchDoor = door.transform;
            break;
        }
    }

    private void TryStartFromDoorWatch()
    {
        if (watchDoor == null)
        {
            ResolveWatchDoor();
            if (watchDoor == null) return;
        }

        float z = watchDoor.localEulerAngles.z;
        if (z > 180f) z -= 360f;

        if (Mathf.Abs(Mathf.DeltaAngle(z, watchDoorOpenZ)) <= watchDoorAngleTolerance)
            TryStartJourneyOnServer();
    }

    private void TryStartJourneyOnServer()
    {
        if (!IsServer || journeyStarted.Value) return;
        journeyStarted.Value = true;
    }

    private void OnJourneyStartedChanged(bool previous, bool current)
    {
        if (current && !previous)
            StartCoroutine(BeginJourneyLocalRoutine());
    }

    private System.Collections.IEnumerator BeginJourneyLocalRoutine()
    {
        if (_journeyInitialized) yield break;
        _journeyInitialized = true;

        if (_npc == null)
            ResolveNpc();
        if (_npc == null) yield break;

        // Yürüyüş boyunca oyuncular içinden geçmesin — collider'ı baştan tak
        // (önceden sadece varışta, konuşma etkileşimi için ekleniyordu).
        EnsureNpcHasCollider();

        // Kapı carve'inin NavMesh'e yazılması için kısa bir nefes.
        // (Obstacle kapandıktan sonra Unity carve güncellemesi bir frame sürebilir.)
        for (int i = 0; i < 8; i++)
            yield return null;

        _animator = _npc.GetComponent<Animator>();
        if (_animator != null)
            _animator.applyRootMotion = false;

        _agent = _npc.GetComponent<NavMeshAgent>();
        if (_agent == null)
            _agent = _npc.gameObject.AddComponent<NavMeshAgent>();

        _agent.enabled = false;
        _agent.speed = walkSpeed;
        _agent.acceleration = 5f;
        _agent.angularSpeed = 360f;
        _agent.stoppingDistance = stoppingDistance;
        _agent.autoBraking = true;
        _agent.autoTraverseOffMeshLink = false; // kapı linkini elle yürüterek geç (tek çizgi teleport olmasın)
        _agent.radius = 0.35f;
        _agent.height = 1.8f;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        _journeyElapsed = 0f;
        _isRunning = false;
        ResetRunStamina();

        // Kapı link'ini tazeleyip PathComplete olana kadar dene (Partial = eşikte takılır).
        RefreshOpenQuestDoorLinks();
        for (int i = 0; i < 10; i++)
            yield return null;

        Vector3 target = ResolveDestination();
        // Önce verilen dünya koordinatı; olmazsa terrain snap + geniş yarıçap.
        if (!TrySampleNear(target, navMeshSampleRadius, float.MaxValue, out NavMeshHit targetHit) &&
            !TrySampleNear(target, 40f, float.MaxValue, out targetHit))
        {
            Vector3 grounded = SnapDestinationToGround(target);
            if (!TrySampleNear(grounded, navMeshSampleRadius, float.MaxValue, out targetHit) &&
                !TrySampleNear(grounded, 40f, float.MaxValue, out targetHit))
            {
                Debug.LogError($"[NpcWalkToPoint] Hedef yakınında NavMesh bulunamadı: {target} (grounded: {grounded})");
                yield break;
            }
        }

        // ÖNCE mevcut konumun HEMEN yanındaki mesh — büyük SamplePosition evi dışına ışınlıyordu.
        if (!TryPlaceOnNavMeshNearNpc())
        {
            Debug.LogError(
                $"[NpcWalkToPoint] {_npc.name} NavMesh üzerinde değil ve güvenli snap yok. " +
                "Ev içi zemin bake'te walkable olmalı; aksi halde agent evi dışına atılırdı (engellendi).");
            yield break;
        }

        _agent.enabled = true;
        if (!_agent.isOnNavMesh)
            _agent.Warp(_npc.position);

        bool pathOk = false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (attempt == 5 || attempt == 12)
                RefreshOpenQuestDoorLinks();

            if (_agent.isOnNavMesh && _agent.SetDestination(targetHit.position))
            {
                float wait = 0f;
                while (_agent.pathPending && wait < 1.25f)
                {
                    wait += Time.deltaTime;
                    yield return null;
                }

                if (_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete)
                {
                    pathOk = true;
                    break;
                }
            }

            yield return new WaitForSeconds(0.2f);
        }

        if (!pathOk)
        {
            Debug.LogError(
                $"[NpcWalkToPoint] {_npc.name} için geçerli rota yok (kapı NavMeshLink / eşik). " +
                "Kapı açıkken Door_Group NavMeshLink uçlarının iki mavi adaya oturduğunu kontrol et.");
            _agent.enabled = false;
            yield break;
        }

        _activeJourneyTarget = targetHit.position;
        _hasActiveJourneyTarget = true;
        if (IsServer)
            RequestDestinationRefresh(force: true);
    }

    private static void RefreshOpenQuestDoorLinks()
    {
        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (door == null) continue;
            if (!door.IsNavMeshPassageOpen) continue;
            door.RefreshDoorwayNavMeshLink();
        }
    }

    /// <summary>
    /// NPC'yi NavMesh'e oturtur. Yatayda uzaktaki (evin dışı) noktalara ışınlamaz.
    /// </summary>
    private bool TryPlaceOnNavMeshNearNpc()
    {
        // 1) Mevcut pozisyonun çok yakınında mesh var mı?
        if (TrySampleNear(_npc.position, 0.75f, maxNavMeshSnapDistance, out NavMeshHit localHit))
        {
            _agent.Warp(localHit.position);
            return true;
        }

        // 2) Açık quest kapısı eşiği — kısa mesafe ise oraya oturt (dışarıya rastgele değil).
        Transform door = FindOpenQuestKnockDoor();
        if (door != null)
        {
            Vector3 doorway = door.position + door.forward * 0.6f;
            if (TrySampleNear(doorway, 1.2f, maxDoorwaySnapDistance, out NavMeshHit doorHit))
            {
                float horiz = HorizontalDistance(_npc.position, doorHit.position);
                if (horiz <= maxDoorwaySnapDistance)
                {
                    Debug.LogWarning(
                        $"[NpcWalkToPoint] {_npc.name} içerde mesh bulamadı; kapı eşiğine alındı ({horiz:0.00}m). " +
                        "Kalıcı çözüm: ev içi NavMesh bake.");
                    _agent.Warp(doorHit.position);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TrySampleNear(Vector3 origin, float sampleRadius, float maxHorizontal, out NavMeshHit hit)
    {
        hit = default;
        if (!NavMesh.SamplePosition(origin, out NavMeshHit sampled, sampleRadius, NavMesh.AllAreas))
            return false;

        if (HorizontalDistance(origin, sampled.position) > maxHorizontal)
            return false;

        hit = sampled;
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Transform FindOpenQuestKnockDoor()
    {
        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (door.IsQuestKnockDoor && door.IsNavMeshPassageOpen)
                return door.transform;
        }
        return null;
    }

    private Vector3 ResolveDestination()
    {
        if (destination != null)
            return destination.position;

        if (useYamanSpawnIfNoDestination)
        {
            NetworkLocalSetup setup = FindFirstObjectByType<NetworkLocalSetup>();
            if (setup != null)
                return setup.YamanSpawnPosition;
            return yamanSpawnFallback;
        }

        return carDestination;
    }

    /// <summary>
    /// Spawn/Inspector Y'si yaklaşık; NavMesh SamplePosition küre yarıçapı kullanır.
    /// Önce terrain yüksekliğine oturt ki 4m sample gerçek zemini kaçırmasın.
    /// </summary>
    private static Vector3 SnapDestinationToGround(Vector3 pos)
    {
        float terrainY = SampleTerrainHeight(pos.x, pos.z);
        if (!float.IsNegativeInfinity(terrainY))
            return new Vector3(pos.x, terrainY + 0.1f, pos.z);

        const float rayHeight = 500f;
        Vector3 origin = new Vector3(pos.x, rayHeight, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayHeight + 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Gökyüzü collider'ı yutmasın — hedefe göre makul yükseklik bandı.
            if (Mathf.Abs(hit.point.y - pos.y) < 80f || pos.y < 1f)
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
        }

        return pos;
    }

    private static float SampleTerrainHeight(float worldX, float worldZ)
    {
        float best = float.NegativeInfinity;
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            return best;

        var probe = new Vector3(worldX, 0f, worldZ);
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float lx = worldX - tp.x;
            float lz = worldZ - tp.z;
            if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                continue;

            float h = terrain.SampleHeight(probe) + tp.y;
            if (h > best)
                best = h;
        }

        return best;
    }

}
