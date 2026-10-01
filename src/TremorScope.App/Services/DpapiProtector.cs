using System.Security.Cryptography;
using TremorScope.Infrastructure.Settings;

namespace TremorScope.App.Services;

/// <summary>Windows の DPAPI で秘密を守る（この PC の、このユーザーでしか復号できない）</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "TremorScope.Secrets.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) => ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
