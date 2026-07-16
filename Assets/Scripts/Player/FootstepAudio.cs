using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ayak sesi: kısa one-shot adımlar. Yürüme / koşma farklı tempo (ve isteğe bağlı clip).
/// Not: clip süresinden kısa gap + Stop/Play önceki sesi keser → bozuk duyulur; PlayOneShot kullan.
/// </summary>
public class FootstepAudio : NetworkBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip walkClip;
    [SerializeField] private AudioClip walkClipRight;
    [SerializeField] private AudioClip runClip;
    [SerializeField] private AudioClip runClipRight;
    [SerializeField] private string walkResourcesPath = "SFX/walk";
    [SerializeField] private string runResourcesPath = "SFX/run";

    [Header("Gait timing")]
    [Tooltip("Yürümede sol→sağ aralığı.")]
    [SerializeField] private float walkPairGap = 0.36f;
    [Tooltip("Yürümede sağ→sonraki sol arası ekstra bekleyiş.")]
    [SerializeField] private float walkStridePause = 0.18f;
    [SerializeField] private float runPairGap = 0.26f;
    [SerializeField] private float runStridePause = 0.10f;
    [SerializeField] private float minSpeedToStep = 1.2f;
    [SerializeField] private float sprintSpeedThreshold = 6.2f;
    [SerializeField] private float gapJitter = 0.03f;

    [Header("Feel")]
    [SerializeField] private float leftPitch = 1.03f;
    [SerializeField] private float rightPitch = 0.96f;
    [SerializeField] private float walkVolume = 0.48f;
    [SerializeField] private float runVolume = 0.58f;
    [SerializeField] private float runPitchBoost = 1.08f;

    [Header("3D Audio")]
    [SerializeField] private float maxDistance = 18f;
    [Tooltip("Yerel oyuncu için 2D'ye yaklaştırır (kendi adımlarını net duysun).")]
    [SerializeField] private float ownerSpatialBlend = 0.15f;

    private PlayerController _player;
    private AudioSource _source;
    private float _nextStepTime;
    private bool _nextIsLeft = true;

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
        ResetGait();
    }

    private void Update()
    {
        if (!IsSpawned || _player == null || walkClip == null || _source == null) return;
        if (!_player.HasSelectedCharacter)
        {
            ResetGait();
            return;
        }

        float speed = _player.CurrentMoveSpeed;
        bool moving = speed >= minSpeedToStep;
        if (IsOwner)
            moving = _player.IsGrounded && moving;

        if (!moving)
        {
            ResetGait();
            return;
        }

        if (Time.time < _nextStepTime)
            return;

        bool sprinting = speed >= sprintSpeedThreshold;
        bool isLeft = _nextIsLeft;

        PlayFoot(isLeft, sprinting);

        float gap = isLeft
            ? (sprinting ? runPairGap : walkPairGap)
            : (sprinting ? runPairGap : walkPairGap) + (sprinting ? runStridePause : walkStridePause);

        gap += Random.Range(-gapJitter, gapJitter);
        gap = Mathf.Max(0.14f, gap);

        _nextStepTime = Time.time + gap;
        _nextIsLeft = !isLeft;
    }

    private void PlayFoot(bool left, bool sprinting)
    {
        AudioClip clip;
        if (sprinting)
        {
            clip = (!left && runClipRight != null) ? runClipRight : runClip;
            if (clip == null) clip = walkClip;
        }
        else
        {
            clip = (!left && walkClipRight != null) ? walkClipRight : walkClip;
        }

        if (clip == null) return;

        float pitch = left ? leftPitch : rightPitch;
        if (sprinting) pitch *= runPitchBoost;
        pitch *= Random.Range(0.98f, 1.03f);

        float volume = sprinting ? runVolume : walkVolume;
        volume *= Random.Range(0.92f, 1.05f);

        // Pitch PlayOneShot'tan önce set edilmeli (Stop yok — kesilme/tıkırtı olmaz).
        _source.pitch = pitch;
        _source.PlayOneShot(clip, volume);
    }

    private void ResetGait()
    {
        _nextStepTime = 0f;
        _nextIsLeft = true;
    }
}
