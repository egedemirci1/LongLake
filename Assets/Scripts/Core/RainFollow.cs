using UnityEngine;

/// <summary>
/// Yağmur: yakın katman kamerayı takip eder ve içeride söner;
/// distant katman içeride çatı üstüne kayar (pencereden dışarıda görünür).
/// Oyuncu Netcode ile runtime'da spawn olduğu için MainCamera takip edilir.
/// </summary>
public class RainFollow : MonoBehaviour
{
    [SerializeField] private float heightAboveCamera = 9f;
    [Tooltip("0 = yağmur alanı oyuncuyu merkezler; >0 bakış yönüne kaydırır (dönünce arkada boşluk bırakır).")]
    [SerializeField] private float forwardOffset = 0f;
    [Header("Rain Appearance")]
    [Tooltip("Karakterin çevresinde yağmur yağan yatay yarıçap (metre).")]
    [SerializeField] private float rainRadius = 16f;
    [SerializeField] private float emissionRate = 1300f;
    [SerializeField] private Vector2 lifetimeRange = new Vector2(1.3f, 1.8f);
    [SerializeField] private Vector2 speedRange = new Vector2(20f, 30f);
    [SerializeField] private Vector2 sizeRange = new Vector2(0.03f, 0.055f);
    [SerializeField] private float windX = 1.2f;
    [SerializeField] private float windZ = 0.4f;
    [SerializeField] private string rainStreakMaterialPath = "VFX/M_RainStreak";
    [SerializeField] private string rainSplashMaterialPath = "VFX/M_RainSplash";
    [SerializeField] private string rainStreakTexturePath = "VFX/T_RainStreak";
    [SerializeField] private string rainSplashTexturePath = "VFX/T_RainSplash";

    [Header("Distant Rain")]
    [Tooltip("Pencereden görünen uzak yağmur yarıçapı (metre).")]
    [SerializeField] private float distantRadius = 100f;
    [SerializeField] private float distantEmissionRate = 750f;
    [SerializeField] private float distantHeightAboveCamera = 14f;
    [Tooltip("İçeride emitter çatı hit noktasının bu kadar üstüne konur.")]
    [SerializeField] private float distantRoofClearance = 3f;
    [Tooltip("Distant emitter hedefe yumuşak geçiş süresi (sn).")]
    [SerializeField] private float distantPositionSmoothTime = 0.35f;

    [Header("Rain Splash")]
    [SerializeField] private bool enableGroundSplashes = true;
    [SerializeField] private LayerMask rainCollisionLayers = ~0;
    [SerializeField] private Vector2 splashLifetimeRange = new Vector2(0.14f, 0.28f);
    [SerializeField] private Vector2 splashSizeRange = new Vector2(0.09f, 0.2f);

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
    [Tooltip("Ham indoor raycast sonucu bu kadar süre sabit kalınca state değişir.")]
    [SerializeField] private float indoorStateDebounce = 0.25f;
    [SerializeField] private LayerMask roofLayers = ~0;

    private Transform _cam;
    private ParticleSystem _particles;
    private ParticleSystem _distantParticles;
    private Transform _distantTransform;
    private AudioSource _rainAudio;
    private Material _rainStreakMaterial;
    private Material _rainSplashMaterial;
    private bool _ownsRainMaterials;
    private float _emissionFactor = 1f; // 1 = açık alan, 0 = kapalı alan

    private bool _isIndoors;
    private bool _rawIndoors;
    private float _indoorDebounceTimer;
    private bool _hasRoofHit;
    private bool _hasCachedRoof;
    private Vector3 _roofHitPoint;
    private Vector3 _distantVelocity;
    private bool _distantInitialized;

