using System.Security.Cryptography;
using Serilog;

namespace SPTInstaller.Helpers;

public static class FileHashHelper
{
    public static bool CheckHash(FileInfo file, HashAlgorithmName algorithm, byte[] expectedHash)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        using var sourceStream = file.OpenRead();
        
        var buffer = new byte[1024 * 1024];
        int read;
        
        while ((read = sourceStream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        
        var sourceHash = hash.GetHashAndReset();
        
        Log.Information($"Comparing {algorithm.Name} Hashes :: S: {Convert.ToHexStringLower(sourceHash)} - E: {Convert.ToHexStringLower(expectedHash)}");
        
        return sourceHash.AsSpan().SequenceEqual(expectedHash);
    }
}