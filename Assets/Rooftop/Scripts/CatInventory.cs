using Unity.Netcode;

namespace Rooftop
{
    public sealed class CatInventory : NetworkBehaviour
    {
        RoofFish carried;
        readonly NetworkVariable<bool> carrying = new();
        public bool HasFish => carrying.Value;

        internal bool TryCarry(RoofFish fish)
        {
            if (!IsSpawned || !IsServer || carrying.Value) return false;
            var match = RoofMatch.Instance;
            if (match && !match.CanCarry(OwnerClientId)) return false;
            carried = fish;
            carrying.Value = true;
            return true;
        }

        internal void ResetForRound()
        {
            if (!IsSpawned || !IsServer) return;
            carried = null;
            carrying.Value = false;
        }

        // Match calls on the server after validating round and box ownership.
        public bool TryDeliver()
        {
            if (!IsSpawned || !IsServer || !carried) return false;
            carried.ReturnAfterDelivery();
            ResetForRound();
            return true;
        }
    }
}
