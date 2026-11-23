using Unity.Netcode.Transports.UTP;

namespace Rooftop.Crypto
{
    // NetworkManager pumps the outer transport; expose the inner UTP lifecycle explicitly.
    public sealed class CryptoUnityDriver : UnityTransport
    {
        public void Receive() => base.OnEarlyUpdate();
        public void Flush() => base.OnPostLateUpdate();
    }
}
