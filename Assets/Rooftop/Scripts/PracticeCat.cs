using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    // Baseline for PR 4. Only the server moves the controller.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PracticeCat : NetworkBehaviour
    {
        Vector2 direction;
        bool jumpRequested;
        float verticalSpeed;
        double nextJump;
        double lastInput;
        Vector2 lastSent;
        float nextSend;

        public override void OnNetworkSpawn()
        {
            if (IsServer) transform.position = new Vector3(OwnerClientId % 2 == 0 ? -9 : 9, 1, (int)(OwnerClientId % 3) - 1);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool jump = Input.GetKeyDown(KeyCode.Space);
            if (!jump && input == lastSent && Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + .15f;
            lastSent = input;
            SubmitRpc(input,jump);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitRpc(Vector2 input, bool jump)
        {
            if (!float.IsFinite(input.x) || !float.IsFinite(input.y)) return;
            direction = Vector2.ClampMagnitude(input, 1);
            jumpRequested |= jump;
            lastInput = NetworkManager.ServerTime.Time;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            var controller = GetComponent<CharacterController>();
            double now = NetworkManager.ServerTime.Time;
            if (now - lastInput > .6) direction = Vector2.zero;
            if (controller.isGrounded)
            {
                verticalSpeed = -1;
                if (jumpRequested && now >= nextJump)
                {
                    verticalSpeed = 6;
                    nextJump = now + 3;
                }
            }
            jumpRequested = false;
            verticalSpeed -= 16 * Time.fixedDeltaTime;
            controller.Move(new Vector3(direction.x * 5, verticalSpeed, direction.y * 5) * Time.fixedDeltaTime);
        }
    }
}
