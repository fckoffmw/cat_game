using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop.Crypto
{
    public sealed class EncryptedTransport : NetworkTransport
    {
        const byte ClientHello = 10, Data = 11, ServerHello = 12, ClientProof = 13, ServerProof = 14;
        const int HeaderSize = 13;
        readonly Dictionary<ulong, Peer> peers = new();
        readonly List<ulong> expired = new();
        CryptoUnityDriver driver;
        bool server;

        sealed class Peer : IDisposable
        {
            public CryptoManager.KeyPair Pair = CryptoManager.GenerateKeyPair();
            public byte[] SendKey, ReceiveKey;
            public ulong Sent;
            public readonly ReplayWindow[] Replay = { new(), new(), new(), new(), new() };
            public bool Ready;
            public double Started = Time.realtimeSinceStartupAsDouble;

            public void Dispose()
            {
                Pair.Dispose();
                CryptoManager.ClearSecret(SendKey);
                CryptoManager.ClearSecret(ReceiveKey);
            }
        }

        // Separate receive windows prevent unreliable snapshots from aging out delayed reliable data.
        sealed class ReplayWindow
        {
            ulong highest, window;
            bool hasReceived;

            public bool IsFresh(ulong sequence) => !hasReceived || sequence > highest ||
                (highest - sequence < 64 && (window & (1UL << (int)(highest - sequence))) == 0);

            public void Accept(ulong sequence)
            {
                if (!hasReceived)
                {
                    highest = sequence;
                    window = 1;
                    hasReceived = true;
                    return;
                }
                if (sequence > highest)
                {
                    ulong distance = sequence - highest;
                    window = distance >= 64 ? 1 : (window << (int)distance) | 1;
                    highest = sequence;
                }
                else window |= 1UL << (int)(highest - sequence);
            }
        }

        public static void RunSelfTest()
        {
            CryptoManager.RunSelfTest();
            var replay = new ReplayWindow();
            if (!replay.IsFresh(8)) throw new Exception("Initial sequence");
            replay.Accept(8);
            if (replay.IsFresh(8)) throw new Exception("Duplicate accepted");
            if (!replay.IsFresh(7)) throw new Exception("Reordered packet rejected");
            replay.Accept(7);
            if (replay.IsFresh(7)) throw new Exception("Reordered duplicate accepted");
            replay.Accept(100);
            if (replay.IsFresh(8)) throw new Exception("Old packet accepted");
            var key = new byte[32];
            var header = Header(Data, 2);
            var ciphertext = CryptoManager.EncryptAES(key, new byte[] { 42 }, 2UL, header);
            if (ciphertext[4] != 2) throw new Exception("Counter nonce");
            header[4] ^= 1;
            try
            {
                CryptoManager.DecryptAES(key, ciphertext, header);
                throw new Exception("Modified header accepted");
            }
            catch (CryptographicException) { }
            Debug.Log("PR6 TRANSPORT_SELFTEST PASS: replay window, counter nonce, authenticated header");
        }

        public override ulong ServerClientId => driver.ServerClientId;

        public override void Initialize(NetworkManager networkManager = null)
        {
            driver = GetComponent<CryptoUnityDriver>();
            if (!driver) driver = gameObject.AddComponent<CryptoUnityDriver>();
            var original = GetComponent<UnityTransport>();
            driver.ConnectionData = original.ConnectionData;
            // Null manager selects UTP's standalone clock and avoids its private NGO metrics dependency.
            driver.Initialize();
            driver.OnTransportEvent -= Handle;
            driver.OnTransportEvent += Handle;
        }

        public override bool StartClient()
        {
            server = false;
            return driver.StartClient();
        }

        public override bool StartServer()
        {
            server = true;
            return driver.StartServer();
        }
        public override ulong GetCurrentRtt(ulong clientId) => driver.GetCurrentRtt(clientId);
        public override void DisconnectRemoteClient(ulong clientId) => driver.DisconnectRemoteClient(clientId);
        public override void DisconnectLocalClient() => driver.DisconnectLocalClient();
        public override NetworkEvent PollEvent(out ulong id, out ArraySegment<byte> payload, out float time)
        {
            id = 0;
            payload = default;
            time = 0;
            return NetworkEvent.Nothing;
        }

        protected override void OnEarlyUpdate()
        {
            driver.Receive();
            expired.Clear();
            foreach (var entry in peers)
                if (!entry.Value.Ready && Time.realtimeSinceStartupAsDouble - entry.Value.Started > 10)
                    expired.Add(entry.Key);
            foreach (var id in expired)
            {
                Debug.LogWarning($"PR6 HANDSHAKE_TIMEOUT peer={id}");
                if (server) DisconnectRemoteClient(id);
                else DisconnectLocalClient();
                Remove(id);
            }
        }

        protected override void OnPostLateUpdate() => driver.Flush();

        public override void Shutdown()
        {
            if (driver)
            {
                driver.OnTransportEvent -= Handle;
                driver.Shutdown();
            }
            foreach (var peer in peers.Values) peer.Dispose();
            peers.Clear();
        }

        void Remove(ulong id)
        {
            if (peers.TryGetValue(id, out var peer)) peer.Dispose();
            peers.Remove(id);
        }

        void Handle(NetworkEvent kind, ulong id, ArraySegment<byte> payload, float time)
        {
            if (kind == NetworkEvent.Connect)
            {
                Remove(id);
                if (peers.Count >= 16)
                {
                    if (server) driver.DisconnectRemoteClient(id);
                    return;
                }
                var peer = new Peer();
                peers.Add(id, peer);
                if (!server) Hello(id, ClientHello, peer.Pair.ExportPublicKey());
                return;
            }
            if (kind == NetworkEvent.Disconnect)
            {
                Remove(id);
                InvokeOnTransportEvent(kind, id, default, time);
                return;
            }
            if (kind != NetworkEvent.Data)
            {
                InvokeOnTransportEvent(kind, id, payload, time);
                return;
            }
            if (!peers.TryGetValue(id, out var session)) return;
            try { Receive(id, session, payload, time); }
            catch (CryptographicException) { Debug.LogWarning($"PR6 AUTH_FAIL peer={id}"); }
        }

        void Hello(ulong id, byte type, byte[] key)
        {
            var packet = new byte[37];
            Buffer.BlockCopy(Header(type, 0), 0, packet, 0, 5);
            Buffer.BlockCopy(key, 0, packet, 5, 32);
            driver.Send(id, new ArraySegment<byte>(packet), NetworkDelivery.ReliableSequenced);
        }

        void Derive(Peer peer, byte[] remoteKey)
        {
            var localKey = peer.Pair.ExportPublicKey();
            var transcript = new byte[64];
            Buffer.BlockCopy(server ? remoteKey : localKey, 0, transcript, 0, 32);
            Buffer.BlockCopy(server ? localKey : remoteKey, 0, transcript, 32, 32);
            byte[] secret = CryptoManager.DeriveSharedSecret(peer.Pair, remoteKey);
            try
            {
                peer.SendKey = CryptoManager.DeriveAesKey(secret, transcript,
                    Encoding.UTF8.GetBytes(server ? "Rooftop/v1/server" : "Rooftop/v1/client"));
                peer.ReceiveKey = CryptoManager.DeriveAesKey(secret, transcript,
                    Encoding.UTF8.GetBytes(server ? "Rooftop/v1/client" : "Rooftop/v1/server"));
            }
            finally { CryptoManager.ClearSecret(secret); }
        }

        void Receive(ulong id, Peer peer, ArraySegment<byte> payload, float time)
        {
            if (payload.Count < 5) throw new CryptographicException();
            var packet = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, packet, 0, packet.Length);
            if (packet[0] != 'R' || packet[1] != 'F' || packet[2] != '6' || packet[3] != 1)
                throw new CryptographicException();
            byte type = (byte)(packet[4] & 15);
            int lane = packet[4] >> 4;
            if (lane > 4) throw new CryptographicException();
            if ((server && type == ClientHello) || (!server && type == ServerHello))
            {
                if (peer.ReceiveKey != null) return;
                if (packet.Length != 37 || lane != 0) throw new CryptographicException();
                var remote = new byte[32];
                Buffer.BlockCopy(packet, 5, remote, 0, 32);
                Derive(peer, remote);
                if (server) Hello(id, ServerHello, peer.Pair.ExportPublicKey());
                else Seal(id, peer, ClientProof, new byte[] { 1 }, NetworkDelivery.ReliableSequenced);
                return;
            }
            if (peer.ReceiveKey == null || packet.Length < HeaderSize + 28) throw new CryptographicException();
            ulong sequence = 0;
            for (int i = 0; i < 8; i++) sequence |= (ulong)packet[5 + i] << (8 * i);
            var replay = peer.Replay[lane];
            if (!replay.IsFresh(sequence))
            {
                Debug.LogWarning($"PR6 REPLAY_DROP peer={id}");
                return;
            }
            var header = Header(packet[4], sequence);
            var encrypted = new byte[packet.Length - HeaderSize];
            Buffer.BlockCopy(packet, HeaderSize, encrypted, 0, encrypted.Length);
            var plaintext = CryptoManager.DecryptAES(peer.ReceiveKey, encrypted, header);
            replay.Accept(sequence);
            if (!peer.Ready)
            {
                byte expected = server ? ClientProof : ServerProof;
                if (type != expected || plaintext.Length != 1 || plaintext[0] != (server ? 1 : 2))
                    throw new CryptographicException();
                if (server) Seal(id, peer, ServerProof, new byte[] { 2 }, NetworkDelivery.ReliableSequenced);
                peer.Ready = true;
                peer.Pair.Dispose();
                Debug.Log($"PR6 SECURE_CONNECTED peer={id}");
                InvokeOnTransportEvent(NetworkEvent.Connect, id, default, time);
            }
            else if (type == Data)
                InvokeOnTransportEvent(NetworkEvent.Data, id, new ArraySegment<byte>(plaintext), time);
        }

        public override void Send(ulong id, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (!peers.TryGetValue(id, out var peer) || !peer.Ready) return;
            var plaintext = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, plaintext, 0, plaintext.Length);
            Seal(id, peer, Data, plaintext, delivery);
        }

        static byte[] Header(byte type, ulong sequence)
        {
            var header = new byte[HeaderSize];
            header[0] = (byte)'R';
            header[1] = (byte)'F';
            header[2] = (byte)'6';
            header[3] = 1;
            header[4] = type;
            for (int i = 0; i < 8; i++) header[5 + i] = (byte)(sequence >> (8 * i));
            return header;
        }

        void Seal(ulong id, Peer peer, byte type, byte[] plaintext, NetworkDelivery delivery)
        {
            if (peer.Sent == ulong.MaxValue) throw new CryptographicException("Session counter exhausted");
            ulong sequence = peer.Sent++;
            var header = Header((byte)(type | ((int)delivery << 4)), sequence);
            var encrypted = CryptoManager.EncryptAES(peer.SendKey, plaintext, sequence, header);
            var packet = new byte[HeaderSize + encrypted.Length];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(encrypted, 0, packet, header.Length, encrypted.Length);
            driver.Send(id, new ArraySegment<byte>(packet), delivery);
        }
    }
}
