using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop.UI
{
    public sealed class ScoreboardUI : MonoBehaviour
    {
        [SerializeField] TMP_Text scores;
        readonly List<ulong> players = new();
        readonly StringBuilder text = new();
        float nextRefresh;

        void OnEnable() => Refresh();

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        public void Refresh()
        {
            nextRefresh = Time.unscaledTime + .5f;
            if (!scores) return;
            players.Clear();
            foreach (var cat in FindObjectsByType<PracticeCat>(FindObjectsSortMode.None))
                if (cat.IsSpawned && !players.Contains(cat.OwnerClientId)) players.Add(cat.OwnerClientId);
            players.Sort();
            text.Clear();
            var manager = NetworkManager.Singleton;
            foreach (ulong id in players)
            {
                string you = manager && manager.LocalClientId == id ? " (вы)" : "";
                // RoofMatch has no public score API. A dash means unavailable, not zero points.
                text.Append("Игрок ").Append(id + 1).Append(you).Append("   —\n");
            }
            scores.text = players.Count == 0 ? "Ожидание игроков…" : text.ToString();
        }
    }
}
