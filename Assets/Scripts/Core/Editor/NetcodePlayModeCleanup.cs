#if UNITY_EDITOR
using UnityEditor;
using Unity.Netcode;

/// <summary>
/// Play Mode kapanırken NetworkManager.Shutdown çağırır.
/// Domain Reload kapalıysa UDP 7777 bir sonraki Play'de "address already in use" verir.
/// </summary>
[InitializeOnLoad]
internal static class NetcodePlayModeCleanup
{
    static NetcodePlayModeCleanup()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode)
            return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.ShutdownInProgress) return;

        if (nm.IsListening || nm.IsServer || nm.IsClient)
            nm.Shutdown();
    }
}
#endif
