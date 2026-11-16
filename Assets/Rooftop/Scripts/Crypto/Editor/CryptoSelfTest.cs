using UnityEditor;
using UnityEngine;

namespace Rooftop.Crypto.Editor
{
    public static class CryptoSelfTest
    {
        [MenuItem("Tools/Rooftop/Crypto/Run Self Test")]
        public static void Run()
        {
            CryptoManager.RunSelfTest();
            Debug.Log("PR6 CRYPTO PASS: X25519, HKDF-SHA256, AES-256-GCM, Hello, authentication failures, RFC vectors.");
        }
    }
}
