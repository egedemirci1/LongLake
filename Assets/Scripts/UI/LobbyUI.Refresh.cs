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
    private void Refresh()
    {
        if (lobbyPanel == null || !lobbyPanel.activeSelf) return;
        EnsureProfessionalLayout();

        var nm = NetworkManager.Singleton;
        var pc = GetLocalPc();
        int localChar = pc != null ? pc.characterIndex.Value : -1;

        bool ahuTaken = PlayerController.IsCharacterIndexTaken(0, nm != null ? nm.LocalClientId : ulong.MaxValue);
        bool yamanTaken = PlayerController.IsCharacterIndexTaken(1, nm != null ? nm.LocalClientId : ulong.MaxValue);

        bool ahuSelected = localChar == 0;
        bool yamanSelected = localChar == 1;

        if (ahuButton != null)
            ahuButton.interactable = !_waitingSelect && (ahuSelected || !ahuTaken);
        if (yamanButton != null)
            yamanButton.interactable = !_waitingSelect && (yamanSelected || !yamanTaken);

        ApplyCharCardVisual(0, ahuSelected, ahuTaken && !ahuSelected);
        ApplyCharCardVisual(1, yamanSelected, yamanTaken && !yamanSelected);

        bool isReady = _lobby != null && _lobby.IsLocalReady();
        bool canReady = pc != null && pc.HasSelectedCharacter;
        if (readyButton != null)
            readyButton.interactable = canReady;
        if (readyButtonLabel != null)
            readyButtonLabel.text = isReady ? "Hazır!" : "Hazırım";
        if (_readyBg != null)
            _readyBg.color = isReady ? ReadyAccent : ButtonIdle;
        if (readyButtonLabel != null)
            readyButtonLabel.color = isReady ? new Color(0.12f, 0.10f, 0.06f, 1f) : BodyText;

        bool isHost = nm != null && nm.IsHost;
        bool canStart = _lobby != null && _lobby.CanHostStartGame();
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = canStart;
        }
        if (_startBg != null)
            _startBg.color = canStart ? StartAccent : ButtonIdle;

        if (statusText != null && !_waitingSelect)
        {
            int ready = _lobby != null ? _lobby.ReadyCount : 0;
            int needed = _lobby != null ? _lobby.ConnectedCount : 0;
            string role = isHost ? "Oda sahibi" : "Misafir";
            bool everyoneReady = needed > 0 && ready == needed;
            string readyLine = everyoneReady
                ? $"<color=#52B87A>Hazır {ready}/{needed}</color>"
                : $"Hazır {ready}/{needed}";
            statusText.text = $"{role}\n{readyLine}";
        }

        if (hostIpText != null)
        {
            if (isHost)
            {
                hostIpText.gameObject.SetActive(true);
                hostIpText.text = $"Oda IP  ·  {GetLocalIpSummary()}";
            }
            else
            {
                hostIpText.gameObject.SetActive(false);
            }
        }

        RefreshPlayerRoster();
    }

    private void HookPlayerControllers()
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pc == null || _hookedPlayers.Contains(pc)) continue;
            _hookedPlayers.Add(pc);
            pc.characterIndex.OnValueChanged += OnAnyCharacterChanged;
        }
    }

    private void OnAnyCharacterChanged(int previous, int current) => Refresh();

    private void RefreshPlayerRoster()
    {
        if (_playerSlots[0].Title == null) return;

        var nm = NetworkManager.Singleton;
        _orderedClients.Clear();
        if (_lobby != null)
            _lobby.CopyConnectedClientsOrdered(_orderedClients);

        int connected = _orderedClients.Count;
        if (_playersEyebrow != null)
            _playersEyebrow.text = $"OYUNCULAR  ·  {connected}/2";

        for (int slot = 0; slot < 2; slot++)
        {
            ref PlayerSlotUi ui = ref _playerSlots[slot];
            if (ui.Title == null) continue;

            if (slot >= _orderedClients.Count)
            {
                ui.Bg.color = new Color(0.08f, 0.09f, 0.10f, 0.85f);
                ui.Dot.color = new Color(0.35f, 0.36f, 0.34f, 1f);
                ui.Title.text = slot == 1 ? "2. Oyuncu" : "Oyuncu";
                ui.Title.color = MutedText;
                ui.Detail.text = "Bağlantı bekleniyor…";
                ui.Detail.color = new Color(MutedText.r, MutedText.g, MutedText.b, 0.75f);
                continue;
            }

            ulong clientId = _orderedClients[slot];
            bool isLocal = nm != null && clientId == nm.LocalClientId;
            bool isHostClient = clientId == NetworkManager.ServerClientId;
            bool ready = _lobby != null && _lobby.IsClientReady(clientId);

            string role = isHostClient ? "Oda sahibi" : "Misafir";
            string who = isLocal ? $"Sen · {role}" : role;

            int charIdx = GetCharacterForClient(clientId);
            string charName = charIdx == 0 ? "Ahu" : charIdx == 1 ? "Yaman" : "Karakter seçiyor";
            string readyLabel = ready ? "Hazır" : "Hazır değil";

            ui.Bg.color = isLocal ? new Color(0.14f, 0.13f, 0.10f, 0.98f) : new Color(0.10f, 0.11f, 0.13f, 0.95f);
            ui.Dot.color = ready ? StartAccent : AccentColor;
            ui.Title.text = who;
            ui.Title.color = BodyText;
            ui.Detail.text = $"{charName}  ·  {readyLabel}";
            ui.Detail.color = ready ? StartAccent : MutedText;
        }
    }

    private static int GetCharacterForClient(ulong clientId)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pc == null || !pc.IsSpawned) continue;
            if (pc.OwnerClientId == clientId)
                return pc.characterIndex.Value;
        }
        return -1;
    }

    private void ApplyCharCardVisual(int index, bool selected, bool takenByOther)
    {
        bool isAhu = index == 0;
        Image bg = isAhu ? _ahuCardBg : _yamanCardBg;
        Image ring = isAhu ? _ahuRing : _yamanRing;
        TextMeshProUGUI name = isAhu ? _ahuName : _yamanName;
        TextMeshProUGUI meta = isAhu ? _ahuMeta : _yamanMeta;
        TextMeshProUGUI badge = isAhu ? _ahuBadge : _yamanBadge;
        Color tone = isAhu ? AhuTone : YamanTone;

        if (bg != null)
        {
            if (selected) bg.color = CharCardSelected;
            else if (takenByOther) bg.color = CharCardTaken;
            else bg.color = CharCardIdle;
        }

        if (ring != null)
        {
            ring.enabled = selected;
            // Rounded sprite dolu bir gorsel oldugu icin sari kullanmak kartin
            // tamamini boyuyordu. Koyu katman metin kontrastini korur;
            // secim vurgusu alttaki sari badge ile verilir.
            ring.color = SelectionOverlay;
        }

        if (name != null)
            name.color = takenByOther ? MutedText : (selected ? Color.white : BodyText);

        if (meta != null)
            meta.color = takenByOther
                ? new Color(MutedText.r, MutedText.g, MutedText.b, 0.55f)
                : (selected ? SelectedMetaText : MutedText);

        if (badge != null)
        {
            if (selected)
            {
                badge.text = "SEÇİLDİ";
                badge.color = AccentColor;
                badge.fontSize = 13f;
            }
            else if (takenByOther)
            {
                badge.text = "ALINDI";
                badge.color = new Color(0.75f, 0.35f, 0.35f, 1f);
                badge.fontSize = 12f;
            }
            else
            {
                badge.text = "SEÇ";
                badge.color = tone;
                badge.fontSize = 12f;
            }
        }
    }

}
