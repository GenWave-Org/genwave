using System.Runtime.InteropServices;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The device a path lives on, via the Linux <c>statx(2)</c> syscall directly (SPEC F154.4's own
/// cross-device rider; T380 review B4) — no shelling out. <c>statx</c>, not the legacy <c>stat</c>,
/// because <c>struct statx</c> is a FIXED, kernel-guaranteed 256-byte wire ABI, identical on every
/// architecture this codebase ships (amd64, arm64); the legacy <c>struct stat</c> layout varies by
/// architecture and would need per-arch field offsets to marshal correctly.
///
/// <para>
/// <see cref="TryGetDeviceId"/> is the ONLY public surface — <see cref="FileSystemProbe"/>'s own
/// <c>OperatingSystem.IsLinux()</c> guard is what makes calling this off Linux unreachable in
/// practice; this class carries no guard of its own beyond letting a missing <c>libc</c> symbol
/// (impossible on Linux, but defensive) fail closed rather than throw.
/// </para>
/// </summary>
static partial class LinuxDeviceId
{
    // AT_FDCWD (fcntl.h) — ignored by the kernel whenever pathname is itself absolute (every path
    // this codebase ever passes here, per the jail's own canonicalisation), so the exact value only
    // matters for documentation/convention.
    const int AtFdCwd = -100;

    // STATX_BASIC_STATS (linux/stat.h) — the conventional "give me everything stat(2) gave" mask;
    // stx_dev_major/stx_dev_minor are unconditionally filled in by the kernel regardless of the mask
    // (man statx), so this is a convention, not a requirement, for THIS class's own purpose.
    const uint StatxBasicStats = 0x000007ff;

    /// <summary><c>struct statx_timestamp</c> (<c>man statx</c>) — 16 bytes, fixed ABI.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct StatxTimestamp
    {
        public long TvSec;
        public uint TvNsec;
        public int Reserved;
    }

    /// <summary>
    /// <c>struct statx</c> (<c>man statx</c>), fields up to <c>stx_dev_minor</c> only — but the
    /// KERNEL always writes a full 256-byte buffer regardless of the requested mask or kernel
    /// version (the struct's own size is a fixed part of the syscall ABI), so
    /// <see cref="StructLayoutAttribute.Size"/> pins the TOTAL size at 256 even though the trailing
    /// <c>stx_mnt_id</c>/<c>stx_dio_*</c>/spare fields are never read here — a smaller declared size
    /// would let the kernel's write overrun this struct's own backing memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    struct StatxBuffer
    {
        public uint Mask;
        public uint Blksize;
        public ulong Attributes;
        public uint Nlink;
        public uint Uid;
        public uint Gid;
        public ushort Mode;
        public ushort Spare0;
        public ulong Ino;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public StatxTimestamp Atime;
        public StatxTimestamp Btime;
        public StatxTimestamp Ctime;
        public StatxTimestamp Mtime;
        public uint RdevMajor;
        public uint RdevMinor;
        public uint DevMajor;
        public uint DevMinor;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int dirfd, string pathname, int flags, uint mask, out StatxBuffer statxBuf);

    /// <summary>
    /// The device <paramref name="path"/> lives on, as an identity stable enough to EQUALITY-compare
    /// against another call's result — <c>(stx_dev_major, stx_dev_minor)</c> packed into one
    /// <see cref="ulong"/>, never a real kernel <c>dev_t</c> encoding a caller should interpret.
    /// <see langword="false"/> on any failure (missing path, permission denied, an unavailable
    /// syscall) — the caller (<see cref="FileSystemProbe"/>) treats that as "cannot vouch for this,"
    /// never a crash.
    /// </summary>
    internal static bool TryGetDeviceId(string path, out ulong deviceId)
    {
        deviceId = 0;
        try
        {
            if (Statx(AtFdCwd, path, 0, StatxBasicStats, out var buf) != 0)
                return false;

            deviceId = ((ulong)buf.DevMajor << 32) | buf.DevMinor;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
