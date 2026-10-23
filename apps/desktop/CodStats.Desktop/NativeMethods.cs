using System.Runtime.InteropServices;

namespace CodStats.Desktop;

internal static partial class NativeMethods
{
    /// <summary>Moves the process's unused pages out of RAM; Windows pages them back in when needed.</summary>
    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyWorkingSet(IntPtr process);
}
