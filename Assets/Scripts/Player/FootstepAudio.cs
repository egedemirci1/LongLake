using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Footsteps as left/right pairs: tik-tik … tik-tik (short gap in pair, longer between strides).
/// Slight pitch/volume difference between feet.
/// </summary>
public class FootstepAudio : NetworkBehaviour
{
    [Header("Clip")]
    [SerializeField] private AudioClip walkClip;
    [SerializeField] private AudioClip walkClipRight; // optional; falls back to walkClip
    [SerializeField] private string resourcesPath = "SFX/walk";

    [Header("Gait (left-right pair)")]
    [Tooltip("Gap between LEFT and RIGHT within one stride.")]
    [SerializeField] private float pairGap = 0.30f;
    [Tooltip("Extra pause after RIGHT before next LEFT (the 'virgül' between strides).")]
    [SerializeField] private float stridePause = 0.22f;
    [SerializeField] private float sprintPairGap = 0.22f;
    [SerializeField] private float sprintStridePause = 0.12f;
    [SerializeField] private float minSpeedToStep = 1.5f;
    [SerializeField] private float sprintSpeedThreshold = 6.5f;
    [SerializeField] private float gapJitter = 0.04f;

    [Header("Left vs Right feel")]
    [SerializeField] private float leftPitch = 1.02f;
    [SerializeField] private float rightPitch = 0.94f;
    [SerializeField] private float leftVolume = 0.42f;
    [SerializeField] private float rightVolume = 0.36f;

    [Header("3D Audio")]
    [SerializeField] private float maxDistance = 18f;

    private PlayerController _player;
    private AudioSource _source;
    private float _nextStepTime;
    private bool _nextIsLeft = true;

    public override void OnNetworkSpawn()
    {
        _player = GetComponent<PlayerController>();

        if (walkClip == null)
            walkClip = Resources.Load<AudioClip>(resourcesPath);

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 1.5f;
        _source.maxDistance = maxDistance;
        _source.dopplerLevel = 0f;
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

        PlayFoot(isLeft);

        // LEFT -> short gap -> RIGHT -> longer pause -> LEFT ...
        float gap = isLeft
            ? (sprinting ? sprintPairGap : pairGap)
            : (sprinting ? sprintPairGap : pairGap) + (sprinting ? sprintStridePause : stridePause);

        gap += Random.Range(-gapJitter, gapJitter);
        gap = Mathf.Max(0.12f, gap);

        _nextStepTime = Time.time + gap;
        _nextIsLeft = !isLeft;
    }

    private void PlayFoot(bool left)
    {
        AudioClip clip = walkClip;
        if (!left && walkClipRight != null)
            clip = walkClipRight;

        if (_source.isPlaying)
            _source.Stop();

        _source.clip = clip;
        _source.loop = false;
        _source.pitch = left ? leftPitch : rightPitch;
        _source.pitch *= Random.Range(0.98f, 1.02f);
        _source.volume = left ? leftVolume : rightVolume;
        _source.Play();
    }

    private void ResetGait()
    {
        _nextStepTime = 0f;
        _nextIsLeft = true;
        if (_source != null && _source.isPlaying)
            _source.Stop();
    }
}
