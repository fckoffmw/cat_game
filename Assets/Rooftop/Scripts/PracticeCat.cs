using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PracticeCat : NetworkBehaviour
    {
        struct Move
        {
            public int number;
            public Vector2 direction;
            public bool jump;
            public bool simulateEarly;
        }

        public static bool Prediction = true;
        public string LastEvent { get; private set; } = "";
        readonly Queue<Move> incoming = new();
        readonly List<Move> pending = new();
        CharacterController controller;
        int sequence, received, acknowledged, lastSnapshot = -1, tick;
        float vertical, cooldown;
        bool grounded, jumpPressed, forceEarly, ready;
        Vector3 remotePosition;
        float remoteYaw;
        float pressedAt = -1;
        int jumpSequence;
        public bool ProbeInput;
        public Vector2 ProbeDirection;
        public void RequestJump(bool early = false)
        {
            jumpPressed = true;
            forceEarly = early;
        }

        public override void OnNetworkSpawn()
        {
            controller = GetComponent<CharacterController>();
            if (IsServer)
            {
                Warp(new Vector3(OwnerClientId % 2 == 0 ? -9 : 9, 1.1f, OwnerClientId % 3 == 2 ? -3 : 3));
                ready = true;
            }
            else if (!IsOwner) controller.enabled = false;
        }

        void Warp(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner && !ProbeInput)
            {
                if (Input.GetKeyDown(KeyCode.Space)) RequestJump();
                if (!IsServer && Input.GetKeyDown(KeyCode.F8)) RequestJump(true);
            }
            if (!IsOwner && !IsServer && ready)
            {
                float blend = 1 - Mathf.Exp(-20 * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, remotePosition, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0,remoteYaw,0), blend);
            }
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !ready) return;
            if (IsOwner)
            {
                var direction = ProbeInput ? ProbeDirection : new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
                var move = new Move { number = ++sequence, direction = Vector2.ClampMagnitude(direction,1), jump = jumpPressed, simulateEarly = forceEarly && !IsServer };
                if (move.jump)
                {
                    jumpSequence = sequence;
                    pressedAt = Time.realtimeSinceStartup;
                    Report("INPUT", sequence);
                }
                if (IsServer) incoming.Enqueue(move);
                else
                {
                    pending.Add(move);
                    if (Prediction)
                    {
                        if (Step(move)) Report("LOCAL_JUMP", sequence);
                    }
                    SubmitRpc(move.number, move.direction, move.jump);
                }
                forceEarly = jumpPressed = false;
            }
            if (!IsServer) return;
            tick++;
            bool jump = false;
            if (incoming.Count > 0)
            {
                var move = incoming.Dequeue();
                acknowledged = move.number;
                jump = move.jump;
                bool accepted = Step(move);
                if (jump) Report(accepted ? "ACCEPT" : "REJECT", acknowledged);
            }
            // Include the exact state of every jump, even between regular snapshots.
            if (jump || tick % 2 == 0)
                StateRpc(tick, acknowledged, transform.position, transform.eulerAngles.y, vertical, cooldown, grounded);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitRpc(int number, Vector2 direction, bool jump)
        {
            if (number <= received || !float.IsFinite(direction.x) || !float.IsFinite(direction.y)) return;
            received = number;
            if (incoming.Count < 100)
                incoming.Enqueue(new Move { number = number, direction = Vector2.ClampMagnitude(direction,1), jump = jump });
        }

        bool Step(Move move)
        {
            if (move.simulateEarly) cooldown = 0;
            float dt = Time.fixedDeltaTime;
            cooldown = Mathf.Max(0, cooldown - dt);
            bool launch = grounded && move.jump && cooldown <= 0;
            if (grounded) vertical = -1;
            if (launch) { vertical = 6; cooldown = 3; }
            vertical -= 16 * dt;
            var planar = new Vector3(move.direction.x,0,move.direction.y);
            if (planar.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(planar);
            var flags = controller.Move((planar * 5 + Vector3.up * vertical) * dt);
            grounded = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0) vertical = Mathf.Min(vertical,0);
            return launch;
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        void StateRpc(int serverTick, int ack, Vector3 position, float yaw, float speed, float remaining, bool onGround)
        {
            if (IsServer || serverTick <= lastSnapshot) return;
            lastSnapshot = serverTick;
            if (!IsOwner)
            {
                remotePosition = position;
                remoteYaw = yaw;
                if (!ready) transform.position = position;
            }
            else
            {
                var before = transform.position;
                Warp(position);
                transform.rotation = Quaternion.Euler(0,yaw,0);
                vertical = speed;
                grounded = onGround;
                cooldown = remaining;
                pending.RemoveAll(command => command.number <= ack);
                if (Prediction) foreach (var command in pending) Step(command);
                if (pressedAt >= 0 && ack >= jumpSequence)
                {
                    Report("ACK", ack, $"after_ms={(Time.realtimeSinceStartup-pressedAt)*1000:F0} delta_y={transform.position.y-before.y:F3} replay={pending.Count} server_y={position.y:F3}");
                    pressedAt = -1;
                }
            }
            ready = true;
        }

        void Report(string label, int number, string details = "")
        {
            LastEvent = $"{label} #{number} {details}";
            Debug.Log($"PR4 player={OwnerClientId} {LastEvent} y={transform.position.y:F3}");
        }
    }
}
