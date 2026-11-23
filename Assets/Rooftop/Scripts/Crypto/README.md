# ПР-6: X25519 → HKDF-SHA256 → AES-256-GCM

CryptoManager содержит криптографические примитивы Арины. EncryptedTransport подключает их к NGO через Unity Transport. Ручная установка библиотеки не нужна.

Библиотека: [BouncyCastle.Cryptography 2.7.0](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0),
официальный NuGet, сборка `lib/netstandard2.0/BouncyCastle.Cryptography.dll`.
Лицензия сохранена в `LICENSE.md`.

SHA-256 DLL: `D61C1F2BA929A230A58E101CCD850E21F2675FA6B9814EC279633E8A089C3495`.
SHA-256 NuGet-пакета: `F091FFCCAB4D03993E660BACE277659A79DEE0972F54D7F1F4BD46D680966241`.

## Запуск

Unity: **Tools → Rooftop → Crypto → Run Self Test**.
Или вызвать `CryptoManager.RunSelfTest()` — возвращает `true`, при ошибке выбрасывает исключение.

```csharp
using Rooftop.Crypto;
using System.Text;

using (var alice = CryptoManager.GenerateKeyPair())
using (var bob = CryptoManager.GenerateKeyPair())
{
    byte[] secretA = null, secretB = null, keyA = null, keyB = null;
    try
    {
        secretA = CryptoManager.DeriveSharedSecret(alice, bob.ExportPublicKey());
        secretB = CryptoManager.DeriveSharedSecret(bob, alice.ExportPublicKey());
        keyA = CryptoManager.DeriveAesKey(secretA);
        keyB = CryptoManager.DeriveAesKey(secretB);
        byte[] packet = CryptoManager.EncryptAES(keyA, Encoding.UTF8.GetBytes("Hello"));
        string result = Encoding.UTF8.GetString(CryptoManager.DecryptAES(keyB, packet));
        // result == "Hello"
    }
    finally
    {
        CryptoManager.ClearSecret(secretA); CryptoManager.ClearSecret(secretB);
        CryptoManager.ClearSecret(keyA); CryptoManager.ClearSecret(keyB);
    }
}
```

Публичные ключи — raw X25519, 32 байта. HKDF выдаёт 32 байта; salt и info должны
совпадать у обеих сторон. Формат шифротекста: `nonce[12] || ciphertext || tag[16]`.
Nonce генерируется заново при каждом шифровании. Необязательный `associatedData`
проверяется GCM, но не включён в пакет: получатель должен передать его отдельно.
Ошибка проверки тега вызывает `CryptographicException`, частичный plaintext не возвращается.

Для будущего сетевого протокола отдельно нужны аутентификация публичных ключей,
защита от повтора и разные HKDF info для направлений обмена. Модуль их не реализует.
Очистка собственных массивов выполняется по возможности; копии внутри managed runtime
и библиотеки не гарантированно стираются. Не выводить ключи в лог.

Самотест проверяет обмен Алисы/Боба, `Hello`, пустое и много-блочное сообщения,
неверный ключ/AAD/тег, усечённый пакет, недопустимый X25519-ключ и использование после Dispose.
Известные ответы: [X25519 RFC 7748 §6.1](https://www.rfc-editor.org/rfc/rfc7748#section-6.1),
[HKDF RFC 5869 A.1](https://www.rfc-editor.org/rfc/rfc5869#appendix-A.1), AES-256-GCM.
Проверено на Windows/Mono из Unity 6000.3.10f1. IL2CPP, macOS и другие платформы не проверены.

## Подключение игровых сообщений

В ветке интеграции `feature/vanya-encrypted-transport` обычный запуск через RoofConnection
использует EncryptedTransport. Обмен ключами и подтверждение выполняются до подключения NGO.
Для каждого направления выводится отдельный ключ; транспортная перегрузка EncryptAES
строит nonce из счётчика. Окно принятых номеров отвергает повторы после проверки GCM-тега.

Подробности протокола, ограничения MITM, запуск отрицательного теста и формат PCAP
приведены в [Security.md](../../../../docs/Security.md). Меню самотеста также проверяет
окно повторов, счётчик nonce и подмену AAD.
