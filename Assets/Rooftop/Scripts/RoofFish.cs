using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class RoofFish : NetworkBehaviour
    {
        // Available -> carried by one player -> waiting at the original spawn -> available.
        const ulong Available = ulong.MaxValue;
        const ulong Respawning = ulong.MaxValue - 1;
        readonly NetworkVariable<ulong> holder = new(Available);
        ulong authoritativeHolder = Available;
        double readyAt, nextBroadcast;

        void SetHolder(ulong value)
        {
            authoritativeHolder = value;
            if (RoofConnection.FullUpdates) ShowState(Available, value);
            else holder.Value = value;
        }

        [Rpc(SendTo.NotServer)]
        void FullStateRpc(ulong value) => ShowState(Available, value);


        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                authoritativeHolder = Available;
                holder.Value = Available;
                nextBroadcast = 0;
            }
            holder.OnValueChanged += ShowState;
            ShowState(Available, holder.Value);
        }

        public override void OnNetworkDespawn() => holder.OnValueChanged -= ShowState;

        void ShowState(ulong previous, ulong current)
        {
            GetComponent<Renderer>().enabled = current == Available;
            GetComponent<Collider>().enabled = current == Available;
        }

        void OnTriggerStay(Collider other)
        {
            if (!IsSpawned || !IsServer || authoritativeHolder != Available) return;
            var bag = other.GetComponentInParent<CatInventory>();
            if (bag && bag.TryCarry(this)) SetHolder(bag.OwnerClientId);
        }

        internal void ResetForRound()
        {
            SetHolder(Available);
            readyAt = 0;
        }

        internal void ReturnAfterDelivery()
        {
            SetHolder(Respawning);
            readyAt = NetworkManager.ServerTime.Time + 5;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            if (RoofConnection.FullUpdates && NetworkManager.ServerTime.Time >= nextBroadcast)
            {
                FullStateRpc(authoritativeHolder);
                nextBroadcast = NetworkManager.ServerTime.Time + .1;
            }
            if (authoritativeHolder == Respawning && NetworkManager.ServerTime.Time >= readyAt)
                SetHolder(Available);
            else if (authoritativeHolder != Available && authoritativeHolder != Respawning &&
                     !NetworkManager.ConnectedClients.ContainsKey(authoritativeHolder))
                SetHolder(Available);
        }
    }
}
