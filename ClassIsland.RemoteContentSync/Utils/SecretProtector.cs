using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassIsland.RemoteContentSync.Utils;

/// <summary>
/// 敏感信息（如 GitHub token）的本地加密存储。
/// Windows 下使用 DPAPI（CryptProtectData，CurrentUser 作用域）：密文绑定当前 Windows 用户与机器，
/// 其他账户或另一台机器都无法解密，彻底避免「明文落盘」。非 Windows 平台降级为 Base64 混淆
/// （带 b64: 前缀，仅隐藏明文，不代表强加密，ClassIsland 教室机均为 Windows 故无影响）。
/// </summary>
internal static class SecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string B64Prefix = "b64:";

    /// <summary>加密明文，返回带前缀的存储字符串（空输入返回空字符串）。</summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectWindows(Encoding.UTF8.GetBytes(plain));
            if (protectedBytes != null)
            {
                return DpapiPrefix + Convert.ToBase64String(protectedBytes);
            }
        }

        return B64Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    }

    /// <summary>解密存储字符串，还原明文；旧版明文直接原样返回（兼容历史配置）。</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        try
        {
            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
                return Encoding.UTF8.GetString(UnprotectWindows(bytes));
            }

            if (stored.StartsWith(B64Prefix, StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[B64Prefix.Length..]));
            }
        }
        catch
        {
            // 解密失败（如换了机器/用户）返回空，调用方会视为「未配置」并要求重新填写
            return "";
        }

        return stored;
    }

    /// <summary>判断存储值是否为加密格式（用于判断是否需要升级旧明文配置）。</summary>
    public static bool IsEncrypted(string? stored) =>
        !string.IsNullOrEmpty(stored) &&
        (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal) || stored.StartsWith(B64Prefix, StringComparison.Ordinal));

    // ---------------- Windows DPAPI P/Invoke ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static byte[]? ProtectWindows(byte[] plain)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        var inputPtr = IntPtr.Zero;
        try
        {
            inputPtr = Marshal.AllocHGlobal(plain.Length);
            Marshal.Copy(plain, 0, inputPtr, plain.Length);
            input.cbData = plain.Length;
            input.pbData = inputPtr;

            if (!CryptProtectData(ref input, "ClassIsland.RemoteContentSync", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0, ref output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (inputPtr != IntPtr.Zero) Marshal.FreeHGlobal(inputPtr);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static byte[] UnprotectWindows(byte[] encrypted)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        var inputPtr = IntPtr.Zero;
        try
        {
            inputPtr = Marshal.AllocHGlobal(encrypted.Length);
            Marshal.Copy(encrypted, 0, inputPtr, encrypted.Length);
            input.cbData = encrypted.Length;
            input.pbData = inputPtr;

            if (!CryptUnprotectData(ref input, "ClassIsland.RemoteContentSync", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0, ref output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (inputPtr != IntPtr.Zero) Marshal.FreeHGlobal(inputPtr);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }
}