    // Koyu soğuk gri — far/ışıkta parlamasın, fırtına hissi versin.
    private static readonly Color NearColorA = new Color(0.22f, 0.25f, 0.29f, 0.42f);
    private static readonly Color NearColorB = new Color(0.32f, 0.36f, 0.40f, 0.68f);
    private static readonly Color DistantColorA = new Color(0.18f, 0.21f, 0.25f, 0.28f);
    private static readonly Color DistantColorB = new Color(0.28f, 0.32f, 0.36f, 0.48f);
    private static readonly Color SplashColorA = new Color(0.28f, 0.31f, 0.35f, 0.40f);
    private static readonly Color SplashColorB = new Color(0.40f, 0.44f, 0.48f, 0.70f);

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        CreateRainMaterials();
        ConfigureRain();
        ConfigureDistantRain();
        ConfigureRainAudio();
    }

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

    private void ConfigureRain()
    {
        var particles = GetComponent<ParticleSystem>();
        if (particles == null) return;

        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startColor = new ParticleSystem.MinMaxGradient(NearColorA, NearColorB);

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
        renderer.velocityScale = 0.1f;
        renderer.lengthScale = 2.2f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enableGPUInstancing = true;
        if (_rainStreakMaterial != null)
            renderer.sharedMaterial = _rainStreakMaterial;

        if (enableGroundSplashes)
            ConfigureGroundSplashes(particles, _rainSplashMaterial != null ? _rainSplashMaterial : renderer.sharedMaterial);
    }

    private void ConfigureDistantRain()
    {
        Material rainMaterial = _rainStreakMaterial;
        if (rainMaterial == null && _particles != null)
        {
            var nearRenderer = _particles.GetComponent<ParticleSystemRenderer>();
            if (nearRenderer != null)
                rainMaterial = nearRenderer.sharedMaterial;
        }

        // Sibling/root olmalı: Rain parent hareketi SmoothDamp'i her frame sıfırlamasın.
        // Rain objesi X'te 90°; distant da aynı world rotasyonu alsın (box → aşağı).
        Transform existing = null;
        if (transform.parent != null)
            existing = transform.parent.Find("RainDistant");
        if (existing == null)
        {
            var sceneRoot = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < sceneRoot.Length; i++)
            {
                if (sceneRoot[i].name == "RainDistant")
                {
                    existing = sceneRoot[i].transform;
                    break;
                }
            }
        }

        if (existing != null && existing.TryGetComponent(out _distantParticles))
        {
            _distantTransform = existing;
        }
        else
        {
            var go = new GameObject("RainDistant", typeof(ParticleSystem));
            _distantTransform = go.transform;
            if (transform.parent != null)
                _distantTransform.SetParent(transform.parent, false);
            _distantParticles = go.GetComponent<ParticleSystem>();
        }

        _distantTransform.rotation = transform.rotation;
        _distantParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _distantParticles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 2800;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x * 1.15f, sizeRange.y * 1.25f);
        main.startColor = new ParticleSystem.MinMaxGradient(DistantColorA, DistantColorB);

        var emission = _distantParticles.emission;
        emission.rateOverTime = distantEmissionRate;

        var shape = _distantParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(distantRadius * 2f, distantRadius * 2f, 1f);

        var velocity = _distantParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = windX;
        velocity.z = windZ;

        var collision = _distantParticles.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.collidesWith = rainCollisionLayers;
        collision.dampen = 0f;
        collision.bounce = 0f;
        collision.lifetimeLoss = 1f;
        collision.radiusScale = 0.3f;
        collision.maxCollisionShapes = 64;
        collision.sendCollisionMessages = false;

        var renderer = _distantParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.12f;
        renderer.lengthScale = 2.6f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enableGPUInstancing = true;
        if (rainMaterial != null)
            renderer.sharedMaterial = rainMaterial;

        // Distant'ta splash yok — maliyet ve içeride gereksiz.
        var subEmitters = _distantParticles.subEmitters;
        subEmitters.enabled = false;

        _distantParticles.Play();
    }

    private void OnDestroy()
    {
        if (_distantTransform != null)
            Destroy(_distantTransform.gameObject);
        if (_ownsRainMaterials)
        {
            if (_rainStreakMaterial != null)
                Destroy(_rainStreakMaterial);
            if (_rainSplashMaterial != null && _rainSplashMaterial != _rainStreakMaterial)
                Destroy(_rainSplashMaterial);
        }
    }

    private void ConfigureGroundSplashes(ParticleSystem rain, Material splashMaterial)
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
        splashMain.maxParticles = 900;
        splashMain.startLifetime = new ParticleSystem.MinMaxCurve(
            splashLifetimeRange.x, splashLifetimeRange.y);
        splashMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        splashMain.startSize = new ParticleSystem.MinMaxCurve(
            splashSizeRange.x, splashSizeRange.y);
        splashMain.startColor = new ParticleSystem.MinMaxGradient(SplashColorA, SplashColorB);
        splashMain.gravityModifier = 1.2f;

        var splashEmission = splash.emission;
        splashEmission.enabled = true;
        splashEmission.rateOverTime = 0f;
        splashEmission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 3, 6)
        });

        var splashShape = splash.shape;
        splashShape.enabled = true;
        splashShape.shapeType = ParticleSystemShapeType.Hemisphere;
        splashShape.radius = 0.06f;

        var sizeOverLifetime = splash.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.15f));

        var colorOverLifetime = splash.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var splashFade = new Gradient();
        splashFade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0.4f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = splashFade;

        var splashRenderer = splash.GetComponent<ParticleSystemRenderer>();
        splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        if (splashMaterial != null)
            splashRenderer.sharedMaterial = splashMaterial;
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

        Vector3 nearPos = GetNearFollowPosition();
        transform.position = nearPos;

        UpdateIndoorState();
        UpdateNearIndoorFade();
        UpdateDistantPosition(nearPos);
    }

    private Vector3 GetNearFollowPosition()
    {
        Vector3 flatForward = _cam.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude > 0.0001f)
            flatForward.Normalize();
        else
            flatForward = Vector3.forward;

        return _cam.position + flatForward * forwardOffset + Vector3.up * heightAboveCamera;
    }

    private void UpdateIndoorState()
    {
        _hasRoofHit = Physics.Raycast(
            _cam.position,
            Vector3.up,
            out RaycastHit hit,
            roofCheckDistance,
            roofLayers,
            QueryTriggerInteraction.Ignore);

        if (_hasRoofHit)
        {
            _roofHitPoint = hit.point;
            _hasCachedRoof = true;
        }

        bool rawIndoors = _hasRoofHit;
        if (rawIndoors == _rawIndoors)
        {
            _indoorDebounceTimer += Time.deltaTime;
        }
        else
        {
            _rawIndoors = rawIndoors;
            _indoorDebounceTimer = 0f;
        }

        if (_indoorDebounceTimer >= indoorStateDebounce && _isIndoors != _rawIndoors)
            _isIndoors = _rawIndoors;
    }

    /// <summary>
    /// Stabilize indoor state'e göre yakın yağmur emisyonunu söndürür/açar.
    /// </summary>
    private void UpdateNearIndoorFade()
    {
        if (_particles == null) return;

        float target = _isIndoors ? 0f : 1f;
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

    private void UpdateDistantPosition(Vector3 nearPos)
    {
        if (_distantTransform == null) return;

        Vector3 target;
        if (_isIndoors && _hasCachedRoof)
        {
            // XZ kamerayı takip et; Y çatı üstü — odada damla doğmaz, pencereden görünür.
            target = new Vector3(
                _cam.position.x,
                _roofHitPoint.y + distantRoofClearance,
                _cam.position.z);
        }
        else
        {
            // Outdoor: near ile aynı XZ/forward, biraz daha yüksek.
            target = nearPos + Vector3.up * (distantHeightAboveCamera - heightAboveCamera);
        }

        if (!_distantInitialized)
        {
            _distantTransform.position = target;
            _distantVelocity = Vector3.zero;
            _distantInitialized = true;
            return;
        }

        _distantTransform.position = Vector3.SmoothDamp(
            _distantTransform.position,
            target,
            ref _distantVelocity,
            distantPositionSmoothTime);
    }
}
