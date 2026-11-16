using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop.UI
{
    public sealed class VictoryUI : MonoBehaviour
    {
        [SerializeField] TMP_Text result;
        [SerializeField] MainMenuUI mainMenu;

        // Public entry point for a future match/UI adapter. Empty winner means a draw.
        public void ShowResult(string winnerName)
        {
            result.text = string.IsNullOrWhiteSpace(winnerName) ? "Ничья" : $"Победа: {winnerName}!";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide() => gameObject.SetActive(false);

        public void ReturnToMainMenu()
        {
            if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
            Hide();
            mainMenu.Show();
        }
    }
}
