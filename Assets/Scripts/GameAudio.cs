using UnityEngine;

/// <summary>
/// Simple global one-shot SFX player. Loads clips from Resources/SFX/ by default.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Clips (optional overrides)")]
    [SerializeField] private AudioClip itemPickupClip;

    private AudioSource _source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("GameAudio");
        DontDestroyOnLoad(go);
        go.AddComponent<GameAudio>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // 2D UI/feedback sounds

        if (itemPickupClip == null)
            itemPickupClip = Resources.Load<AudioClip>("SFX/item-pickup");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void PlayItemPickup()
    {
        PlayOneShot(itemPickupClip);
    }

    public void PlayOneShot(AudioClip clip, float volume = 1f)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, volume);
    }

    public static void PlayPickup()
    {
        if (Instance != null)
            Instance.PlayItemPickup();
    }
}
