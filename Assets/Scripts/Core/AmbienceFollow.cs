using UnityEngine;

/// <summary>
/// İki katmanlı ortam sesi: dışarıda doğa ambiyansı (ambiance.wav, seamless loop),
/// kapalı alanda düşük bir oda ambiyansı (room-ambient.ogg). Kameradan yukarı raycast ile
/// iç/dış tespit edilir ve iki katman arasında yumuşak crossfade yapılır.
/// Kendi kendine kurulur; sahneye component eklemek gerekmez.
/// </summary>
public sealed class AmbienceFollow : MonoBehaviour
{
    public static AmbienceFollow Instance { get; private set; }

    [Header("Clips (Resources/SFX)")]
    [SerializeField] private string outdoorResourcesPath = "SFX/ambiance";
    [SerializeField] private string indoorResourcesPath = "SFX/room-ambient";

    [Header("Levels")]
    [Range(0f, 1f)]
    // ambiance.wav mean ≈ −48 dB (çok kısık) → dışarıda tama yakın.
    [SerializeField] private float outdoorVolume = 1.0f;
    [Range(0f, 1f)]
    [Tooltip("Dışarıdaki doğa sesinin kapalı alanda duyulma oranı.")]
    [SerializeField] private float outdoorIndoorFactor = 0.1f;
    [Range(0f, 1f)]
    [Tooltip("Kapalı alandaki oda ambiyansının ses seviyesi.")]
    // room-ambient.ogg mean ≈ -28 dB; dış ambiyanstan 20 dB yüksek olduğu için kısık çal.
    [SerializeField] private float indoorHumVolume = 0.08f;

    [Header("Indoor Detection")]
    [Tooltip("Kameradan yukarı bu mesafede çatı/tavan aranır.")]
    [SerializeField] private float roofCheckDistance = 60f;
    [Tooltip("İç/dış geçişinde crossfade hızı (birim/sn).")]
    [SerializeField] private float fadeSpeed = 1.5f;
    [SerializeField] private LayerMask roofLayers = ~0;

    private AudioSource _outdoorSource;
    private AudioSource _indoorSource;
    private Transform _cam;
    private float _indoorFactor; // 0 = tam dışarı, 1 = tam içeri

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("AmbienceFollow");
        DontDestroyOnLoad(go);
        go.AddComponent<AmbienceFollow>();
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

        _outdoorSource = CreateSource(outdoorResourcesPath, "outdoor");
        _indoorSource = CreateSource(indoorResourcesPath, "indoor");
    }

    private AudioSource CreateSource(string resourcesPath, string label)
    {
        var clip = Resources.Load<AudioClip>(resourcesPath);
        if (clip == null)
        {
            // İç mekân uğultusu henüz eklenmemiş olabilir — sessizce atla.
            Debug.LogWarning($"[Ambience] Klip bulunamadı ({label}): Resources/{resourcesPath}");
            return null;
        }

        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = 0f;
        source.Play();
        return source;
    }

    private void Update()
    {
        if (_cam == null)
        {
            var main = Camera.main;
            if (main == null) return; // oyuncu henüz spawn olmadı
            _cam = main.transform;
        }

        bool indoors = Physics.Raycast(
            _cam.position, Vector3.up, roofCheckDistance, roofLayers, QueryTriggerInteraction.Ignore);

        float target = indoors ? 1f : 0f;
        _indoorFactor = Mathf.MoveTowards(_indoorFactor, target, fadeSpeed * Time.deltaTime);

        if (_outdoorSource != null)
        {
            float factor = Mathf.Lerp(1f, outdoorIndoorFactor, _indoorFactor);
            _outdoorSource.volume = AudioSettingsService.ScaleAmbience(outdoorVolume * factor);
        }

        if (_indoorSource != null)
        {
            _indoorSource.volume = AudioSettingsService.ScaleAmbience(indoorHumVolume * _indoorFactor);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
