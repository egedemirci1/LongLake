using UnityEngine;

// Partial: split for maintainability. Type identity unchanged.
public partial class RainFollow : MonoBehaviour
{
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

}
