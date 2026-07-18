using UnityEngine;

/// <summary>
/// Yagmur particle sistemini local oyuncunun kamerasinin uzerinde tutar.
/// Oyuncu Netcode ile runtime'da spawn oldugu icin sahnede sabit referans yoktur;
/// bunun yerine MainCamera tag'li kamera takip edilir.
/// </summary>
public class RainFollow : MonoBehaviour
{
    [SerializeField] private float heightAboveCamera = 9f;
    [Tooltip("0 = yağmur alanı oyuncuyu merkezler; >0 bakış yönüne kaydırır (dönünce arkada boşluk bırakır).")]
    [SerializeField] private float forwardOffset = 0f;
    [Header("Rain Appearance")]
    [Tooltip("Karakterin çevresinde yağmur yağan yatay yarıçap (metre).")]
    [SerializeField] private float rainRadius = 8f;
    [SerializeField] private float emissionRate = 950f;
    [SerializeField] private Vector2 lifetimeRange = new Vector2(1.3f, 1.8f);
    [SerializeField] private Vector2 speedRange = new Vector2(18f, 26f);
    [SerializeField] private Vector2 sizeRange = new Vector2(0.025f, 0.045f);
    [SerializeField] private float windX = 1.2f;
    [SerializeField] private float windZ = 0.4f;

    [Header("Rain Splash")]
    [SerializeField] private bool enableGroundSplashes = true;
    [SerializeField] private LayerMask rainCollisionLayers = ~0;
    [SerializeField] private Vector2 splashLifetimeRange = new Vector2(0.12f, 0.24f);
    [SerializeField] private Vector2 splashSizeRange = new Vector2(0.025f, 0.06f);

    [Header("Rain Audio")]
    [SerializeField] private AudioClip rainLoopClip;
    [SerializeField] private string rainResourcesPath = "SFX/rain_loop";
    [Range(0f, 1f)]
    // rain_loop.wav mean ≈ −19 dB; keep as soft bed under gameplay SFX.
    [SerializeField] private float rainVolume = 0.24f;
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
        main.maxParticles = 3500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.82f, 0.9f, 0.22f),
            new Color(0.9f, 0.95f, 1f, 0.48f));

        var emission = particles.emission;
        emission.rateOverTime = emissionRate;

        var shape = particles.shape;
        // Box şekli damlaları local +Z'ye fırlatır; Rain objesi X'te 90° döndürüldüğü için
        // bu dünya-aşağı demektir. Aynı dönüş yüzünden yatay taban local X-Y düzlemidir:
        // scale = (genişlik, derinlik, dikey kalınlık) olarak verilmeli.
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(rainRadius * 2f, rainRadius * 2f, 1f);

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

        if (enableGroundSplashes)
            ConfigureGroundSplashes(particles, renderer.sharedMaterial);
    }

    private void ConfigureGroundSplashes(ParticleSystem rain, Material rainMaterial)
    {
        var collision = rain.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        // Low/Medium kalite yalnızca statik geometri önbelleğiyle çarpışır ve damlaları
        // sık sık ıskalar; High her parçacık için gerçek raycast yapar (n≈2-3k, job'larda).
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.collidesWith = rainCollisionLayers;
        collision.dampen = 0f;
        collision.bounce = 0f;
        collision.lifetimeLoss = 1f;
        collision.radiusScale = 0.3f;
        collision.maxCollisionShapes = 64;
        collision.sendCollisionMessages = false;

        Transform existing = transform.Find("RainSplash");
        ParticleSystem splash;
        if (existing != null && existing.TryGetComponent(out splash))
        {
            // Domain reload kapalıyken tekrar alt sistem üretme.
        }
        else
        {
            GameObject splashObject = new GameObject("RainSplash", typeof(ParticleSystem));
            splashObject.transform.SetParent(transform, false);
            splashObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            splash = splashObject.GetComponent<ParticleSystem>();
        }

        // Yeni ParticleSystem oluşur oluşmaz çalmaya başlar; duration gibi ana
        // ayarlar ancak sistem tamamen durmuşken değiştirilebilir.
        splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var splashMain = splash.main;
        splashMain.loop = false;
        splashMain.playOnAwake = false;
        splashMain.duration = 0.3f;
        splashMain.simulationSpace = ParticleSystemSimulationSpace.World;
        splashMain.maxParticles = 700;
        splashMain.startLifetime = new ParticleSystem.MinMaxCurve(
            splashLifetimeRange.x, splashLifetimeRange.y);
        splashMain.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
        splashMain.startSize = new ParticleSystem.MinMaxCurve(
            splashSizeRange.x, splashSizeRange.y);
        splashMain.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.82f, 0.9f, 0.3f),
            new Color(0.92f, 0.96f, 1f, 0.6f));
        splashMain.gravityModifier = 1.2f;

        var splashEmission = splash.emission;
        splashEmission.enabled = true;
        splashEmission.rateOverTime = 0f;
        splashEmission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 2, 4)
        });

        var splashShape = splash.shape;
        splashShape.enabled = true;
        splashShape.shapeType = ParticleSystemShapeType.Hemisphere;
        splashShape.radius = 0.04f;

        var splashRenderer = splash.GetComponent<ParticleSystemRenderer>();
        splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        splashRenderer.sharedMaterial = rainMaterial;
        splashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        splashRenderer.receiveShadows = false;
        splashRenderer.enableGPUInstancing = true;

        var subEmitters = rain.subEmitters;
        subEmitters.enabled = true;
        bool alreadyAdded = false;
        for (int i = 0; i < subEmitters.subEmittersCount; i++)
        {
            if (subEmitters.GetSubEmitterSystem(i) == splash)
            {
                alreadyAdded = true;
                break;
            }
        }

        if (!alreadyAdded)
        {
            subEmitters.AddSubEmitter(
                splash,
                ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritNothing);
        }
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
        _rainAudio.volume = AudioSettingsService.ScaleAmbience(rainVolume);
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
            _rainAudio.volume = AudioSettingsService.ScaleAmbience(
                rainVolume * volumeFactor);
        }
    }
}
