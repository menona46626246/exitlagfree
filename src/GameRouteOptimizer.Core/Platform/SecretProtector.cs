using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GameRouteOptimizer.Core.Platform;

/// <summary>Protección de secretos locales (claves privadas WireGuard).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);

    /// <summary>Nombre del mecanismo (para mostrar en UI/logs).</summary>
    string Mechanism { get; }
}

/// <summary>
/// Windows: DPAPI (CryptProtectData, CurrentUser). Linux/macOS: AES-GCM con keyfile
/// local con permisos restrictivos (documentado: garantía menor que DPAPI).
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private readonly string _keyFilePath;

    public SecretProtector(string dataDirectory)
    {
        _keyFilePath = Path.Combine(dataDirectory, "machine.key");
    }

    public string Mechanism => PlatformInfo.IsWindows ? "DPAPI (Windows)" : "AES-GCM (keyfile local)";

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var plain = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = PlatformInfo.IsWindows
            ? DpapiProtect(plain)
            : AesGcmProtect(plain);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
        var data = Convert.FromBase64String(ciphertext);
        var plain = PlatformInfo.IsWindows ? DpapiUnprotect(data) : AesGcmUnprotect(data);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    // ---------- DPAPI (Windows) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptDescr, uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptDescr, uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    private static byte[] DpapiProtect(byte[] plain)
    {
        var pIn = Alloc(plain);
        try
        {
            if (!CryptProtectData(ref pIn, "GameRouteOptimizer", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var pOut))
                throw new InvalidOperationException("DPAPI CryptProtectData falló.");
            return ReadAndFree(pOut);
        }
        finally
        {
            Free(pIn.PbData);
        }
    }

    private static byte[] DpapiUnprotect(byte[] data)
    {
        var pIn = Alloc(data);
        try
        {
            if (!CryptUnprotectData(ref pIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var pOut))
                throw new InvalidOperationException("DPAPI CryptUnprotectData falló.");
            return ReadAndFree(pOut);
        }
        finally
        {
            Free(pIn.PbData);
        }
    }

    private static DataBlob Alloc(byte[] data)
    {
        var ptr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, ptr, data.Length);
        return new DataBlob { CbData = data.Length, PbData = ptr };
    }

    private static byte[] ReadAndFree(DataBlob blob)
    {
        try
        {
            var result = new byte[blob.CbData];
            Marshal.Copy(blob.PbData, result, 0, blob.CbData);
            return result;
        }
        finally
        {
            Free(blob.PbData);
        }
    }

    private static void Free(IntPtr ptr) { if (ptr != IntPtr.Zero) LocalFree(ptr); }

    // ---------- AES-GCM (Linux/macOS fallback) ----------

    private byte[] AesGcmProtect(byte[] plain)
    {
        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return nonce.Concat(cipher).Concat(tag).ToArray();
    }

    private byte[] AesGcmUnprotect(byte[] data)
    {
        var key = GetOrCreateKey();
        var nonce = data[..12];
        var tag = data[^16..];
        var cipher = data[12..^16];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private byte[] GetOrCreateKey()
    {
        if (File.Exists(_keyFilePath))
        {
            var key = File.ReadAllBytes(_keyFilePath);
            if (key.Length == 32) return key;
        }
        var newKey = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
        File.WriteAllBytes(_keyFilePath, newKey);
        try
        {
            if (!PlatformInfo.IsWindows)
                File.SetUnixFileMode(_keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            else
                File.SetAttributes(_keyFilePath, FileAttributes.Hidden);
        }
        catch { /* best effort */ }
        return newKey;
    }
}
