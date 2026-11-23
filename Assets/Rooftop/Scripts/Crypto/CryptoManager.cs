using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;

namespace Rooftop.Crypto
{
    // Isolated primitives. Authenticate peer public keys separately before using the derived key.
    // This class does not implement a network handshake or replay protection.
    public static class CryptoManager
    {
        public const int KeySize = 32;
        public const int NonceSize = 12;
        public const int TagSize = 16;
        static readonly SecureRandom Random = new SecureRandom();
        static readonly byte[] DefaultInfo = Encoding.UTF8.GetBytes("Rooftop/PR6/AES-256-GCM/v1");

        public sealed class KeyPair : IDisposable
        {
            internal readonly byte[] PrivateKey;
            readonly byte[] publicKey;
            internal bool IsDisposed { get; private set; }

            internal KeyPair(byte[] privateKey, byte[] publicKey)
            {
                PrivateKey = privateKey;
                this.publicKey = publicKey;
            }

            public byte[] ExportPublicKey()
            {
                if (IsDisposed) throw new ObjectDisposedException(nameof(KeyPair));
                return (byte[])publicKey.Clone();
            }

            public void Dispose()
            {
                if (IsDisposed) return;
                ClearSecret(PrivateKey);
                IsDisposed = true;
            }
        }

        public static KeyPair GenerateKeyPair()
        {
            var privateKey = new X25519PrivateKeyParameters(Random);
            return new KeyPair(privateKey.GetEncoded(), privateKey.GeneratePublicKey().GetEncoded());
        }

        public static byte[] DeriveSharedSecret(KeyPair ownKeyPair, byte[] peerPublicKey)
        {
            if (ownKeyPair == null) throw new ArgumentNullException(nameof(ownKeyPair));
            if (ownKeyPair.IsDisposed) throw new ObjectDisposedException(nameof(KeyPair));
            return DeriveSharedSecret(ownKeyPair.PrivateKey, peerPublicKey);
        }

        // Both keys are raw 32-byte X25519 encodings, not PEM/SPKI containers.
        public static byte[] DeriveSharedSecret(byte[] ownPrivateKey, byte[] peerPublicKey)
        {
            RequireLength(ownPrivateKey, KeySize, nameof(ownPrivateKey));
            RequireLength(peerPublicKey, KeySize, nameof(peerPublicKey));
            var secret = new byte[KeySize];
            try
            {
                var privateKey = new X25519PrivateKeyParameters(ownPrivateKey, 0);
                var publicKey = new X25519PublicKeyParameters(peerPublicKey, 0);
                privateKey.GenerateSecret(publicKey, secret, 0);
                int nonzero = 0;
                foreach (byte value in secret) nonzero |= value;
                if (nonzero == 0) throw new CryptographicException("X25519 returned an all-zero shared secret.");
                return secret;
            }
            catch (InvalidOperationException e)
            {
                ClearSecret(secret);
                throw new CryptographicException("Invalid X25519 peer public key.", e);
            }
            catch
            {
                ClearSecret(secret);
                throw;
            }
        }

        // RFC 5869 HKDF-SHA256 (extract + expand). Both peers must use the same salt and info.
        public static byte[] DeriveAesKey(byte[] sharedSecret, byte[] salt = null, byte[] info = null)
        {
            if (sharedSecret == null) throw new ArgumentNullException(nameof(sharedSecret));
            if (sharedSecret.Length == 0) throw new ArgumentException("Secret must not be empty.", nameof(sharedSecret));
            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(sharedSecret, salt, info ?? DefaultInfo));
            var key = new byte[KeySize];
            hkdf.GenerateBytes(key, 0, key.Length);
            return key;
        }

        // Packet layout: nonce[12] || ciphertext[N] || authenticationTag[16].
        // Each call creates a fresh random nonce; callers cannot accidentally reuse a supplied nonce.
        public static byte[] EncryptAES(byte[] aesKey, byte[] plaintext, byte[] associatedData = null)
        {
            RequireLength(aesKey, KeySize, nameof(aesKey));
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            var nonce = new byte[NonceSize];
            Random.NextBytes(nonce);
            return EncryptWithNonce(aesKey, plaintext, associatedData, nonce);
        }

        // Transport uses a fresh directional session key and a monotonic counter.
        public static byte[] EncryptAES(byte[] aesKey, byte[] plaintext, ulong counter, byte[] associatedData)
        {
            RequireLength(aesKey, KeySize, nameof(aesKey));
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            var nonce = new byte[NonceSize];
            for (int i = 0; i < 8; i++) nonce[4 + i] = (byte)(counter >> (8 * i));
            return EncryptWithNonce(aesKey, plaintext, associatedData, nonce);
        }

        static byte[] EncryptWithNonce(byte[] aesKey, byte[] plaintext, byte[] associatedData, byte[] nonce)
        {
            var cipher = CreateGcm(true, aesKey, nonce, associatedData);
            var packet = new byte[checked(NonceSize + cipher.GetOutputSize(plaintext.Length))];
            Buffer.BlockCopy(nonce, 0, packet, 0, NonceSize);
            int written = cipher.ProcessBytes(plaintext, 0, plaintext.Length, packet, NonceSize);
            cipher.DoFinal(packet, NonceSize + written);
            return packet;
        }

