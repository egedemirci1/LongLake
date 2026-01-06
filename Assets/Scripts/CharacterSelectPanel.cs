using UnityEngine;
using Unity.Netcode;

public class CharacterSelector : MonoBehaviour
{
    public GameObject selectionPanel;

    public void SelectAhu() => SetCharacter(0);
    public void SelectYaman() => SetCharacter(1);

    private void SetCharacter(int index)
    {
        // Yerel oyuncuyu (bizi) bul ve seimi ilet
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            var setup = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<NetworkLocalSetup>();
            
            if (pc != null)
            {
                pc.SelectCharacter(index);
                selectionPanel.SetActive(false); // Seimden sonra paneli kapat

                // Kontrolleri aktif et (NetworkLocalSetup'tan)
                if (setup != null)
                {
                    setup.EnableControlsAfterCharacterSelection();
                }
                else
                {
                    // Fallback: Eğer NetworkLocalSetup bulunamazsa manuel yap
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }
    }
}
