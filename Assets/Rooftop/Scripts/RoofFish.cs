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
        double readyAt;

        public override void OnNetworkSpawn()
        {
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
            if (!IsSpawned || !IsServer || holder.Value != Available) return;
            var bag = other.GetComponentInParent<CatInventory>();
            if (bag && bag.TryCarry(this)) holder.Value = bag.OwnerClientId;
        }

        internal void ReturnAfterDelivery()
        {
            holder.Value = Respawning;
            readyAt = NetworkManager.ServerTime.Time + 5;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            if (holder.Value == Respawning && NetworkManager.ServerTime.Time >= readyAt)
                holder.Value = Available;
            else if (holder.Value != Available && holder.Value != Respawning &&
                     !NetworkManager.ConnectedClients.ContainsKey(holder.Value))
                holder.Value = Available;
        }
    }
}
