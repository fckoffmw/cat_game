using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class DeliveryBox : MonoBehaviour
    {
        [SerializeField] int slot;

        internal int Slot => slot;

        void OnTriggerStay(Collider other)
        {
            var match = RoofMatch.Instance;
            if (!match || !match.IsServer) return;
            if (other.TryGetComponent<CatInventory>(out var bag))
                match.TryScore(slot, bag);
        }
    }
}
