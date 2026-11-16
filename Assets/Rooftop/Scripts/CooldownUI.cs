using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Rooftop
{
    [RequireComponent(typeof(Image))]
    public sealed class CooldownUI : MonoBehaviour
    {
        const float JumpCooldown = 3f;
        Image icon;

        void Awake()
        {
            icon = GetComponent<Image>();
            icon.enabled = false;
        }

        void LateUpdate()
        {
            var manager = NetworkManager.Singleton;
            var player = manager && manager.IsConnectedClient
                ? manager.LocalClient?.PlayerObject : null;

            // Resolve the current player each frame, including reconnects and respawns.
            if (!player || !player.IsSpawned ||
                !player.TryGetComponent<PracticeCat>(out var cat) || !cat.IsOwner)
            {
                icon.enabled = false;
                return;
            }

            icon.fillAmount = 1f - Mathf.Clamp01(cat.GetCooldown() / JumpCooldown);
            icon.enabled = true;
        }

        void OnDisable()
        {
            if (icon) icon.enabled = false;
        }
    }
}
