using System.Runtime.InteropServices;
using System.Text;

namespace StreamDecky.Helpers;

/// <summary>
/// Wraps the Windows DPAPI so secrets such as the DeepSeek API key are stored
/// scrambled for the current user instead of as readable text in
/// %LOCALAPPDATA%\StreamDecky\app-settings.json.
/// <para>
/// P/Invoke is used directly to avoid taking a NuGet dependency on
/// System.Security.Cryptography.ProtectedData for these few calls.
/// </para>
/// </summary>
public static class DataProtection
{
    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "plain:";
    private const uint CryptprotectUiForbidden = 0x1;

    /// <summary>
    /// Produces a storable representation of <paramref name="value"/>, or fails.
    /// <para>
    /// This deliberately fails closed: if DPAPI cannot protect the value there is no
    /// base64 fallback, because base64 is trivially reversible and the app tells the
    /// user their key is protected. Callers must not persist the secret when this
    /// returns <see langword="false"/>.
    /// </para>
    /// </summary>
    public static bool TryProtect(string? value, out string protectedValue)
    {
        if (string.IsNullOrEmpty(value))
        {
            protectedValue = string.Empty;
            return true;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(value);

        try
        {
            if (TryCryptProtect(plainBytes, out byte[] protectedBytes))
            {
                protectedValue = DpapiPrefix + Convert.ToBase64String(protectedBytes);
                return true;
            }

            AppDiagnostics.Error("DPAPI protection failed. The secret was not stored.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("DPAPI protection failed. The secret was not stored.", ex);
        }
        finally
        {
            Array.Clear(plainBytes);
        }

        protectedValue = string.Empty;
        return false;
    }

    /// <summary>
    /// True when <paramref name="storedValue"/> is already in the DPAPI form. Anything
    /// else — a legacy <c>plain:</c> value or a hand-edited clear-text one — is readable
    /// by anyone with the file and must be re-protected before it is written again.
    /// </summary>
    public static bool IsProtected(string? storedValue)
    {
        return string.IsNullOrWhiteSpace(storedValue)
            || storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal);
    }

    /// <summary>Reverses <see cref="TryProtect"/>; returns an empty string for anything unreadable.</summary>
    public static string Unprotect(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
            return string.Empty;

        try
        {
            if (storedValue.StartsWith(PlainPrefix, StringComparison.Ordinal))
            {
                // Read-only legacy path: earlier builds fell back to base64 when DPAPI
                // failed. Writing this form is no longer possible; see TryProtect.
                AppDiagnostics.Warning(
                    "Read an unprotected stored secret. Re-enter it in Settings so it can be protected.");
                return Encoding.UTF8.GetString(Convert.FromBase64String(storedValue[PlainPrefix.Length..]));
            }

            if (!storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                // Values written before this helper existed, or hand-edited by the user.
                return storedValue;
            }

            byte[] protectedBytes = Convert.FromBase64String(storedValue[DpapiPrefix.Length..]);
            if (TryCryptUnprotect(protectedBytes, out byte[] plainBytes))
                return Encoding.UTF8.GetString(plainBytes);

            AppDiagnostics.Warning(
                "Could not unprotect a stored secret. It was most likely written by a different Windows user account.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Could not unprotect a stored secret.", ex);
        }

        return string.Empty;
    }

    private static bool TryCryptProtect(byte[] plainBytes, out byte[] protectedBytes)
    {
        protectedBytes = Array.Empty<byte>();

        var input = default(DataBlob);
        var output = default(DataBlob);

        try
        {
            input = Allocate(plainBytes);
            if (!CryptProtectData(ref input, "StreamDecky", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref output))
                return false;

            protectedBytes = Read(output);
            return true;
        }
        finally
        {
            Free(ref input);
            Free(ref output);
        }
    }

    private static bool TryCryptUnprotect(byte[] protectedBytes, out byte[] plainBytes)
    {
        plainBytes = Array.Empty<byte>();

        var input = default(DataBlob);
        var output = default(DataBlob);

        try
        {
            input = Allocate(protectedBytes);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref output))
                return false;

            plainBytes = Read(output);
            return true;
        }
        finally
        {
            Free(ref input);
            Free(ref output);
        }
    }

    private static DataBlob Allocate(byte[] data)
    {
        var blob = new DataBlob
        {
            DataSize = data.Length,
            DataPointer = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.DataPointer, data.Length);
        return blob;
    }

    private static byte[] Read(DataBlob blob)
    {
        if (blob.DataPointer == IntPtr.Zero || blob.DataSize <= 0)
            return Array.Empty<byte>();

        byte[] data = new byte[blob.DataSize];
        Marshal.Copy(blob.DataPointer, data, 0, blob.DataSize);
        return data;
    }

    private static void Free(ref DataBlob blob)
    {
        if (blob.DataPointer == IntPtr.Zero)
            return;

        // Zero the buffer before releasing it so the secret does not linger in freed memory.
        for (int i = 0; i < blob.DataSize; i++)
            Marshal.WriteByte(blob.DataPointer, i, 0);

        Marshal.FreeHGlobal(blob.DataPointer);
        blob.DataPointer = IntPtr.Zero;
        blob.DataSize = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataSize;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        ref DataBlob output);
}
