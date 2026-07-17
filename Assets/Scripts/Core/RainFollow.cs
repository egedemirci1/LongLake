using UnityEngine;

/// <summary>
/// Yagmur particle sistemini local oyuncunun kamerasinin uzerinde tutar.
/// Oyuncu Netcode ile runtime'da spawn oldugu icin sahnede sabit referans yoktur;
/// bunun yerine MainCamera tag'li kamera takip edilir.
/// </summary>
public class RainFollow : MonoBehaviour
{
    [SerializeField] private float heightAboveCamera = 9f;
    [SerializeField] private float forwardOffset = 4f;
    [Header("Rain Appearance")]
    [SerializeField] private float emissionRate = 850f;
    [SerializeField] private Vector2 lifetimeRange = new Vector2(1.3f, 1.8f);
    [SerializeField] private Vector2 speedRange = new Vector2(18f, 26f);
    [SerializeField] private Vector2 sizeRange = new Vector2(0.025f, 0.045f);
    [SerializeField] private float windX = 1.2f;
    [SerializeField] private float windZ = 0.4f;

    [Header("Rain Audio")]
    [SerializeField] private AudioClip rainLoopClip;
    [SerializeField] private string rainResourcesPath = "SFX/rain_loop";
    [Range(0f, 1f)]
    [SerializeField] private float rainVolume = 0.42f;
    [Range(0f, 1f)]
    [Tooltip("Kapalı alanda dışarıdaki yağmurun duyulma oranı.")]
    [SerializeField] private float indoorVolumeFactor = 0.12f;

    [Header("Indoor Detection")]
    [Tooltip("Kameradan yukarı bu mesafede çatı/tavan aranır.")]
    [SerializeField] private float roofCheckDistance = 60f;
    [Tooltip("Kapalı alana girince/çıkınca emisyonun sönme-açılma hızı (birim/sn oranı).")]
    [SerializeField] private float emissionFadeSpeed = 3f;
    [SerializeField] private LayerMask roofLayers = ~0;

    private Transform _cam;
    private ParticleSystem _particles;
    private AudioSource _rainAudio;
    private float _emissionFactor = 1f; // 1 = açık alan, 0 = kapalı alan

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        ConfigureRain();
        ConfigureRainAudio();
    }

    private void ConfigureRain()
    {
        var particles = GetComponent<ParticleSystem>();
        if (particles == null) return;

        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 2500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.82f, 0.9f, 0.22f),
            new Color(0.9f, 0.95f, 1f, 0.48f));

        var emission = particles.emission;
        emission.rateOverTime = emissionRate;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(20f, 1f, 20f);

        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = windX;
        velocity.z = windZ;

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;

        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1.8f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enableGPUInstancing = true;
    }

    private void ConfigureRainAudio()
    {
        if (rainLoopClip == null)
            rainLoopClip = Resources.Load<AudioClip>(rainResourcesPath);
        if (rainLoopClip == null)
        {
            Debug.LogWarning($"[Rain] Audio clip bulunamadı: Resources/{rainResourcesPath}");
            return;
        }

        _rainAudio = GetComponent<AudioSource>();
        if (_rainAudio == null)
            _rainAudio = gameObject.AddComponent<AudioSource>();

        _rainAudio.clip = rainLoopClip;
        _rainAudio.loop = true;
        _rainAudio.playOnAwake = false;
        _rainAudio.spatialBlend = 0f;
        _rainAudio.volume = rainVolume;
        _rainAudio.dopplerLevel = 0f;
        _rainAudio.Play();
    }

    private void LateUpdate()
    {
        if (_cam == null)
        {
            var main = Camera.main;
            if (main == null) return; // oyuncu henuz spawn olmadi
            _cam = main.transform;
        }

        // Kameranin biraz onune ve uzerine konumlan (yatay duzlemde)
        Vector3 flatForward = _cam.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        transform.position = _cam.position + flatForward * forwardOffset + Vector3.up * heightAboveCamera;

        UpdateIndoorFade();
    }

    /// <summary>
    /// Kameradan yukarı tek raycast: üstte çatı/tavan varsa yağmur emisyonunu söndürür.
    /// Mevcut damlalar ömrünü tamamlayıp kaybolur; içeri girişte doğal bir geçiş olur.
    /// </summary>
    private void UpdateIndoorFade()
    {
        if (_particles == null) return;

        bool indoors = Physics.Raycast(
            _cam.position, Vector3.up, roofCheckDistance, roofLayers, QueryTriggerInteraction.Ignore);

        float target = indoors ? 0f : 1f;
        _emissionFactor = Mathf.MoveTowards(_emissionFactor, target, emissionFadeSpeed * Time.deltaTime);

        var emission = _particles.emission;
        emission.rateOverTime = emissionRate * _emissionFactor;

        if (_rainAudio != null)
        {
            float volumeFactor = Mathf.Lerp(indoorVolumeFactor, 1f, _emissionFactor);
            _rainAudio.volume = rainVolume * volumeFactor;
        }
    }
}
