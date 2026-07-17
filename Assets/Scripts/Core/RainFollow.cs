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

    private Transform _cam;

    private void Awake()
    {
        ConfigureRain();
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
    }
}
