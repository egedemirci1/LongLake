using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class TestSceneHelper : MonoBehaviour
{
    [System.Serializable]
    public struct NamedPrefab
    {
        public string name;
        public GameObject prefab;
    }

    [Header("NPC Prefabs to Spawn")]
    [SerializeField] private List<NamedPrefab> npcPrefabs = new List<NamedPrefab>();

    [Header("Default Character (0 = Ahu, 1 = Yaman)")]
    [Range(0, 1)]
    [SerializeField] private int defaultCharacterIndex = 0;

    private bool _characterAutoSelected = false;

    private void Start()
    {
        // Auto start Host in TestScene if NetworkManager is present but not running
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[TestSceneHelper] Starting NetworkManager as HOST automatically.");
            NetworkManager.Singleton.StartHost();
        }
    }

    public static bool UnlockCursor = false;

    private void Update()
    {
        // Toggle cursor unlock with Left Alt
        if (Input.GetKeyDown(KeyCode.LeftAlt))
        {
            UnlockCursor = !UnlockCursor;
            if (UnlockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // Try to select default character once local player object is spawned
        if (!_characterAutoSelected && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var controller = localPlayer.GetComponent<PlayerController>();
                if (controller != null)
                {
                    controller.SelectCharacter(defaultCharacterIndex);
                    _characterAutoSelected = true;
                    Debug.Log($"[TestSceneHelper] Automatically selected character index: {defaultCharacterIndex}");
                }
            }
        }
    }

    private void OnGUI()
    {
        // Draw a simple debug window for testing
        GUILayout.BeginArea(new Rect(10, 10, 250, 400));
        var style = new GUIStyle(GUI.skin.box);
        style.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.7f));
        GUILayout.BeginVertical(style);

        GUILayout.Label("<b>TEST SCENE HELPER</b>", GUILayout.Width(230));
        GUILayout.Label("<color=yellow>Press Left Alt to unlock mouse</color>");

        if (NetworkManager.Singleton == null)
        {
            GUILayout.Label("NetworkManager not found in scene!");
            GUILayout.EndVertical();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label($"Status: {(NetworkManager.Singleton.IsServer ? "Host/Server" : (NetworkManager.Singleton.IsClient ? "Client" : "Stopped"))}");

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Start Host"))
            {
                NetworkManager.Singleton.StartHost();
            }
        }
        else
        {
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var controller = localPlayer.GetComponent<PlayerController>();
                if (controller != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>Select Character:</b>");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Ahu (0)"))
                    {
                        controller.SelectCharacter(0);
                    }
                    if (GUILayout.Button("Yaman (1)"))
                    {
                        controller.SelectCharacter(1);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(15);
            GUILayout.Label("<b>Spawn NPC:</b>");
            if (npcPrefabs == null || npcPrefabs.Count == 0)
            {
                GUILayout.Label("No NPC prefabs assigned.");
            }
            else
            {
                foreach (var np in npcPrefabs)
                {
                    if (np.prefab == null) continue;

                    if (GUILayout.Button($"Spawn {np.name}"))
                    {
                        SpawnNpcAtPlayer(np.prefab);
                    }
                }
            }
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void SpawnNpcAtPlayer(GameObject prefab)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TestSceneHelper] Only server/host can spawn NPCs.");
            return;
        }

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (localPlayer != null)
        {
            spawnPos = localPlayer.transform.position + localPlayer.transform.forward * 2.5f;
            spawnRot = Quaternion.LookRotation(-localPlayer.transform.forward);
        }

        GameObject npc = Instantiate(prefab, spawnPos, spawnRot);
        if (npc.TryGetComponent<NetworkObject>(out var netObj))
        {
            netObj.Spawn();
            Debug.Log($"[TestSceneHelper] Spawned networked NPC: {prefab.name}");
        }
        else
        {
            Debug.Log($"[TestSceneHelper] Spawned local NPC: {prefab.name}");
        }
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i)
        {
            pix[i] = col;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }
}
