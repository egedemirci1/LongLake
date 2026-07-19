using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// Partial: split for maintainability. Type identity unchanged.
public partial class LoadingScreenManager : MonoBehaviour
{
    private void EnsureVideoLayer(RectTransform parent)
    {
        var videoGo = new GameObject("VideoLayer", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        videoGo.transform.SetParent(parent, false);
        videoGo.transform.SetSiblingIndex(0);
        StretchFull(videoGo.GetComponent<RectTransform>());
        _videoRaw = videoGo.GetComponent<RawImage>();
        _videoRaw.color = Color.white;
        _videoRaw.raycastTarget = false;

        var posterGo = new GameObject("VideoPoster", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        posterGo.transform.SetParent(parent, false);
        posterGo.transform.SetSiblingIndex(1);
        StretchFull(posterGo.GetComponent<RectTransform>());
        _posterImage = posterGo.GetComponent<Image>();
        _posterImage.preserveAspect = false;
        _posterImage.raycastTarget = false;
        if (loadingPosterSprite != null)
            _posterImage.sprite = loadingPosterSprite;

        if (loadingVideoClip == null)
            return;

        _videoRt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
        {
            name = "LoadingScreenVideoRT",
            hideFlags = HideFlags.HideAndDontSave
        };
        _videoRt.Create();
        _videoRaw.texture = _videoRt;

        _videoPlayer = gameObject.GetComponent<VideoPlayer>();
        if (_videoPlayer == null)
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();

        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.isLooping = true;
        _videoPlayer.clip = loadingVideoClip;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture = _videoRt;
        _videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.playbackSpeed = 1f;
        _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        _videoPlayer.audioOutputMode = muteVideo
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;

        if (!_videoHooks)
        {
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.started += OnVideoStarted;
            _videoPlayer.loopPointReached += OnVideoLoop;
            _videoPlayer.errorReceived += OnVideoError;
            _videoHooks = true;
        }
    }

    private void BeginVideoWarmup()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;
        if (_videoPlayer.isPrepared || _videoPlayer.isPlaying) return;
        _videoPlayer.Prepare();
    }

    private void KickVideoPlay()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;

        if (_videoPlayer.isPrepared)
            _videoPlayer.Play();
        else
            _videoPlayer.Prepare();
    }

    private void KeepVideoAlive()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;
        if (_videoRecovering) return;

        if (!_videoPlayer.isPrepared)
        {
            if (Time.unscaledTime >= _nextVideoKickUnscaled)
            {
                _nextVideoKickUnscaled = Time.unscaledTime + 0.4f;
                try { _videoPlayer.Prepare(); }
                catch { /* ignore */ }
            }
            return;
        }

        long frame = -1;
        try { frame = _videoPlayer.frame; }
        catch
        {
            RequestVideoRecover();
            return;
        }

        bool advancing = frame != _lastVideoFrame && frame >= 0;
        if (advancing)
        {
            _lastVideoFrame = frame;
            _videoStallTimer = 0f;
            if (_posterImage != null)
                _posterImage.enabled = false;
            return;
        }

        if (!_videoPlayer.isPlaying)
        {
            try { _videoPlayer.Play(); }
            catch
            {
                RequestVideoRecover();
                return;
            }
        }

        // Play diyor ama kare ilerlemiyor → LoadScene spike sonrası tipik durum
        _videoStallTimer += Time.unscaledDeltaTime;
        if (_videoStallTimer >= 0.25f)
        {
            _videoStallTimer = 0f;
            RequestVideoRecover();
        }
    }

    private void RequestVideoRecover()
    {
        if (_videoRecovering || !_isShowing) return;
        if (Time.unscaledTime < _nextRecoverAllowedUnscaled) return;
        _nextRecoverAllowedUnscaled = Time.unscaledTime + 0.6f;
        if (_videoRecoverRoutine != null)
            StopCoroutine(_videoRecoverRoutine);
        _videoRecoverRoutine = StartCoroutine(RecoverVideoRoutine());
    }

    private IEnumerator RecoverVideoRoutine()
    {
        _videoRecovering = true;

        if (_videoPlayer != null)
        {
            try
            {
                _videoPlayer.Stop();
            }
            catch { /* ignore */ }
        }

        // RT kaybolmuş olabilir
        if (_videoRt == null || !_videoRt.IsCreated())
        {
            if (_videoRt != null)
            {
                _videoRt.Release();
                Destroy(_videoRt);
            }

            _videoRt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "LoadingScreenVideoRT",
                hideFlags = HideFlags.HideAndDontSave
            };
            _videoRt.Create();
            if (_videoRaw != null)
                _videoRaw.texture = _videoRt;
        }

        if (_videoPlayer == null)
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();

        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.isLooping = true;
        _videoPlayer.clip = loadingVideoClip;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture = _videoRt;
        _videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.playbackSpeed = 1f;
        _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        _videoPlayer.audioOutputMode = muteVideo
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;

        bool prepared = false;
        void OnPrep(VideoPlayer vp)
        {
            prepared = true;
            vp.Play();
            if (_posterImage != null)
                _posterImage.enabled = false;
        }

        _videoPlayer.prepareCompleted += OnPrep;
        _videoPlayer.Prepare();

        float t = 0f;
        while (!prepared && t < 2f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _videoPlayer.prepareCompleted -= OnPrep;

        if (!prepared && _videoPlayer.isPrepared)
        {
            _videoPlayer.Play();
            if (_posterImage != null)
                _posterImage.enabled = false;
        }

        _lastVideoFrame = -1;
        _videoStallTimer = 0f;
        _videoRecovering = false;
        _videoRecoverRoutine = null;
    }

    private void StopLoadingVideo()
    {
        if (_videoPlayer == null) return;
        if (_videoPlayer.isPlaying)
            _videoPlayer.Pause();
        _videoPlayer.time = 0;
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        if (_isShowing)
            source.Play();
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        if (_posterImage != null)
            _posterImage.enabled = false;
    }

    private void OnVideoLoop(VideoPlayer source)
    {
        // Loop noktası bazı platformlarda pause bırakabiliyor.
        if (_isShowing && !source.isPlaying)
            source.Play();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[LoadingScreenManager] Video error: {message}");
        if (_posterImage != null)
            _posterImage.enabled = true;
    }

    private void TeardownVideo()
    {
        if (_videoPlayer != null && _videoHooks)
        {
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.started -= OnVideoStarted;
            _videoPlayer.loopPointReached -= OnVideoLoop;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoHooks = false;
            _videoPlayer.Stop();
        }

        if (_videoRt != null)
        {
            _videoRt.Release();
            Destroy(_videoRt);
            _videoRt = null;
        }
    }

    private static void PauseMainMenuVideo()
    {
        var menuVideo = FindFirstObjectByType<MainMenuVideoBackground>();
        if (menuVideo != null)
            menuVideo.gameObject.SetActive(false);
    }

}
