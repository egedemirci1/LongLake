using UnityEngine;

/// <summary>
/// Yağmur: yakın katman kamerayı takip eder ve içeride söner;
/// distant katman içeride çatı üstüne kayar (pencereden dışarıda görünür).
/// Oyuncu Netcode ile runtime'da spawn olduğu için MainCamera takip edilir.
/// </summary>
public partial class RainFollow : MonoBehaviour
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
