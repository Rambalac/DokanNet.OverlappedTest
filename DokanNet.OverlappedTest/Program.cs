using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DokanNet.OverlappedTest
{
    class Program
    {
        [Flags]
        public enum DesiredAccess : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000
        }
        [Flags]
        public enum ShareMode : uint
        {
            FILE_SHARE_NONE = 0x0,
            FILE_SHARE_READ = 0x1,
            FILE_SHARE_WRITE = 0x2,
            FILE_SHARE_DELETE = 0x4,

        }
        public enum MoveMethod : uint
        {
            FILE_BEGIN = 0,
            FILE_CURRENT = 1,
            FILE_END = 2
        }
        public enum CreationDisposition : uint
        {
            CREATE_NEW = 1,
            CREATE_ALWAYS = 2,
            OPEN_EXISTING = 3,
            OPEN_ALWAYS = 4,
            TRUNCATE_EXSTING = 5
        }
        [Flags]
        public enum FlagsAndAttributes : uint
        {
            FILE_ATTRIBUTES_ARCHIVE = 0x20,
            FILE_ATTRIBUTE_HIDDEN = 0x2,
            FILE_ATTRIBUTE_NORMAL = 0x80,
            FILE_ATTRIBUTE_OFFLINE = 0x1000,
            FILE_ATTRIBUTE_READONLY = 0x1,
            FILE_ATTRIBUTE_SYSTEM = 0x4,
            FILE_ATTRIBUTE_TEMPORARY = 0x100,
            FILE_FLAG_WRITE_THROUGH = 0x80000000,
            FILE_FLAG_OVERLAPPED = 0x40000000,
            FILE_FLAG_NO_BUFFERING = 0x20000000,
            FILE_FLAG_RANDOM_ACCESS = 0x10000000,
            FILE_FLAG_SEQUENTIAL_SCAN = 0x8000000,
            FILE_FLAG_DELETE_ON = 0x4000000,
            FILE_FLAG_POSIX_SEMANTICS = 0x1000000,
            FILE_FLAG_OPEN_REPARSE_POINT = 0x200000,
            FILE_FLAG_OPEN_NO_CALL = 0x100000
        }

        internal delegate void FileIOCompletionRoutine(int dwErrorCode, int dwNumberOfBytesTransfered, ref NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
             string lpFileName,
             DesiredAccess dwDesiredAccess,
             ShareMode dwShareMode,
             IntPtr lpSecurityAttributes,
             CreationDisposition dwCreationDisposition,
             FlagsAndAttributes dwFlagsAndAttributes,
             IntPtr hTemplateFile);

        [DllImport("kernel32", SetLastError = true)]
        internal static extern Int32 CloseHandle(
            SafeFileHandle hObject);

        [DllImport("kernel32", SetLastError = true)]
        internal static extern bool ReadFile(
            SafeFileHandle hFile,
            Byte[] aBuffer,
            UInt32 cbToRead,
            ref UInt32 cbThatWereRead,
            ref NativeOverlapped pOverlapped);

        [DllImport("kernel32", SetLastError = true)]
        internal static extern bool ReadFileEx(
            SafeFileHandle hFile,
            Byte[] aBuffer,
            UInt32 cbToRead,
            ref NativeOverlapped pOverlapped,
            FileIOCompletionRoutine callback);

        static void Write(Stream file, byte val, int count = blockSize)
        {
            file.Write(Enumerable.Repeat(val, count).ToArray(), 0, count);
        }

        public class OverlappedResult
        {
            public byte[] buff;
            public int read;
            public NativeOverlapped overlapped;
            public ManualResetEvent ev = new ManualResetEvent(false);
            public FileIOCompletionRoutine deleg;
        }
        static OverlappedResult Read(SafeFileHandle handle, long offset, int count = blockSize)
        {
            Console.WriteLine(offset);
            var res = new OverlappedResult
            {
                buff = new byte[count]

            };
            var overlapped = new NativeOverlapped();
            overlapped.OffsetLow = (int)(offset & 0xffffffff);
            overlapped.OffsetHigh = (int)(offset >> 32);
            overlapped.EventHandle = IntPtr.Zero;
            res.overlapped = overlapped;
            res.deleg = (int a, int read, ref NativeOverlapped o) =>
             {
                 Console.WriteLine("Done: " + offset);
                 res.ev.Set();
                 res.read = read;
             };
            var b = ReadFileEx(handle, res.buff, (uint)count, ref overlapped, res.deleg);
            if (!b)
            {
                throw new InvalidOperationException(Marshal.GetLastWin32Error().ToString());
            }
            return res;
        }

        public static void AssertRead(string file)
        {
            var handle = CreateFile(file, DesiredAccess.GENERIC_READ, ShareMode.FILE_SHARE_NONE, IntPtr.Zero,
                 CreationDisposition.OPEN_EXISTING, FlagsAndAttributes.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

            var res = new OverlappedResult[] {
                Read(handle, 0 * blockSize),
                Read(handle, 1 * blockSize),
                Read(handle, 2 * blockSize),
                Read(handle, 3 * blockSize),
                Read(handle, 4 * blockSize)
            };

            WaitHandle.WaitAll(res.Select(o => o.ev).ToArray());

            CloseHandle(handle);

            for (int i = 0; i < 5; i++)
                for (int j = 0; j < blockSize; j++)
                {
                    if (res[i].buff[j] != i + 1)
                    {
                        Console.WriteLine("Bad data, should be " + (i + 1) + ", but is " + res[i].buff[j]+ " Block offset: " + j);
                        break;
                    }
                }

        }

//        const int blockSize = 1 << 10; //OK
//        const int blockSize = 1 << 20; //Strange error
        const int blockSize = 1 << 23; //Bad data

        static void Main(string[] args)
        {
            var dokanfile = "N:\\testdata";
            var realFile = "C:\\testdata";
            //Prepare files
            using (var writer = new FileStream(realFile, FileMode.OpenOrCreate, FileAccess.Write))
            {
                Write(writer, 1);
                Write(writer, 2);
                Write(writer, 3);
                Write(writer, 4);
                Write(writer, 5);
            }

            Console.WriteLine("Reading overlapped from real device");
            AssertRead(realFile);

            Console.WriteLine("Reading overlapped from dokan device");
            AssertRead(dokanfile);
        }
    }
}
