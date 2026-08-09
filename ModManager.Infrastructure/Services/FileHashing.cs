using System.Security.Cryptography;

namespace ModManager.Infrastructure.Services;

internal static class FileHashing
{
    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