        public static byte[] DecryptAES(byte[] aesKey, byte[] packet, byte[] associatedData = null)
        {
            RequireLength(aesKey, KeySize, nameof(aesKey));
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            if (packet.Length < NonceSize + TagSize) throw new CryptographicException("Truncated AES-GCM packet.");
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(packet, 0, nonce, 0, NonceSize);
            var cipher = CreateGcm(false, aesKey, nonce, associatedData);
            var plaintext = new byte[cipher.GetOutputSize(packet.Length - NonceSize)];
            try
            {
                int written = cipher.ProcessBytes(packet, NonceSize, packet.Length - NonceSize, plaintext, 0);
                cipher.DoFinal(plaintext, written);
                return plaintext; // Never return partial plaintext before successful tag verification.
            }
            catch (InvalidCipherTextException e)
            {
                ClearSecret(plaintext);
                throw new CryptographicException("AES-GCM authentication failed.", e);
            }
            catch
            {
                ClearSecret(plaintext);
                throw;
            }
        }

        static GcmBlockCipher CreateGcm(bool encrypt, byte[] key, byte[] nonce, byte[] aad)
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(encrypt, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce, aad));
            return cipher;
        }

        static void RequireLength(byte[] value, int length, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
            if (value.Length != length) throw new ArgumentException($"Expected {length} bytes.", name);
        }

        // Best-effort clearing of caller-owned arrays; managed/library copies may still exist.
        public static void ClearSecret(byte[] value)
        {
            if (value != null) Array.Clear(value, 0, value.Length);
        }

        // Throws on failure. Covers round-trip, bad keys/tags/AAD and published known-answer vectors.
        public static bool RunSelfTest()
        {
            using (var alice = GenerateKeyPair())
            using (var bob = GenerateKeyPair())
            {
                byte[] secretA = null, secretB = null, keyA = null, keyB = null;
                try
                {
                    secretA = DeriveSharedSecret(alice, bob.ExportPublicKey());
                    secretB = DeriveSharedSecret(bob, alice.ExportPublicKey());
                    Check(Arrays.FixedTimeEquals(secretA, secretB), "Shared secrets differ.");
                    keyA = DeriveAesKey(secretA);
                    keyB = DeriveAesKey(secretB);
                    Check(Arrays.FixedTimeEquals(keyA, keyB), "AES keys differ.");
                    var hello = Encoding.UTF8.GetBytes("Hello");
                    var aad = Encoding.UTF8.GetBytes("PR6/self-test");
                    var packet = EncryptAES(keyA, hello, aad);
                    Check(Encoding.UTF8.GetString(DecryptAES(keyB, packet, aad)) == "Hello", "Hello round-trip failed.");
                    Reject(() => DecryptAES(keyB, new byte[NonceSize + TagSize - 1]));
                    Check(DecryptAES(keyB, EncryptAES(keyA, Array.Empty<byte>())).Length == 0, "Empty message failed.");
                    var largerMessage = new byte[4097];
                    Random.NextBytes(largerMessage);
                    Check(Arrays.AreEqual(largerMessage, DecryptAES(keyB, EncryptAES(keyA, largerMessage))), "Multi-block round-trip failed.");
                    Reject(() => DecryptAES(keyB, packet, new byte[] { 1 }));
                    packet[packet.Length - 1] ^= 1;
                    Reject(() => DecryptAES(keyB, packet, aad));
                    Reject(() => DeriveSharedSecret(alice, new byte[KeySize]));
                    var wrongKey = (byte[])keyB.Clone();
                    wrongKey[0] ^= 1;
                    try { Reject(() => DecryptAES(wrongKey, EncryptAES(keyA, hello))); }
                    finally { ClearSecret(wrongKey); }
                    var independentPublicKey = alice.ExportPublicKey();
                    independentPublicKey[0] ^= 1;
                    Check(!Arrays.AreEqual(independentPublicKey, alice.ExportPublicKey()), "Public key alias exposed.");
                    bob.Dispose();
                    try { DeriveSharedSecret(bob, alice.ExportPublicKey()); throw new Exception("Disposed key accepted."); }
                    catch (ObjectDisposedException) { }
                }
                finally
                {
                    ClearSecret(secretA); ClearSecret(secretB);
                    ClearSecret(keyA); ClearSecret(keyB);
                }
            }

            // RFC 7748 section 6.1: Alice's private key and Bob's public key.
            var vectorSecret = DeriveSharedSecret(
                Hex.Decode("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a"),
                Hex.Decode("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f"));
            Check(Arrays.AreEqual(vectorSecret, Hex.Decode("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742")), "X25519 RFC vector failed.");
            ClearSecret(vectorSecret);
            // First 32 output bytes of RFC 5869 appendix A.1 (L = 42).
            var ikm = new byte[22];
            for (int i = 0; i < ikm.Length; i++) ikm[i] = 0x0b;
            var vectorKey = DeriveAesKey(ikm, Hex.Decode("000102030405060708090a0b0c"), Hex.Decode("f0f1f2f3f4f5f6f7f8f9"));
            Check(Arrays.AreEqual(vectorKey, Hex.Decode("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf")), "HKDF RFC vector failed.");
            ClearSecret(vectorKey);
            // AES-256-GCM known-answer vector: zero key, nonce and 16-byte plaintext.
            var vectorPacket = Hex.Decode("000000000000000000000000cea7403d4d606b6e074ec5d3baf39d18d0d1c8a799996bf0265b98b5d48ab919");
            Check(Arrays.AreEqual(DecryptAES(new byte[KeySize], vectorPacket), new byte[16]), "AES-256-GCM vector failed.");
            return true;
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new CryptographicException("PR6 self-test: " + message);
        }

        static void Reject(Action operation)
        {
            try { operation(); }
            catch (CryptographicException) { return; }
            throw new CryptographicException("PR6 self-test: invalid input was accepted.");
        }
    }
}
