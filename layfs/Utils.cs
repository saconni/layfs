using Fsp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VolumeInfo = Fsp.Interop.VolumeInfo;
using FileInfo = Fsp.Interop.FileInfo;
using System.Runtime.InteropServices;
using System.Collections;

namespace layfs
{
    class Utils
    {
        public const int ALLOCATION_UNIT = 4096;

        public class DirectoryEntryComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                return String.Compare(
                    (String)((DictionaryEntry)x).Key,
                    (String)((DictionaryEntry)y).Key);
            }
        }

        public static DirectoryEntryComparer _DirectoryEntryComparer =
            new DirectoryEntryComparer();

        public static void ThrowIoExceptionWithHResult(Int32 HResult)
        {
            throw new IOException(null, HResult);
        }
        public static void ThrowIoExceptionWithWin32(Int32 Error)
        {
            ThrowIoExceptionWithHResult(unchecked((Int32)(0x80070000 | Error)));
        }
        public static void ThrowIoExceptionWithNtStatus(Int32 Status)
        {
            ThrowIoExceptionWithWin32((Int32)FileSystemBase.Win32FromNtStatus(Status));
        }

        public static Int32 GetFileInfoFromFileStream(FileStream stream, out FileInfo fileInfo)
        {
            BY_HANDLE_FILE_INFORMATION Info;
            if (!GetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(),
                out Info))
                ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
            fileInfo.FileAttributes = Info.dwFileAttributes;
            fileInfo.ReparseTag = 0;
            fileInfo.FileSize = (UInt64)stream.Length;
            fileInfo.AllocationSize = (fileInfo.FileSize + ALLOCATION_UNIT - 1) / ALLOCATION_UNIT * ALLOCATION_UNIT;
            fileInfo.CreationTime = Info.ftCreationTime;
            fileInfo.LastAccessTime = Info.ftLastAccessTime;
            fileInfo.LastWriteTime = Info.ftLastWriteTime;
            fileInfo.ChangeTime = fileInfo.LastWriteTime;
            fileInfo.IndexNumber = 0;
            fileInfo.HardLinks = 0;
            return FileSystemBase.STATUS_SUCCESS;
        }

        public static Int32 GetFileInfoFromFileSystemInfo(FileSystemInfo Info, out FileInfo FileInfo)
        {
            FileInfo.FileAttributes = (UInt32)Info.Attributes;
            FileInfo.ReparseTag = 0;
            FileInfo.FileSize = Info is System.IO.FileInfo ?
                (UInt64)((System.IO.FileInfo)Info).Length : 0;
            FileInfo.AllocationSize = (FileInfo.FileSize + ALLOCATION_UNIT - 1)
                / ALLOCATION_UNIT * ALLOCATION_UNIT;
            FileInfo.CreationTime = (UInt64)Info.CreationTimeUtc.ToFileTimeUtc();
            FileInfo.LastAccessTime = (UInt64)Info.LastAccessTimeUtc.ToFileTimeUtc();
            FileInfo.LastWriteTime = (UInt64)Info.LastWriteTimeUtc.ToFileTimeUtc();
            FileInfo.ChangeTime = FileInfo.LastWriteTime;
            FileInfo.IndexNumber = 0;
            FileInfo.HardLinks = 0;
            return FileSystemBase.STATUS_SUCCESS;
        }

        public static Int32 GetFileInfoFromDirectoryInfo(DirectoryInfo dirInfo, out FileInfo fileInfo)
        {
            return GetFileInfoFromFileSystemInfo(dirInfo, out fileInfo);
        }

        public static void SetBasicInfoToFileStream(
            FileStream stream,
            UInt32 fileAttributes,
            UInt64 creationTime,
            UInt64 lastAccessTime,
            UInt64 lastWriteTime)
        {
            if (0 == fileAttributes)
                fileAttributes = (UInt32)System.IO.FileAttributes.Normal;

                FILE_BASIC_INFO Info = default(FILE_BASIC_INFO);
                if (unchecked((UInt32)(-1)) != fileAttributes)
                    Info.FileAttributes = fileAttributes;
                if (0 != creationTime)
                    Info.CreationTime = creationTime;
                if (0 != lastAccessTime)
                    Info.LastAccessTime = lastAccessTime;
                if (0 != lastWriteTime)
                    Info.LastWriteTime = lastWriteTime;
                if (!SetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(),
                    0/*FileBasicInfo*/, ref Info, (UInt32)Marshal.SizeOf(Info)))
                    Utils.ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
        }

        public static void SetBasicInfoToDirectoryInfo(
            DirectoryInfo dirInfo,
            UInt32 fileAttributes,
            UInt64 creationTime,
            UInt64 lastAccessTime,
            UInt64 lastWriteTime)
        {
            if (0 == fileAttributes)
                fileAttributes = (UInt32)System.IO.FileAttributes.Normal;

            if (unchecked((UInt32)(-1)) != fileAttributes)
                dirInfo.Attributes = (System.IO.FileAttributes)fileAttributes;
            if (0 != creationTime)
                dirInfo.CreationTimeUtc = DateTime.FromFileTimeUtc((Int64)creationTime);
            if (0 != lastAccessTime)
                dirInfo.LastAccessTimeUtc = DateTime.FromFileTimeUtc((Int64)lastAccessTime);
            if (0 != lastWriteTime)
                dirInfo.LastWriteTimeUtc = DateTime.FromFileTimeUtc((Int64)lastWriteTime);
        }

        public static void SetFileDisposition(FileStream stream, bool safe)
        {
            FILE_DISPOSITION_INFO Info;
            Info.DeleteFile = true;
            if (!SetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(),
                4/*FileDispositionInfo*/, ref Info, (UInt32)Marshal.SizeOf(Info)))
                if (!safe)
                    ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public UInt32 dwFileAttributes;
            public UInt64 ftCreationTime;
            public UInt64 ftLastAccessTime;
            public UInt64 ftLastWriteTime;
            public UInt32 dwVolumeSerialNumber;
            public UInt32 nFileSizeHigh;
            public UInt32 nFileSizeLow;
            public UInt32 nNumberOfLinks;
            public UInt32 nFileIndexHigh;
            public UInt32 nFileIndexLow;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_BASIC_INFO
        {
            public UInt64 CreationTime;
            public UInt64 LastAccessTime;
            public UInt64 LastWriteTime;
            public UInt64 ChangeTime;
            public UInt32 FileAttributes;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO
        {
            public Boolean DeleteFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean GetFileInformationByHandle(
            IntPtr hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean SetFileInformationByHandle(
            IntPtr hFile,
            Int32 FileInformationClass,
            ref FILE_BASIC_INFO lpFileInformation,
            UInt32 dwBufferSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean SetFileInformationByHandle(
            IntPtr hFile,
            Int32 FileInformationClass,
            ref FILE_DISPOSITION_INFO lpFileInformation,
            UInt32 dwBufferSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern Boolean MoveFileExW(
            [MarshalAs(UnmanagedType.LPWStr)] String lpExistingFileName,
            [MarshalAs(UnmanagedType.LPWStr)] String lpNewFileName,
            UInt32 dwFlags);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern Boolean SetFileSecurityW(
            [MarshalAs(UnmanagedType.LPWStr)] String FileName,
            Int32 SecurityInformation,
            Byte[] SecurityDescriptor);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern Boolean SetKernelObjectSecurity(
            IntPtr Handle,
            Int32 SecurityInformation,
            Byte[] SecurityDescriptor);
    }
}
