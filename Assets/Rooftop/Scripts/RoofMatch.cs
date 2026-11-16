using System;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RoofMatch : NetworkBehaviour
    {
        internal const int MaxPlayers = 3;
        internal const int MinPlayers = 2;
        internal const int RoundSeconds = 60;

        internal enum Phase : byte { Lobby, Playing, Finished }

        internal struct RoundState : INetworkSerializable, IEquatable<RoundState>
        {
            public Phase Phase;
            public double EndsAt;
            public int WinnerSlot;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Phase);
                if (Phase == RoofMatch.Phase.Playing) serializer.SerializeValue(ref EndsAt);
                else EndsAt = 0;
                if (Phase == RoofMatch.Phase.Finished) serializer.SerializeValue(ref WinnerSlot);
                else WinnerSlot = -1;
            }

            public bool Equals(RoundState other) =>
                Phase == other.Phase && EndsAt == other.EndsAt && WinnerSlot == other.WinnerSlot;
        }

        internal struct Standing : INetworkSerializable, IEquatable<Standing>
        {
            public ulong ClientId;
            public int Slot;
            public int Score;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ClientId);
                serializer.SerializeValue(ref Slot);
                serializer.SerializeValue(ref Score);
            }

            public bool Equals(Standing other) =>
                ClientId == other.ClientId && Slot == other.Slot && Score == other.Score;
        }

        internal static RoofMatch Instance { get; private set; }

        readonly NetworkVariable<RoundState> round = new(new RoundState { WinnerSlot = -1 });
        NetworkList<Standing> standings;

        internal RoundState Round => round.Value;
        internal int PlayerCount => standings.Count;
        internal Standing GetStanding(int index) => standings[index];
        internal bool IsRoundActive => Round.Phase == Phase.Playing && TimeRemaining > 0;
        internal double TimeRemaining => Math.Max(0, Round.EndsAt - NetworkManager.ServerTime.Time);

        internal bool TryGetStanding(ulong clientId, out Standing standing)
        {
            int index = IndexOf(clientId);
            standing = index >= 0 ? standings[index] : default;
            return index >= 0;
        }

        void Awake() => standings = new NetworkList<Standing>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) RefreshStandings();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || NetworkManager.ShutdownInProgress) return;
            if (Round.Phase == Phase.Finished) return;
            RefreshStandings();
            if (Round.Phase == Phase.Playing && !IsRoundActive)
            {
                FreezeResult();
                return;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (IsRoundActive && BroadcastFullState && NetworkManager.ServerTime.Time >= nextBroadcast)
            {
                FullMatchStateRpc((float)TimeRemaining, ScoreForSlot(0), ScoreForSlot(1), ScoreForSlot(2));
                nextBroadcast = NetworkManager.ServerTime.Time + .1;
            }
#endif
        }

        internal bool TryStartRound()
        {
            if (!IsSpawned || !IsServer || Round.Phase == Phase.Playing ||
                NetworkManager.ConnectedClientsIds.Count < MinPlayers) return false;

            standings.Clear();
            RefreshStandings();
            if (PlayerCount < MinPlayers) return false;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (!client.PlayerObject) continue;
                client.PlayerObject.GetComponent<CatInventory>().ResetForRound();
                client.PlayerObject.GetComponent<PracticeCat>().ResetForRound();
            }
            foreach (var fish in FindObjectsByType<RoofFish>(FindObjectsSortMode.None))
                if (fish.IsSpawned) fish.ResetForRound();

            round.Value = new RoundState
            {
                Phase = Phase.Playing,
                EndsAt = NetworkManager.ServerTime.Time + RoundSeconds,
                WinnerSlot = -1
            };
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            nextBroadcast = 0;
#endif
            return true;
        }

        internal bool CanCarry(ulong clientId) =>
            IsServer && IsRoundActive && IndexOf(clientId) >= 0;

        internal bool TryScore(int slot, CatInventory bag)
        {
            if (!IsServer || !IsRoundActive) return false;
            int index = IndexOf(bag.OwnerClientId);
            if (index < 0) return false;
            var standing = standings[index];
            if (standing.Slot != slot || !bag.TryDeliver()) return false;
            standing.Score++;
            standings[index] = standing;
            return true;
        }

        int IndexOf(ulong clientId)
        {
            for (int i = 0; i < standings.Count; i++)
                if (standings[i].ClientId == clientId) return i;
            return -1;
        }

        void RefreshStandings()
        {
            for (int i = standings.Count - 1; i >= 0; i--)
                if (!NetworkManager.ConnectedClients.ContainsKey(standings[i].ClientId))
                    standings.RemoveAt(i);

            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (!client.PlayerObject || IndexOf(client.ClientId) >= 0) continue;
                int slot = client.PlayerObject.GetComponent<PracticeCat>().SpawnSlot;
                if (slot >= 0 && slot < MaxPlayers)
                    standings.Add(new Standing { ClientId = client.ClientId, Slot = slot });
            }
        }

        void FreezeResult()
        {
            int highest = -1;
            int winner = -1;
            for (int i = 0; i < standings.Count; i++)
            {
                var standing = standings[i];
                if (standing.Score == highest) winner = -1;
                if (standing.Score <= highest) continue;
                highest = standing.Score;
                winner = standing.Slot;
            }
            var finished = Round;
            finished.Phase = Phase.Finished;
            finished.EndsAt = 0;
            finished.WinnerSlot = winner;
            round.Value = finished;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal static bool BroadcastFullState;
        double nextBroadcast;

        int ScoreForSlot(int slot)
        {
            for (int i = 0; i < standings.Count; i++)
                if (standings[i].Slot == slot) return standings[i].Score;
            return -1;
        }

        // Educational baseline: redundant full timer and score snapshots, like the fish baseline.
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void FullMatchStateRpc(float remaining, int cyan, int magenta, int orange) { }
#endif
    }
}
