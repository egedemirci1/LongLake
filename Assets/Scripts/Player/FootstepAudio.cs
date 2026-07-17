using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ayak sesi: hareket halindeyken yürüme / koşma klibini çalar.
/// </summary>
public class FootstepAudio : NetworkBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip walkClip;
    [SerializeField] private AudioClip runClip;
    [SerializeField] private string walkResourcesPath = "SFX/walk";
    [SerializeField] private string runResourcesPath = "SFX/running";

    [Header("Gait")]
    [SerializeField] private float minSpeedToStep = 1.2f;
    [SerializeField] private float sprintSpeedThreshold = 6.2f;

    [Header("Feel")]
    [SerializeField] private float leftPitch = 1.03f;
    [SerializeField] private float walkVolume = 0.48f;
    [SerializeField] private float runVolume = 0.58f;
    [SerializeField] private float runPitchBoost = 1.08f;

    [Header("3D Audio")]
    [SerializeField] private float maxDistance = 18f;
    [Tooltip("Yerel oyuncu için 2D'ye yaklaştırır (kendi adımlarını net duysun).")]
    [SerializeField] private float ownerSpatialBlend = 0.15f;

    private PlayerController _player;
    private AudioSource _source;

    public override void OnNetworkSpawn()
    {
        _player = GetComponent<PlayerController>();

        if (walkClip == null)
            walkClip = Resources.Load<AudioClip>(walkResourcesPath);
        if (runClip == null)
            runClip = Resources.Load<AudioClip>(runResourcesPath);
        if (runClip == null)
            runClip = walkClip;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = IsOwner ? ownerSpatialBlend : 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 1.5f;
        _source.maxDistance = maxDistance;
        _source.dopplerLevel = 0f;
        _source.priority = 128;
    }

    public override void OnNetworkDespawn()
    {
        StopSteps();
    }

    private void Update()
    {
        if (!IsSpawned || _player == null || walkClip == null || _source == null) return;
        if (!_player.HasSelectedCharacter)
        {
            StopSteps();
            return;
        }

        float speed = _player.CurrentMoveSpeed;
        bool moving = speed >= minSpeedToStep;
        if (IsOwner)
            moving = _player.IsGrounded && moving;

        if (!moving)
        {
            StopSteps();
            return;
        }

        bool sprinting = false;
        if (IsOwner)
        {
            sprinting = Input.GetKey(KeyCode.LeftShift);
        }
        else
        {
            sprinting = speed >= sprintSpeedThreshold;
        }

        AudioClip targetClip = sprinting ? runClip : walkClip;
        float targetVolume = sprinting ? runVolume : walkVolume;
        float targetPitch = sprinting ? (leftPitch * runPitchBoost) : leftPitch;

        if (_source.clip != targetClip || !_source.isPlaying)
        {
            _source.clip = targetClip;
            _source.loop = false;
            _source.volume = targetVolume;
            _source.pitch = targetPitch;
            _source.Play();
        }
        else
        {
            _source.volume = targetVolume;
            _source.pitch = targetPitch;
            if (!_source.isPlaying)
            {
                _source.Play();
            }
        }
    }

    private void StopSteps()
    {
        if (_source != null && _source.isPlaying)
            _source.Stop();
    }
}
