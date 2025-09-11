using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace UniversalUtilities
{
    /// <summary>
    /// 将指定线程绑定到特定的 CPU 组和逻辑处理器。
    /// </summary>
    public static class CpuTopology
    {
        #region PInvoke & Constants

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemCpuSetInformation(
            IntPtr Information,
            uint BufferLength,
            out uint ReturnedLength,
            IntPtr Process,
            uint Flags);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread(); // pseudo-handle

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetThreadGroupAffinity(IntPtr hThread, ref GROUP_AFFINITY GroupAffinity, out GROUP_AFFINITY PreviousAffinity);

        [DllImport("kernel32.dll")]
        static extern void GetCurrentProcessorNumberEx(out PROCESSOR_NUMBER ProcNumber);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        const uint THREAD_SET_INFORMATION = 0x0020;
        const uint THREAD_QUERY_INFORMATION = 0x0040;
        const uint THREAD_SET_LIMITED_INFORMATION = 0x0400; // optional

        [StructLayout(LayoutKind.Sequential)]
        struct GROUP_AFFINITY
        {
            public UIntPtr Mask; // KAFFINITY (ULONG_PTR)
            public ushort Group;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public ushort[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESSOR_NUMBER
        {
            public ushort Group;
            public byte Number;
            public byte Reserved;
        }

        #endregion

        public class CpuSetInfo
        {
            public uint Id;
            public ushort Group;
            public byte LogicalProcessorIndex; // group-relative index
            public byte CoreIndex;
            public byte LastLevelCacheIndex;
            public byte NumaNodeIndex;
            public byte EfficiencyClass;
            public byte Flags; // bitfield
            public ulong AllocationTag;
            public override string ToString()
            {
                return $"CpuSet Id={Id}, Group={Group}, LogicalProcessorIndex={LogicalProcessorIndex}, CoreIndex={CoreIndex}, LastLevelCacheIndex={LastLevelCacheIndex}, NumaNodeIndex={NumaNodeIndex}, EfficiencyClass={EfficiencyClass}, Flags=0x{Flags:X2}, AllocationTag=0x{AllocationTag:X16}";
            }
        }

        public static IReadOnlyList<CpuSetInfo> GetAllCpuSetInfo()
        {
            return QuerySystemCpuSets().AsReadOnly();
        }
        public static uint[] GetPCpuSetIds()
        {
            var list = QuerySystemCpuSets();
            if (list.Count == 0) return Array.Empty<uint>();
            var distinct = list.Select(x => x.EfficiencyClass).Distinct().OrderBy(v => v).ToArray();
            if (distinct.Length == 0) return Array.Empty<uint>();
            byte pClass = distinct.Last();
            return list.Where(x => x.EfficiencyClass == pClass).Select(x => x.Id).ToArray();
        }
        public static uint[] GetECpuSetIds()
        {
            var list = QuerySystemCpuSets();
            if (list.Count == 0) return Array.Empty<uint>();
            var distinct = list.Select(x => x.EfficiencyClass).Distinct().OrderBy(v => v).ToArray();
            if (distinct.Length == 0) return Array.Empty<uint>();
            byte eClass = distinct.First();
            return list.Where(x => x.EfficiencyClass == eClass).Select(x => x.Id).ToArray();
        }
        public static IDisposable PinCurrentThreadToPCores()
        {
            var list = QuerySystemCpuSets();
            if (list.Count == 0) throw new InvalidOperationException("No CPU set information available on this system.");

            var distinct = list.Select(x => x.EfficiencyClass).Distinct().OrderBy(v => v).ToArray();
            if (distinct.Length == 0) throw new InvalidOperationException("Could not determine EfficiencyClass values.");

            byte pClass = distinct.Last();
            var pSets = list.Where(x => x.EfficiencyClass == pClass).ToArray();
            if (pSets.Length == 0) throw new InvalidOperationException("No P-cores detected.");

            var grouped = pSets.GroupBy(x => x.Group).ToDictionary(g => g.Key, g => g.ToArray());
            ushort chosenGroup = grouped.OrderByDescending(kv => kv.Value.Length).First().Key;
            var chosenEntries = grouped[chosenGroup];

            UInt64 mask = 0UL;
            foreach (var e in chosenEntries)
            {
                int bit = e.LogicalProcessorIndex;
                mask |= (1UL << bit);
            }

            if (mask == 0UL) throw new InvalidOperationException("Failed to build affinity mask for chosen group.");
            return new GroupAffinityPin(chosenGroup, mask);
        }
        public static Dictionary<ushort, int[]> GetPCoresGroupedByGroup()
        {
            var list = QuerySystemCpuSets();
            var distinct = list.Select(x => x.EfficiencyClass).Distinct().OrderBy(v => v).ToArray();
            if (distinct.Length == 0) return new Dictionary<ushort, int[]>();
            byte pClass = distinct.Last();
            var pSets = list.Where(x => x.EfficiencyClass == pClass);
            var grouped = pSets.GroupBy(x => x.Group).ToDictionary(g => g.Key, g => g.Select(x => (int)x.LogicalProcessorIndex).OrderBy(i => i).ToArray());
            return grouped;
        }
        /// <summary>
        /// 返回当前 OS 线程 id (GetCurrentThreadId).
        /// </summary>
        public static int GetCurrentOsThreadId() => (int)GetCurrentThreadId();
        /// <summary>
        /// 将当前线程绑定到指定的逻辑处理器 global indices（group*64 + index）。
        /// 使用方式： using (CpuTopology.PinCurrentThreadToLogicalIndices(new[]{...})) { ... }
        /// 注意：所有索引必须位于同一 group，否则抛出异常。
        /// </summary>
        public static IDisposable PinCurrentThreadToLogicalIndices(int[] logicalProcessorGlobalIndices)
        {
            if (logicalProcessorGlobalIndices == null || logicalProcessorGlobalIndices.Length == 0)
                throw new ArgumentException("logicalProcessorGlobalIndices required");

            // convert and verify group consistency
            ushort group = (ushort)(logicalProcessorGlobalIndices[0] / 64);
            UInt64 mask = 0UL;
            foreach (var idx in logicalProcessorGlobalIndices)
            {
                ushort g = (ushort)(idx / 64);
                int bit = idx % 64;
                if (g != group) throw new InvalidOperationException("All provided logical indices must be in the same processor group for SetThreadGroupAffinity.");
                mask |= (1UL << bit);
            }

            // Prepare GROUP_AFFINITY as a variable (can't pass a temporary to ref)
            GROUP_AFFINITY newGa = new GROUP_AFFINITY
            {
                Mask = (UIntPtr)mask,
                Group = group,
                Reserved = new ushort[3]
            };

            // Prevent CLR from migrating managed thread to other native thread
            Thread.BeginThreadAffinity();

            // Call API with ref to variable and get previous affinity via out
            bool ok = SetThreadGroupAffinity(GetCurrentThread(), ref newGa, out GROUP_AFFINITY prev);
            if (!ok)
            {
                // restore CLR state then throw
                Thread.EndThreadAffinity();
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, "SetThreadGroupAffinity failed for current thread.");
            }

            // Return IDisposable that will restore previous affinity and EndThreadAffinity()
            return new CurrentThreadAffinityRestore(prev);
        }
        /// <summary>
        /// 将指定 OS 线程 (native thread id) 绑定到指定逻辑处理器（global indices）。
        /// 返回 IDisposable，Dispose 时恢复并关闭打开的线程句柄。
        /// 要点：所有 logicalProcessorGlobalIndices 必须在同一 group。
        /// </summary>
        public static IDisposable PinThreadByOsIdToLogicalIndices(int osThreadId, int[] logicalProcessorGlobalIndices)
        {
            if (logicalProcessorGlobalIndices == null || logicalProcessorGlobalIndices.Length == 0)
                throw new ArgumentException("logicalProcessorGlobalIndices required");

            ushort group = (ushort)(logicalProcessorGlobalIndices[0] / 64);
            UInt64 mask = 0UL;
            foreach (var idx in logicalProcessorGlobalIndices)
            {
                ushort g = (ushort)(idx / 64);
                int bit = idx % 64;
                if (g != group) throw new InvalidOperationException("All provided logical indices must be in the same processor group for SetThreadGroupAffinity.");
                mask |= (1UL << bit);
            }

            // open a real thread handle
            uint desiredAccess = THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION;
            IntPtr hThread = OpenThread(desiredAccess, false, (uint)osThreadId);
            if (hThread == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, $"OpenThread failed for tid={osThreadId}");
            }

            // Pin using handle; we will close handle in the returned IDisposable
            return PinThreadByHandleToLogicalIndices(hThread, true, logicalProcessorGlobalIndices);
        }
        /// <summary>
        /// 使用指定的线程句柄绑定（若 closeHandleOnDispose 则在 Dispose 时关闭句柄）。
        /// 注意：线程句柄应具备 SetThreadGroupAffinity 所需的权限（THREAD_SET_INFORMATION）。
        /// </summary>
        public static IDisposable PinThreadByHandleToLogicalIndices(IntPtr threadHandle, bool closeHandleOnDispose, int[] logicalProcessorGlobalIndices)
        {
            if (threadHandle == IntPtr.Zero) throw new ArgumentException("threadHandle is null");
            if (logicalProcessorGlobalIndices == null || logicalProcessorGlobalIndices.Length == 0)
                throw new ArgumentException("logicalProcessorGlobalIndices required");

            ushort group = (ushort)(logicalProcessorGlobalIndices[0] / 64);
            UInt64 mask = 0UL;
            foreach (var idx in logicalProcessorGlobalIndices)
            {
                ushort g = (ushort)(idx / 64);
                int bit = idx % 64;
                if (g != group) throw new InvalidOperationException("All provided logical indices must be in the same processor group for SetThreadGroupAffinity.");
                mask |= (1UL << bit);
            }

            GROUP_AFFINITY newGa = new GROUP_AFFINITY { Mask = (UIntPtr)mask, Group = group, Reserved = new ushort[3] };
            // Note: we cannot call Thread.BeginThreadAffinity() on remote thread. Caller should ensure managed thread calls BeginThreadAffinity if it's a managed thread.
            bool ok = SetThreadGroupAffinity(threadHandle, ref newGa, out GROUP_AFFINITY prev);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (closeHandleOnDispose) CloseHandle(threadHandle);
                throw new System.ComponentModel.Win32Exception(err, "SetThreadGroupAffinity failed for provided thread handle.");
            }

            return new ExternalThreadAffinityRestore(threadHandle, prev, closeHandleOnDispose);
        }

        static List<CpuSetInfo> QuerySystemCpuSets()
        {
            uint returnedLen = 0;
            bool ok = GetSystemCpuSetInformation(IntPtr.Zero, 0, out returnedLen, GetCurrentProcess(), 0);
            int last = Marshal.GetLastWin32Error();
            const int ERROR_INSUFFICIENT_BUFFER = 122;

            if (!ok && last != ERROR_INSUFFICIENT_BUFFER)
            {
                return new List<CpuSetInfo>();
            }

            if (returnedLen == 0) return new List<CpuSetInfo>();

            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal((int)returnedLen);
                if (!GetSystemCpuSetInformation(buffer, returnedLen, out returnedLen, GetCurrentProcess(), 0))
                {
                    return new List<CpuSetInfo>();
                }

                var list = new List<CpuSetInfo>();
                int offset = 0;
                int total = (int)returnedLen;
                while (offset < total)
                {
                    uint entrySize = (uint)Marshal.ReadInt32(buffer, offset);
                    uint entryType = (uint)Marshal.ReadInt32(buffer, offset + 4);
                    const uint CpuSetInformation = 0;

                    if (entryType == CpuSetInformation)
                    {
                        int baseOffset = offset + 8;
                        uint id = (uint)Marshal.ReadInt32(buffer, baseOffset + 0);
                        ushort group = (ushort)Marshal.ReadInt16(buffer, baseOffset + 4);
                        byte logicalProcessorIndex = Marshal.ReadByte(buffer, baseOffset + 6);
                        byte coreIndex = Marshal.ReadByte(buffer, baseOffset + 7);
                        byte lastLevelCacheIndex = Marshal.ReadByte(buffer, baseOffset + 8);
                        byte numaNodeIndex = Marshal.ReadByte(buffer, baseOffset + 9);
                        byte efficiencyClass = Marshal.ReadByte(buffer, baseOffset + 10);
                        byte flags = Marshal.ReadByte(buffer, baseOffset + 11);
                        ulong allocTag = (ulong)Marshal.ReadInt64(buffer, baseOffset + 16);

                        list.Add(new CpuSetInfo
                        {
                            Id = id,
                            Group = group,
                            LogicalProcessorIndex = logicalProcessorIndex,
                            CoreIndex = coreIndex,
                            LastLevelCacheIndex = lastLevelCacheIndex,
                            NumaNodeIndex = numaNodeIndex,
                            EfficiencyClass = efficiencyClass,
                            Flags = flags,
                            AllocationTag = allocTag
                        });
                    }

                    if (entrySize == 0) break;
                    offset += (int)entrySize;
                }

                return list;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        // IDisposable used when we pinned current thread (we called BeginThreadAffinity)
        sealed class CurrentThreadAffinityRestore : IDisposable
        {
            GROUP_AFFINITY _prev;
            bool _done = false;
            public CurrentThreadAffinityRestore(GROUP_AFFINITY prev) { _prev = prev; }
            public void Dispose()
            {
                if (!_done)
                {
                    SetThreadGroupAffinity(GetCurrentThread(), ref _prev, out _);
                    try { Thread.EndThreadAffinity(); } catch { }
                    _done = true;
                }
            }
        }
        // IDisposable for external thread handle (we may close handle when done)
        sealed class ExternalThreadAffinityRestore : IDisposable
        {
            readonly IntPtr _hThread;
            GROUP_AFFINITY _prev;
            readonly bool _closeHandle;
            bool _done = false;

            public ExternalThreadAffinityRestore(IntPtr hThread, GROUP_AFFINITY prev, bool closeHandle)
            {
                _hThread = hThread;
                _prev = prev;
                _closeHandle = closeHandle;
            }

            public void Dispose()
            {
                if (!_done)
                {
                    // attempt to restore previous affinity
                    SetThreadGroupAffinity(_hThread, ref _prev, out _);
                    if (_closeHandle)
                    {
                        try { CloseHandle(_hThread); } catch { }
                    }
                    _done = true;
                }
            }
        }
        // The original GroupAffinityPin used earlier
        sealed class GroupAffinityPin : IDisposable
        {
            GROUP_AFFINITY _previous;
            bool _disposed = false;

            public GroupAffinityPin(ushort group, UInt64 mask)
            {
                GROUP_AFFINITY newGa = new GROUP_AFFINITY
                {
                    Mask = (UIntPtr)mask,
                    Group = group,
                    Reserved = new ushort[3]
                };

                Thread.BeginThreadAffinity();

                bool ok = SetThreadGroupAffinity(GetCurrentThread(), ref newGa, out GROUP_AFFINITY prev);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    Thread.EndThreadAffinity();
                    throw new System.ComponentModel.Win32Exception(err, "SetThreadGroupAffinity failed");
                }

                _previous = prev;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    SetThreadGroupAffinity(GetCurrentThread(), ref _previous, out _);
                    try { Thread.EndThreadAffinity(); } catch { }
                    _disposed = true;
                }
            }
        }
        public static (ushort group, byte number) SampleCurrentProcessor()
        {
            GetCurrentProcessorNumberEx(out PROCESSOR_NUMBER pn);
            return (pn.Group, pn.Number);
        }
    }
}
