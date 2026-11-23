using System;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Rooftop.Crypto.Editor
{
    public static class AesGcmTests
    {
        [MenuItem("Tools/Rooftop/Crypto/Run AES-GCM Tests")]
        public static void Run()
        {
            var key = new byte[32];
            var aad = new byte[] { 82, 70, 54, 1, 11 };
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
            try
            {
                foreach (int size in new[] { 0, 1, 15, 16, 17, 1200, 4097 })
                {
                    var original = new byte[size];
                    for (int i = 0; i < size; i++) original[i] = (byte)i;
                    var packet = CryptoManager.EncryptAES(key, original, (ulong)size, aad);
                    var restored = CryptoManager.DecryptAES(key, packet, aad);
                    if (restored.Length != original.Length) throw new Exception("Length mismatch");
                    for (int i = 0; i < size; i++)
                        if (restored[i] != original[i]) throw new Exception("Content mismatch");
                    Debug.Log($"PR6 AES ROUNDTRIP PASS bytes={size}");
                }
                var valid = CryptoManager.EncryptAES(key, new byte[32], 100UL, aad);
                // Each mutation starts from an independent copy of a valid message.
                foreach (int position in new[] { 0, 11, 12, 27, 43, 44, valid.Length - 1 })
                {
                    var damaged = (byte[])valid.Clone();
                    damaged[position] ^= 1;
                    Reject(() => CryptoManager.DecryptAES(key, damaged, aad), $"bit flip offset={position}");
                }
                var wrongKey = (byte[])key.Clone();
                wrongKey[0] ^= 1;
                try { Reject(() => CryptoManager.DecryptAES(wrongKey, valid, aad), "wrong key"); }
                finally { CryptoManager.ClearSecret(wrongKey); }
                var wrongAad = (byte[])aad.Clone();
                wrongAad[4] ^= 1;
                Reject(() => CryptoManager.DecryptAES(key, valid, wrongAad), "changed AAD");
                Reject(() => CryptoManager.DecryptAES(key, valid), "missing AAD");
                foreach (int length in new[] { 0, 12, 27, valid.Length - 1 })
                {
                    var truncated = new byte[length];
                    Buffer.BlockCopy(valid, 0, truncated, 0, length);
                    Reject(() => CryptoManager.DecryptAES(key, truncated, aad), $"truncated length={length}");
                }
                var extended = new byte[valid.Length + 1];
                Buffer.BlockCopy(valid, 0, extended, 0, valid.Length);
                Reject(() => CryptoManager.DecryptAES(key, extended, aad), "appended byte");
                Debug.Log("PR6 AES TESTS PASS");
            }
            finally { CryptoManager.ClearSecret(key); }
        }

        static void Reject(Func<byte[]> decrypt, string description)
        {
            try
            {
                var result = decrypt();
                CryptoManager.ClearSecret(result);
            }
            catch (CryptographicException)
            {
                Debug.Log("PR6 AES REJECT PASS " + description);
                return;
            }
            throw new Exception("Unauthenticated plaintext returned: " + description);
        }
    }
}
