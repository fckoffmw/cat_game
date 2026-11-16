using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(NetworkManager))]
    public sealed class RoofConnection : MonoBehaviour
    {
        static readonly string[] BoxNames = { "Бирюзовая", "Розовая", "Оранжевая" };
        static readonly Color[] BoxColors =
        {
            new(.45f, 1f, 1f), new(1f, .62f, 1f), new(1f, .74f, .42f)
        };

        public GameObject fishPrefab;
        public static bool FullUpdates;
        public bool largeRoof;
        readonly HashSet<ulong> approvedPlayers = new();
        readonly List<RoofMatch.Standing> ranking = new(RoofMatch.MaxPlayers);
        NetworkManager manager;
        GameObject matchPrefab;
        int playerLimit = RoofMatch.MaxPlayers;
        string address = "127.0.0.1";
        string port = "7777";
        string status = "";

        void Awake()
        {
            manager = GetComponent<NetworkManager>();
            matchPrefab = Resources.Load<GameObject>("RoofMatch");
            manager.OnClientDisconnectCallback += OnDisconnect;
        }

        void OnDestroy()
        {
            if (manager) manager.OnClientDisconnectCallback -= OnDisconnect;
        }

        void OnDisconnect(ulong clientId) => approvedPlayers.Remove(clientId);

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = approvedPlayers.Count < playerLimit;
            response.CreatePlayerObject = response.Approved;
            if (response.Approved) approvedPlayers.Add(request.ClientNetworkId);
            else response.Reason = "Лобби заполнено.";
        }

        public bool StartSession(bool host, string ip, ushort udpPort, bool enableMatch = true)
        {
            if (manager.IsListening) return false;
            approvedPlayers.Clear();
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = host ? Approve : null;
            manager.GetComponent<UnityTransport>().SetConnectionData(ip, udpPort, "0.0.0.0");
            if (!host) return manager.StartClient();
            if (!manager.StartHost()) return false;
            if (largeRoof)
            {
                for (int section = 0; section < 6; section++)
                    for (int i = 0; i < 8; i++)
                    {
                        float x = (section % 2 == 0 ? -1 : 1) * (2 + 2 * (i % 4));
                        float z = (section / 2 - 1) * 5 + (i < 4 ? -1.6f : 1.6f);
                        var fish = Instantiate(fishPrefab, new Vector3(x, .24f, z), Quaternion.identity);
                        fish.GetComponent<NetworkObject>().Spawn(true);
                    }
            }
            if (enableMatch) Instantiate(matchPrefab).GetComponent<NetworkObject>().Spawn(true);
            return true;
        }

        void Connect(bool host, ushort udpPort)
        {
            status = StartSession(host, host ? "127.0.0.1" : address, udpPort)
                ? "" : "Не удалось запустить соединение.";
        }

        void OnGUI()
        {
            if (!manager) return;
            float height = manager.IsListening ? 162 : 250;
            GUILayout.BeginArea(new Rect(12, 12, 310, height), GUI.skin.box);
            if (manager.IsListening) DrawSession();
            else DrawConnection();
            GUILayout.EndArea();

            var match = RoofMatch.Instance;
            if (match && match.IsSpawned) DrawRanking(match);
        }

        void DrawConnection()
        {
            GUILayout.Label("Коты на крыше");
            GUILayout.Label("Адрес и UDP-порт");
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address);
            port = GUILayout.TextField(port, GUILayout.Width(65));
            GUILayout.EndHorizontal();
            PracticeCat.Prediction = GUILayout.Toggle(PracticeCat.Prediction, "Предсказание своего кота");
            largeRoof = GUILayout.Toggle(largeRoof, "Хост: 60 рыбок");
            FullUpdates = GUILayout.Toggle(FullUpdates, "Хост: полная рассылка рыбок (10 Гц)");
            playerLimit = GUILayout.Toggle(playerLimit == 3, "Хост: разрешить третьего игрока") ? 3 : 2;

            bool validPort = ushort.TryParse(port, out var number) && number > 0;
            using (new GUIEnabledScope(validPort))
            {
                if (GUILayout.Button("Создать игру")) Connect(true, number);
                if (GUILayout.Button("Подключиться")) Connect(false, number);
            }
            string reason = string.IsNullOrEmpty(status) ? manager.DisconnectReason : status;
            if (!string.IsNullOrEmpty(reason)) GUILayout.Label(reason);
        }

        void DrawSession()
        {
            var match = RoofMatch.Instance;
            var player = manager.LocalClient?.PlayerObject;
            GUILayout.Label(manager.IsHost
                ? $"Хост · игроков {manager.ConnectedClientsIds.Count}/{playerLimit}"
                : player ? "Клиент" : "Подключение…");

            if (match && match.TryGetStanding(manager.LocalClientId, out var standing))
                DrawBoxLabel(standing.Slot, $"Ваша коробка — {BoxNames[standing.Slot]}");

            if (player)
            {
                GUILayout.Label(player.GetComponent<CatInventory>().HasFish ? "Рыбка в лапах" : "Лапы свободны");
                if (!manager.IsServer)
                    GUILayout.Label(PracticeCat.Prediction ? "Предсказание включено" : "Ожидание сервера");
            }

            if (match)
            {
                switch (match.Round.Phase)
                {
                    case RoofMatch.Phase.Lobby:
                        GUILayout.Label("Лобби · нужно минимум 2 игрока");
                        DrawStart(match, "Начать раунд");
                        break;
                    case RoofMatch.Phase.Playing:
                        GUILayout.Label($"Осталось {Mathf.CeilToInt((float)match.TimeRemaining)} с");
                        break;
                    case RoofMatch.Phase.Finished:
                        int winner = match.Round.WinnerSlot;
                        if (winner < 0) GUILayout.Label("Раунд окончен · ничья");
                        else DrawBoxLabel(winner, $"Победа · {BoxNames[winner]} коробка");
                        DrawStart(match, "Реванш");
                        break;
                }
            }
            if (GUILayout.Button("Отключиться")) manager.Shutdown();
        }

        void DrawStart(RoofMatch match, string caption)
        {
            if (!manager.IsHost)
            {
                GUILayout.Label("Ожидаем хоста");
                return;
            }

            using (new GUIEnabledScope(manager.ConnectedClientsIds.Count >= RoofMatch.MinPlayers))
                if (GUILayout.Button(caption)) match.TryStartRound();
        }

        void DrawRanking(RoofMatch match)
        {
            ranking.Clear();
            for (int i = 0; i < match.PlayerCount; i++) ranking.Add(match.GetStanding(i));
            ranking.Sort((a, b) => a.Score == b.Score ? a.Slot.CompareTo(b.Slot) : b.Score.CompareTo(a.Score));

            GUILayout.BeginArea(new Rect(Screen.width - 212, 12, 200, 128), GUI.skin.box);
            GUILayout.Label("Счёт");
            foreach (var entry in ranking)
            {
                string you = entry.ClientId == manager.LocalClientId ? " (вы)" : "";
                DrawBoxLabel(entry.Slot, $"{BoxNames[entry.Slot]}{you} · {entry.Score}");
            }
            GUILayout.EndArea();
        }

        static void DrawBoxLabel(int slot, string label)
        {
            var previous = GUI.contentColor;
            GUI.contentColor = BoxColors[slot];
            GUILayout.Label(label);
            GUI.contentColor = previous;
        }

        readonly struct GUIEnabledScope : System.IDisposable
        {
            readonly bool previous;

            public GUIEnabledScope(bool enabled)
            {
                previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose() => GUI.enabled = previous;
        }
    }
}
