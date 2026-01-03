using UnityEngine;
using Unity.Netcode;

public class CharacterSelector : MonoBehaviour
{
    public GameObject selectionPanel;

    public void SelectAhu() => SetCharacter(0);
    public void SelectYaman() => SetCharacter(1);

    private void SetCharacter(int index)
    {
        // Yerel oyuncuyu (bizi) bul ve seçimi ilet
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.SelectCharacter(index); //
                selectionPanel.SetActive(false); // Seçimden sonra paneli kapat

                // Fareyi tekrar oyun içine kilitle (StarterAssets için)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}