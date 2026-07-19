using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Partial: split for maintainability. Type identity unchanged.
public partial class LobbyUI : MonoBehaviour
{
    private static string GetLocalIpSummary()
    {
        var ips = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254")) continue;
                    if (!ips.Contains(ip)) ips.Add(ip);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyUI] IP listesi alınamadı: {e.Message}");
        }

        if (ips.Count == 0) return "bulunamadı";

        ips.Sort((a, b) => IpPriority(b).CompareTo(IpPriority(a)));
        return string.Join("  ·  ", ips.GetRange(0, Mathf.Min(2, ips.Count)));
    }

    private static int IpPriority(string ip)
    {
        if (ip.StartsWith("10.")) return 2;
        if (ip.StartsWith("192.168.")) return 1;
        return 0;
    }

    private static PlayerController GetLocalPc()
    {
        var nm = NetworkManager.Singleton;
        if (nm?.LocalClient?.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<PlayerController>();
    }

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private void ValidateSceneReferences()
    {
        if (connectPanel == null || lobbyPanel == null ||
            ahuButton == null || yamanButton == null ||
            readyButton == null || startGameButton == null ||
            statusText == null || readyButtonLabel == null || hostIpText == null)
        {
            Debug.LogError("[LobbyUI] MainMenu scene UI references are incomplete.", this);
        }
    }

}
