using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gameplay yükleme: UI → LoadScene → AsyncOperation progress → LoadEventCompleted → %100 → hide.
/// </summary>
public class GameplaySceneLoader : MonoBehaviour
{
    public static GameplaySceneLoader Instance { get; private set; }

    [SerializeField] private string defaultGameplayScene = "CrashSite_Main";
    [SerializeField] private float minLoadingScreenSeconds = 1.2f;

    private NetworkManager _nm;
    private NetworkManager _hookedNm;
    private bool _hooks;
    private bool _loadInProgress;
    private bool _hostLoadStarted;
    private bool _finishStarted;
    private Coroutine _progressRoutine;
    private Coroutine _hostRoutine;
    private Coroutine _finishRoutine;
    private float _displayProgress;
    private float _targetProgress;
    private float _loadStartedUnscaled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject(nameof(GameplaySceneLoader));
        DontDestroyOnLoad(go);
        go.AddComponent<GameplaySceneLoader>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        Unhook();
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!_loadInProgress) return;

        float speed = _finishStarted ? 1.6f : 0.9f;
        _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, Time.unscaledDeltaTime * speed);
        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
        {
            ui.SetProgress(_displayProgress);
            // LoadScene ana thread'i kilitleyince VideoPlayer durur — her frame toparla.
            ui.PulseVideo();
        }
    }

    public void BeginHostLoad(string sceneName = null)
    {
        if (string.IsNullOrEmpty(sceneName))
            sceneName = defaultGameplayScene;

        // Önceki yarım yükleme asılı kaldıysa sıfırla
        if (_hostLoadStarted && !_loadInProgress)
            _hostLoadStarted = false;

        if (_hostLoadStarted) return;
        _hostLoadStarted = true;
        _finishStarted = false;

        EnsureHooks();
        if (_hostRoutine != null)
            StopCoroutine(_hostRoutine);
        _hostRoutine = StartCoroutine(HostLoadRoutine(sceneName));
    }

    /// <summary>MainMenu'ye dönüşte asılı loading / host flag'lerini temizler.</summary>
    public void ResetForMenu()
    {
        if (_hostRoutine != null)
        {
            StopCoroutine(_hostRoutine);
            _hostRoutine = null;
        }

        if (_finishRoutine != null)
        {
            StopCoroutine(_finishRoutine);
            _finishRoutine = null;
        }

        if (_progressRoutine != null)
        {
            StopCoroutine(_progressRoutine);
            _progressRoutine = null;
        }

        _hostLoadStarted = false;
        _loadInProgress = false;
        _finishStarted = false;
        _displayProgress = 0f;
        _targetProgress = 0f;

        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
            ui.HideLoadingScreen();
    }

    public void BeginClientLoadingUi(string sceneName = null)
    {
        if (string.IsNullOrEmpty(sceneName))
            sceneName = defaultGameplayScene;

        EnsureHooks();
        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
        {
            ui.ShowLoadingScreenForScene(sceneName, "Bölüm 1: Uzungöl Tatili");
            ui.SetProgress(0.02f);
        }

        _loadInProgress = true;
        _finishStarted = false;
        _displayProgress = 0.02f;
        _targetProgress = 0.05f;
        _loadStartedUnscaled = Time.unscaledTime;
    }

    private IEnumerator HostLoadRoutine(string sceneName)
    {
        _loadInProgress = true;
        _finishStarted = false;
        _displayProgress = 0f;
        _targetProgress = 0.05f;
        _loadStartedUnscaled = Time.unscaledTime;

        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
        {
            ui.ShowLoadingScreenForScene(sceneName, "Bölüm 1: Uzungöl Tatili");
            ui.SetProgress(0.02f);
        }

        Canvas.ForceUpdateCanvases();
        yield return null;
        yield return new WaitForEndOfFrame();

        float wait = 0f;
        while (ui != null && !ui.IsVisualReady && wait < 2.5f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        // Video gerçekten oynarken biraz bekle, sonra LoadScene.
        yield return new WaitForSecondsRealtime(1.25f);
        _targetProgress = 0.08f;

        _nm = NetworkManager.Singleton;
        if (_nm == null || !_nm.IsServer || _nm.SceneManager == null)
        {
            Debug.LogError("[GameplaySceneLoader] NetworkSceneManager yok — yükleme iptal.");
            FinishLoadingUi();
            _hostLoadStarted = false;
            yield break;
        }

        // Load hemen video'yu keser; UI PulseVideo ile recover edecek.
        _targetProgress = 0.1f;

        // Host yeniden başladıysa SceneManager hook'larını yenile
        EnsureHooks();

        SceneEventProgressStatus status;
        try
        {
            status = _nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameplaySceneLoader] LoadScene exception: {e}");
            FinishLoadingUi();
            _hostLoadStarted = false;
            yield break;
        }

        // LoadScene sonrası ilk frame'lerde video'yu zorla toparla.
        for (int i = 0; i < 10; i++)
        {
            ui = LoadingScreenManager.Resolve();
            ui?.PulseVideo();
            yield return null;
        }

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[GameplaySceneLoader] LoadScene failed: {status}");
            FinishLoadingUi();
            _hostLoadStarted = false;
        }

        _hostRoutine = null;
    }

    private void EnsureHooks()
    {
        _nm = NetworkManager.Singleton;
        if (_nm == null || _nm.SceneManager == null) return;

        // Shutdown/StartHost sonrası SceneManager yenilenir — eski hook'lar olay kaçırır (%10'da kalır).
        if (_hooks && _hookedNm == _nm)
            return;

        Unhook();
        _nm.SceneManager.OnSceneEvent += OnSceneEvent;
        _nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        _hookedNm = _nm;
        _hooks = true;
    }

    public void TryHookWhenNetworkReady()
    {
        _hooks = false;
        _hookedNm = null;
        StartCoroutine(HookRetry());
    }

    private IEnumerator HookRetry()
    {
        for (int i = 0; i < 10; i++)
        {
            EnsureHooks();
            if (_hooks) yield break;
            yield return new WaitForSecondsRealtime(0.2f);
        }
    }

    private void Unhook()
    {
        if (_hookedNm != null && _hookedNm.SceneManager != null)
        {
            _hookedNm.SceneManager.OnSceneEvent -= OnSceneEvent;
            _hookedNm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
        else if (_hooks && _nm != null && _nm.SceneManager != null)
        {
            _nm.SceneManager.OnSceneEvent -= OnSceneEvent;
            _nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        _hooks = false;
        _hookedNm = null;
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                if (!_loadInProgress)
                    BeginClientLoadingUi(sceneEvent.SceneName);

                if (sceneEvent.AsyncOperation != null)
                {
                    if (_progressRoutine != null)
                        StopCoroutine(_progressRoutine);
                    _progressRoutine = StartCoroutine(TrackAsyncProgress(sceneEvent.AsyncOperation));
                }
                break;

            case SceneEventType.LoadComplete:
                _targetProgress = Mathf.Max(_targetProgress, 0.85f);
                break;
        }
    }

    private IEnumerator TrackAsyncProgress(AsyncOperation op)
    {
        while (op != null && !op.isDone)
        {
            float raw = 0f;
            try { raw = op.progress; }
            catch { break; }

            // 0..0.9 → bar'da 10%..80% (son %20 LoadEventCompleted için ayrılır)
            float p = Mathf.Clamp01(raw / 0.9f);
            _targetProgress = Mathf.Max(_targetProgress, Mathf.Lerp(0.1f, 0.8f, p));
            yield return null;
        }

        _targetProgress = Mathf.Max(_targetProgress, 0.85f);
        _progressRoutine = null;
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (!_loadInProgress || _finishStarted) return;

        // Sadece gameplay sahnesi
        if (!string.IsNullOrEmpty(sceneName) &&
            sceneName != defaultGameplayScene &&
            sceneName.IndexOf("CrashSite", System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        _finishStarted = true;
        if (_finishRoutine != null)
            StopCoroutine(_finishRoutine);
        _finishRoutine = StartCoroutine(CompleteAfterMinTime());
    }

    private IEnumerator CompleteAfterMinTime()
    {
        _targetProgress = 1f;

        float elapsed = Time.unscaledTime - _loadStartedUnscaled;
        float remain = minLoadingScreenSeconds - elapsed;
        if (remain > 0f)
            yield return new WaitForSecondsRealtime(remain);

        // Bar görsel olarak %100 olmadan kapatma (eski bug: %60'ta kayboluyordu).
        float t = 0f;
        while (_displayProgress < 0.98f && t < 3f)
        {
            _targetProgress = 1f;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _displayProgress = 1f;
        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
            ui.SetProgress(1f, snap: true);

        yield return new WaitForSecondsRealtime(0.2f);
        FinishLoadingUi();
        _hostLoadStarted = false;
        _finishRoutine = null;
    }

    private void FinishLoadingUi()
    {
        _loadInProgress = false;
        _finishStarted = false;
        if (_progressRoutine != null)
        {
            StopCoroutine(_progressRoutine);
            _progressRoutine = null;
        }

        var ui = LoadingScreenManager.Resolve();
        if (ui != null)
            ui.HideLoadingScreen();
    }
}
