using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop.UI
{
    public sealed class MainMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] SettingsUI settings;
        [SerializeField] VictoryUI victory;
        [SerializeField] GameObject scoreboard;
        [SerializeField] TMP_Text status;
        bool wasListening;

        void Start()
        {
            settings.Initialize();
            Show();
        }

        void Update()
        {
            var manager = NetworkManager.Singleton;
            bool listening = manager && manager.IsListening;
            if (listening)
            {
                panel.SetActive(false);
                settings.Hide();
            }
            else if (wasListening) Show();
            if (scoreboard) scoreboard.SetActive(listening);
            wasListening = listening;
        }

        public void CreateGame() => StartSession(true);
        public void JoinGame() => StartSession(false);

        void StartSession(bool host)
        {
            var manager = NetworkManager.Singleton;
            if (!manager) { SetStatus("Не найден NetworkManager."); return; }
            if (manager.IsListening || manager.ShutdownInProgress) return;
            var connection = manager.GetComponent<RoofConnection>();
            bool started;
            if (connection)
            {
                // Retain the existing public entry point: approval, match spawn and fish setup.
                started = connection.StartSession(host, "127.0.0.1", 7777);
            }
            else
            {
                var transport = manager.GetComponent<UnityTransport>();
                if (!transport) { SetStatus("Не найден UnityTransport."); return; }
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
                started = host ? manager.StartHost() : manager.StartClient();
            }
            if (started) panel.SetActive(false);
            else SetStatus("Не удалось запустить соединение.");
        }

        public void OpenSettings()
        {
            panel.SetActive(false);
            settings.Show();
        }

        public void Show()
        {
            settings.Hide();
            victory.Hide();
            panel.SetActive(true);
            SetStatus("");
        }

        void SetStatus(string message)
        {
            if (status) status.text = message;
        }

        public void QuitGame()
        {
            if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
