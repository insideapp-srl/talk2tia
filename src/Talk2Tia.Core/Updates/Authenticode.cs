using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Talk2Tia.Core.Updates;

/// <summary>
/// Verifies that a file carries a valid Authenticode signature (WinVerifyTrust: intact file, trusted chain) made with
/// an InsideApp code-signing certificate. Pinning the publisher instead of a thumbprint keeps updates working when the
/// certificate is renewed.
/// </summary>
internal static class Authenticode
{
    private const string Publisher = "INSIDEAPP";

    public static bool IsSignedByInsideApp(string path, out string detail)
    {
        var status = Verify(path);
        if (status != 0)
        {
            detail = $"invalid signature (WinVerifyTrust 0x{status:X8})";
            return false;
        }

        try
        {
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            var organization = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? string.Empty;
            if (!organization.StartsWith(Publisher, StringComparison.OrdinalIgnoreCase))
            {
                detail = $"signed by '{signer.Subject}', not by InsideApp";
                return false;
            }

            detail = signer.Subject;
            return true;
        }
        catch (CryptographicException ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static uint Verify(string path)
    {
        var fileInfo = new WinTrustFileInfo { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, fDeleteOld: false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UIChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE: offline PCs must still verify; the chain is checked
                UnionChoice = 1, // WTD_CHOICE_FILE
                File = filePtr,
                StateAction = 0, // WTD_STATEACTION_IGNORE
                ProvFlags = 0x00000010, // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UrlReference;
        public uint ProvFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [In] ref Guid action, [In] ref WinTrustData data);
}
