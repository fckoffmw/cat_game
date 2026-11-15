using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop
{
    public sealed class RoofConnection : MonoBehaviour
    {
        string address = "127.0.0.1";
        void OnGUI()
        {
            var manager = NetworkManager.Singleton;
            if (!manager) return;
            GUILayout.BeginArea(new Rect(12, 12, 220, 160), GUI.skin.box);
            if (manager.IsListening)
            {
                GUILayout.Label(manager.IsHost ? $"Хост · игроков {manager.ConnectedClientsIds.Count}" : "Клиент");
                if (GUILayout.Button("Отключиться")) manager.Shutdown();
            }
            else
            {
                address = GUILayout.TextField(address);
                var transport = manager.GetComponent<UnityTransport>();
                if (GUILayout.Button("Создать игру"))
                {
                    transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
                    manager.StartHost();
                }
                if (GUILayout.Button("Подключиться"))
                {
                    transport.SetConnectionData(address, 7777);
                    manager.StartClient();
                }
            }
            GUILayout.EndArea();
        }
    }
}
