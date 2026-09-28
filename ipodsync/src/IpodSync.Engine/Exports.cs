using System.Runtime.InteropServices;
using System.Text;

namespace IpodSync.Engine;

/// <summary>
/// Native entry points. <c>Java_com_ipodemu_library_IpodEngine_call</c> is the JNI method
/// <c>com.ipodemu.library.IpodEngine.call(byte[]): byte[]</c> (UTF-8 JSON both ways -- bytes, not jstring, because
/// JNI's "modified UTF-8" can't carry emoji in song titles); it calls straight into the JNIEnv function table, so no
/// C shim is needed (indices from jni.h's JNINativeInterface). <c>ipodsync_call</c> / <c>ipodsync_free</c> are the
/// same thing as a plain C API, used to test the NativeAOT build on a desktop.
///
/// Nothing may escape an [UnmanagedCallersOnly] method: an exception there aborts the host process (it did, the first
/// time, and took ipodplayer down with it). So every failure becomes an error reply, built without the serializer in
/// case the serializer is what failed.
/// </summary>
public static unsafe class Exports
{
    const int GetArrayLength = 171, NewByteArray = 176, GetByteArrayRegion = 200, SetByteArrayRegion = 208;

    [UnmanagedCallersOnly(EntryPoint = "Java_com_ipodemu_library_IpodEngine_call")]
    public static IntPtr JniCall(IntPtr env, IntPtr cls, IntPtr request)
    {
        try
        {
            IntPtr* fn = *(IntPtr**)env;
            var getLen = (delegate* unmanaged<IntPtr, IntPtr, int>)fn[GetArrayLength];
            var getRegion = (delegate* unmanaged<IntPtr, IntPtr, int, int, byte*, void>)fn[GetByteArrayRegion];
            var newArr = (delegate* unmanaged<IntPtr, int, IntPtr>)fn[NewByteArray];
            var setRegion = (delegate* unmanaged<IntPtr, IntPtr, int, int, byte*, void>)fn[SetByteArrayRegion];

            int n = getLen(env, request);
            var input = new byte[n];
            fixed (byte* p = input) getRegion(env, request, 0, n, p);
            byte[] output = Encoding.UTF8.GetBytes(SafeHandle(Encoding.UTF8.GetString(input)));
            IntPtr arr = newArr(env, output.Length);
            fixed (byte* p = output) setRegion(env, arr, 0, output.Length, p);
            return arr;
        }
        catch { return IntPtr.Zero; }   // Kotlin side treats null as "engine failed"
    }

    [UnmanagedCallersOnly(EntryPoint = "ipodsync_call")]
    public static byte* CCall(byte* request, int length, int* outLength)
    {
        try
        {
            byte[] output = Encoding.UTF8.GetBytes(SafeHandle(Encoding.UTF8.GetString(request, length)));
            byte* mem = (byte*)NativeMemory.Alloc((nuint)output.Length);
            output.AsSpan().CopyTo(new Span<byte>(mem, output.Length));
            *outLength = output.Length;
            return mem;
        }
        catch { *outLength = 0; return null; }
    }

    [UnmanagedCallersOnly(EntryPoint = "ipodsync_free")]
    public static void CFree(byte* p) => NativeMemory.Free(p);

    static string SafeHandle(string json)
    {
        try { return EngineCore.Handle(json); }
        catch (Exception ex) { return "{\"status\":500,\"body\":{\"error\":\"" + Escape(ex.GetType().Name + ": " + ex.Message) + "\"}}"; }
    }

    static string Escape(string s)
    {
        var b = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            b.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", < ' ' => $"\\u{(int)c:x4}", _ => c.ToString() });
        return b.ToString();
    }
}
