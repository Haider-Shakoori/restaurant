using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BusinessOS.Restaurant.Printing;

[SupportedOSPlatform("windows")]
public sealed class WindowsRawPrinter
{
    public void Print(string printerName, string documentName, string text)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new ArgumentException("A Windows printer name is required.", nameof(printerName));
        }

        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open printer '{printerName}'.");
        }

        try
        {
            var info = new DocInfo
            {
                DocumentName = documentName,
                DataType = "RAW",
            };

            if (StartDocPrinter(printerHandle, 1, ref info) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the KOT print document.");
            }

            try
            {
                if (!StartPagePrinter(printerHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the KOT print page.");
                }

                try
                {
                    var payload = BuildEscPosPayload(text);
                    var unmanaged = Marshal.AllocCoTaskMem(payload.Length);

                    try
                    {
                        Marshal.Copy(payload, 0, unmanaged, payload.Length);

                        if (!WritePrinter(printerHandle, unmanaged, payload.Length, out var written) ||
                            written != payload.Length)
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "The thermal printer did not accept the full KOT payload.");
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(unmanaged);
                    }
                }
                finally
                {
                    EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            ClosePrinter(printerHandle);
        }
    }

    internal static byte[] BuildEscPosPayload(string text)
    {
        var content = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var payload = new byte[2 + content.Length + 7];
        var index = 0;

        payload[index++] = 0x1B;
        payload[index++] = 0x40;

        Buffer.BlockCopy(content, 0, payload, index, content.Length);
        index += content.Length;

        payload[index++] = 0x0A;
        payload[index++] = 0x0A;
        payload[index++] = 0x0A;
        payload[index++] = 0x0A;
        payload[index++] = 0x1D;
        payload[index++] = 0x56;
        payload[index] = 0x00;

        return payload;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DocumentName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(
        string printerName,
        out IntPtr printerHandle,
        IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(
        IntPtr printerHandle,
        int level,
        ref DocInfo docInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(
        IntPtr printerHandle,
        IntPtr buffer,
        int count,
        out int written);
}
