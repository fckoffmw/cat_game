using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop
{
    public sealed class RoofConnection : MonoBehaviour
    {
        public GameObject fishPrefab;
        public static bool FullUpdates;
        public bool largeRoof;
        string address = "127.0.0.1";
        string port = "7777";

        public bool StartSession(bool host, string ip, ushort udpPort)
        {
            var manager = NetworkManager.Singleton;
            manager.GetComponent<UnityTransport>().SetConnectionData(ip,udpPort,"0.0.0.0");
            if (!host) return manager.StartClient();
            if (!manager.StartHost()) return false;
            if (largeRoof)
            {
                for (int section=0; section<6; section++)
                    for (int i=0; i<8; i++)
                    {
                        float x = (section%2 == 0 ? -1 : 1)*(2+2*(i%4));
                        float z = (section/2-1)*5 + (i<4 ? -1.6f : 1.6f);
                        var fish = Instantiate(fishPrefab,new Vector3(x,.24f,z),Quaternion.identity);
                        fish.GetComponent<NetworkObject>().Spawn(true);
                    }
            }
            return true;
        }

        void OnGUI()
        {
            var manager = NetworkManager.Singleton;
            if (!manager) return;
            GUILayout.BeginArea(new Rect(12,12,300,235),GUI.skin.box);
            if (manager.IsListening)
            {
                GUILayout.Label(manager.IsHost ? $"Хост · игроков {manager.ConnectedClientsIds.Count}" : "Клиент");
                var player = manager.LocalClient?.PlayerObject;
                if (player)
                {
                    GUILayout.Label(player.GetComponent<CatInventory>().HasFish ? "Рыбка в лапах" : "Лапы свободны");
                    if (!manager.IsServer)
                    {
                        GUILayout.Label(PracticeCat.Prediction ? "Предсказание включено" : "Ожидание сервера");
                        GUILayout.Label(player.GetComponent<PracticeCat>().LastEvent);
                    }
                }
                if (GUILayout.Button("Отключиться")) manager.Shutdown();
            }
            else
            {
                address = GUILayout.TextField(address);
                port = GUILayout.TextField(port);
                PracticeCat.Prediction = GUILayout.Toggle(PracticeCat.Prediction,"Предсказание своего кота");
                largeRoof = GUILayout.Toggle(largeRoof,"Хост: 60 рыбок");
                FullUpdates = GUILayout.Toggle(FullUpdates,"Хост: полная рассылка рыбок (10 Гц)");
                if (ushort.TryParse(port,out var number))
                {
                    if (GUILayout.Button("Создать игру")) StartSession(true,"127.0.0.1",number);
                    if (GUILayout.Button("Подключиться")) StartSession(false,address,number);
                }
            }
            GUILayout.EndArea();
        }
    }
}
