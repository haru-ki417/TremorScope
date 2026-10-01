using System.Security.Cryptography;
using System.Text;

namespace TremorScope.Core.Privacy;

/// <summary>
/// カルテ番号から、クラウドに送るための「仮名 ID」（例: TS-4KQ7M2XD9P）を作る。
///
/// 施設ごとの秘密の鍵で HMAC-SHA256 をとるので、
///   ・同じ施設・同じ患者なら、いつも同じ仮名 ID になる（経過を追える）
///   ・鍵を持たない人（クラウドの管理者など）は、仮名 ID からカルテ番号を割り出せない
///   ・別の施設では別の仮名 ID になる（施設をまたいで照合されない）
/// 鍵は PC の中だけに保存し、クラウドには送らない。
/// </summary>
public sealed class Pseudonymizer
{
    public const string Prefix = "TS-";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 読み間違えやすい I, O, 0, 1 を除いた 32 文字

    private readonly byte[] key;

    public Pseudonymizer(byte[] siteKey)
    {
        ArgumentNullException.ThrowIfNull(siteKey);
        if (siteKey.Length < 16) throw new ArgumentException("施設の鍵は 16 バイト以上にしてください。", nameof(siteKey));
        key = (byte[])siteKey.Clone();
    }

    public static byte[] CreateSiteKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>カルテ番号の前後の空白や全角・半角の違いで別人にならないよう、整えてから計算する</summary>
    public string Pseudonymize(string localId)
    {
        string normalized = Normalize(localId);
        if (normalized.Length == 0) throw new ArgumentException("カルテ番号が空です。", nameof(localId));
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalized));

        // 先頭 50 ビットを 32 文字の記号 10 個に直す（衝突の可能性は 100 万人でも 10 億分の 1 程度）
        var sb = new StringBuilder(Prefix, Prefix.Length + 10);
        ulong bits = BitConverter.ToUInt64(hash, 0);
        for (int i = 0; i < 10; i++)
        {
            sb.Append(Alphabet[(int)(bits & 31)]);
            bits >>= 5;
        }
        return sb.ToString();
    }

    public static string Normalize(string? localId) =>
        (localId ?? "").Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}
