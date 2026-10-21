using System.Runtime.InteropServices;

namespace EasyTry.Conversion.Writers;

internal static class NativeE57Methods
{
    internal const string LibraryName = "EasyTryE57Native";
    internal const uint RequiredFieldMask = 0x7F;

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr easytry_e57_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileGuid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string scanGuid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string scanName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string description,
        uint scanRows,
        uint scanColumns,
        nuint bufferCapacity);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int easytry_e57_write(
        IntPtr handle,
        double[] x,
        double[] y,
        double[] z,
        float[] processedIntensity,
        double[] rawIntensity,
        uint[] rowIndex,
        uint[] columnIndex,
        nuint pointCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int easytry_e57_complete(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void easytry_e57_cancel(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void easytry_e57_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int easytry_e57_validate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        int readAllPoints,
        out ulong pointCount,
        out uint fieldMask);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr easytry_e57_last_error();

    internal static string GetLastError()
    {
        var pointer = easytry_e57_last_error();
        return pointer == IntPtr.Zero
            ? "原生 E57 写入器未返回错误详情。"
            : Marshal.PtrToStringUTF8(pointer) ?? "原生 E57 写入器返回了空错误信息。";
    }
}
