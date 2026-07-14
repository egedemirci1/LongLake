using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Loops the assigned VideoClip into the scene RawImage (VideoLayer).
/// Hierarchy objects are authored in the scene — nothing is spawned at runtime.
/// </summary>
public class MainMenuVideoBackground : MonoBehaviour
{
    [SerializeField] private VideoClip videoClip;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private RawImage rawImage;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool mute = true;

    private Image placeholderImage;

    private void Awake()
    {
        if (videoClip == null || videoPlayer == null)
        {
            Debug.LogError("[MainMenuVideoBackground] Missing videoClip / videoPlayer reference.");
            return;
        }

        placeholderImage = GetComponent<Image>();
        Camera targetCam = Camera.main;
        if (targetCam == null)
        {
            targetCam = FindFirstObjectByType<Camera>();
        }
        if (targetCam == null)
        {
            Debug.LogError("[MainMenuVideoBackground] No Camera found for CameraFarPlane mode.");
            return;
        }

        // Disable UI RawImage path entirely to avoid RenderTexture decode issues.
        if (rawImage != null)
        {
            rawImage.texture = null;
            rawImage.enabled = false;
        }

        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = loop;
        videoPlayer.clip = videoClip;
        videoPlayer.renderMode = VideoRenderMode.CameraFarPlane;
        videoPlayer.targetCamera = targetCam;
        videoPlayer.targetCameraAlpha = 1f;
        videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        videoPlayer.audioOutputMode = mute
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;
        videoPlayer.skipOnDrop = true;

        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.started += OnStarted;
        videoPlayer.errorReceived += OnError;
        videoPlayer.Prepare();
    }

    private void OnPrepared(VideoPlayer source)
    {
        source.Play();
    }

    private void OnStarted(VideoPlayer source)
    {
        if (placeholderImage != null)
            placeholderImage.enabled = false;
    }

    private void OnError(VideoPlayer source, string message)
    {
        Debug.LogError($"[MainMenuVideoBackground] VideoPlayer error: {message}");
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.started -= OnStarted;
            videoPlayer.errorReceived -= OnError;
            videoPlayer.Stop();
        }
    }
}
