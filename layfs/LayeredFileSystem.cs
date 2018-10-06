using Fsp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VolumeInfo = Fsp.Interop.VolumeInfo;
using FileInfo = Fsp.Interop.FileInfo;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Collections;

namespace layfs
{
    class LayeredFileSystem : FileSystemBase
    {
        private string _writePath;
        private string _readOnlyPath;

        private static string NormalizePath(string path)
        {
            path = Path.GetFullPath(path);
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);

            return path;
        }

        private static bool FileOrDirectoryExists(string path)
        {
            return (Directory.Exists(path) || File.Exists(path));
        }

        private string GetWritePath(string fileName)
        {
            return _writePath + fileName;
        }

        private string GetReadOnlyPath(string fileName)
        {
            return _readOnlyPath + fileName;
        }

        public LayeredFileSystem(string writePath, string readOnlyPah)
        {
            _writePath = NormalizePath(writePath);
            _readOnlyPath = NormalizePath(readOnlyPah);
        }

        public override Int32 ExceptionHandler(Exception ex)
        {
            Int32 HResult = ex.HResult; /* needs Framework 4.5 */
            if (0x80070000 == (HResult & 0xFFFF0000))
                return NtStatusFromWin32((UInt32)HResult & 0xFFFF);
            return STATUS_UNEXPECTED_IO_ERROR;
        }

        public override Int32 Init(Object Host0)
        {
            FileSystemHost Host = (FileSystemHost)Host0;
            Host.SectorSize = Utils.ALLOCATION_UNIT;
            Host.SectorsPerAllocationUnit = 1;
            Host.MaxComponentLength = 255;
            Host.FileInfoTimeout = 1000;
            Host.CaseSensitiveSearch = false;
            Host.CasePreservedNames = true;
            Host.UnicodeOnDisk = true;
            Host.PersistentAcls = true;
            Host.PostCleanupWhenModifiedOnly = true;
            Host.PassQueryDirectoryPattern = true;
            Host.VolumeCreationTime = (UInt64)File.GetCreationTimeUtc(_writePath).ToFileTimeUtc();
            Host.VolumeSerialNumber = 0;
            return STATUS_SUCCESS;
        }

        public override Int32 GetVolumeInfo(
            out VolumeInfo VolumeInfo)
        {
            VolumeInfo = default(VolumeInfo);
            try
            {
                DriveInfo Info = new DriveInfo(_writePath);
                VolumeInfo.TotalSize = (UInt64)Info.TotalSize;
                VolumeInfo.FreeSize = (UInt64)Info.TotalFreeSpace;
            }
            catch (ArgumentException)
            {
                /*
                 * DriveInfo only supports drives and does not support UNC paths.
                 * It would be better to use GetDiskFreeSpaceEx here.
                 */
            }
            return STATUS_SUCCESS;
        }

        public override Int32 GetSecurityByName(
            String fileName,
            out UInt32 fileAttributes /* or ReparsePointIndex */,
            ref Byte[] securityDescriptor)
        {
            string path = GetWritePath(fileName);

            if(!FileOrDirectoryExists(path))
            {
                path = GetReadOnlyPath(fileName);
            }

            System.IO.FileInfo info = new System.IO.FileInfo(path);
            fileAttributes = (UInt32)info.Attributes;
            if (null != securityDescriptor)
            {
                if (FileOrDirectoryExists(path))
                {
                    securityDescriptor = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
                }
            }
                

            return STATUS_SUCCESS;
        }

        public override Int32 Create(
            String fileName,
            UInt32 createOptions,
            UInt32 grantedAccess,
            UInt32 fileAttributes,
            Byte[] securityDescriptor,
            UInt64 allocationSize,
            out Object fileNode,
            out Object fileDesc0,
            out FileInfo fileInfo,
            out String normalizedName)
        {
            fileNode = default(Object);
            normalizedName = default(String);

            FileDescriptor fileDesc = null;
            try
            {
                var path = GetWritePath(fileName);

                // directory or file?
                if (0 == (createOptions & FILE_DIRECTORY_FILE))
                {
                    // file

                    FileSecurity Security = null;

                    if (null != securityDescriptor)
                    {
                        Security = new FileSecurity();
                        Security.SetSecurityDescriptorBinaryForm(securityDescriptor);
                    }

                    fileDesc = new FileDescriptor(fileName,
                        new FileStream(
                            path,
                            FileMode.CreateNew,
                            (FileSystemRights)grantedAccess | FileSystemRights.WriteAttributes,
                            FileShare.Read | FileShare.Write | FileShare.Delete,
                            4096,
                            0,
                            Security), true);

                    fileDesc.SetFileAttributes(fileAttributes | (UInt32)System.IO.FileAttributes.Archive);
                }
                else
                {
                    // directory

                    if (Directory.Exists(path))
                        Utils.ThrowIoExceptionWithNtStatus(STATUS_OBJECT_NAME_COLLISION);

                    DirectorySecurity Security = null;

                    if (null != securityDescriptor)
                    {
                        Security = new DirectorySecurity();
                        Security.SetSecurityDescriptorBinaryForm(securityDescriptor);
                    }

                    fileDesc = new FileDescriptor(fileName, Directory.CreateDirectory(path, Security), true);

                    fileDesc.SetFileAttributes(fileAttributes);
                }

                // assign the file descriptor
                fileDesc0 = fileDesc;
                return fileDesc.GetFileInfo(out fileInfo);
            }
            catch 
            {
                if (null != fileDesc)
                    fileDesc.Dispose();

                throw;
            }
        }

        public override Int32 Open(
            String fileName,
            UInt32 createOptions,
            UInt32 grantedAccess,
            out Object fileNode,
            out Object fileDesc0,
            out FileInfo fileInfo,
            out String normalizedName)
        {
            fileNode = default(Object);
            normalizedName = default(String);

            FileDescriptor fileDesc = null;

            try
            {
                string path = GetWritePath(fileName);
                
                if(Directory.Exists(path))
                {
                    fileDesc = new FileDescriptor(fileName, new DirectoryInfo(path), true);

                    path = GetReadOnlyPath(fileName);
                    if(Directory.Exists(path))
                    {
                        fileDesc.ReadOnlyDirInfo = new DirectoryInfo(path);
                    }
                }
                else if(File.Exists(path))
                {
                    fileDesc = new FileDescriptor(fileName, 
                        new FileStream(
                            path,
                            FileMode.Open,
                            (FileSystemRights)grantedAccess,
                            FileShare.Read | FileShare.Write | FileShare.Delete,
                            4096,
                            0), true);
                }
                else
                {
                    path = GetReadOnlyPath(fileName);
                    if (Directory.Exists(path))
                    {
                        fileDesc = new FileDescriptor(fileName, new DirectoryInfo(path), false);
                    }
                    else if (File.Exists(path))
                    {
                        fileDesc = new FileDescriptor(fileName, 
                            new FileStream(
                                path,
                                FileMode.Open,
                                (FileSystemRights)grantedAccess,
                                FileShare.Read | FileShare.Write | FileShare.Delete,
                                4096,
                                0), false);
                    }
                    else
                    {
                        throw new FileNotFoundException();
                    }
                }

                fileDesc0 = fileDesc;
                return fileDesc.GetFileInfo(out fileInfo);
            }
            catch
            {
                if (fileDesc != null)
                    fileDesc.Dispose();

                throw;
            }
        }

        public void MakeWriteable(FileDescriptor fileDesc)
        {
            if(!fileDesc.IsWriteable())
            {
                var path = Path.GetDirectoryName(GetWritePath(fileDesc.FileName));
                if(!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                path = GetWritePath(fileDesc.FileName);

                File.Copy(GetReadOnlyPath(fileDesc.FileName), path, true);

                fileDesc.WriteStream = new FileStream(
                    path, 
                    FileMode.Open, 
                    FileAccess.ReadWrite, 
                    FileShare.Read | FileShare.Write | FileShare.Delete, 
                    4096, 0);
            }
        }

        public override Int32 Overwrite(
            Object fileNode,
            Object fileDesc0,
            UInt32 fileAttributes,
            Boolean replaceFileAttributes,
            UInt64 allocationSize,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;

            MakeWriteable(fileDesc);

            if (replaceFileAttributes)
                fileDesc.SetFileAttributes(fileAttributes | (UInt32)System.IO.FileAttributes.Archive);
            else if (0 != fileAttributes)
                fileDesc.SetFileAttributes(fileDesc.GetFileAttributes() | fileAttributes | (UInt32)System.IO.FileAttributes.Archive);

            fileDesc.WriteStream.SetLength(0);

            return fileDesc.GetFileInfo(out fileInfo);
        }

        public override void Cleanup(
            Object fileNode,
            Object fileDesc0,
            String fileName,
            UInt32 flags)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            if (0 != (flags & CleanupDelete))
            {
                fileDesc.SetDisposition(true);
                fileDesc.Dispose();
            }
        }

        public override void Close(
            Object fileNode,
            Object fileDesc0)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            fileDesc.Close();
        }

        public override Int32 Read(
            Object fileNode,
            Object fileDesc0,
            IntPtr buffer,
            UInt64 offset,
            UInt32 length,
            out UInt32 PBytesTransferred)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;

            FileStream stream = fileDesc.GetReadStream();

            if (offset >= (UInt64)stream.Length)
                Utils.ThrowIoExceptionWithNtStatus(STATUS_END_OF_FILE);

            Byte[] Bytes = new byte[length];

            stream.Seek((Int64)offset, SeekOrigin.Begin);

            PBytesTransferred = (UInt32)stream.Read(Bytes, 0, Bytes.Length);
            Marshal.Copy(Bytes, 0, buffer, Bytes.Length);
            return STATUS_SUCCESS;
        }

        public override Int32 Write(
            Object fileNode,
            Object fileDesc0,
            IntPtr buffer,
            UInt64 offset,
            UInt32 length,
            Boolean writeToEndOfFile,
            Boolean constrainedIo,
            out UInt32 pBytesTransferred,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;

            MakeWriteable(fileDesc);

            FileStream stream = fileDesc.WriteStream;

            if (constrainedIo)
            {
                if (offset >= (UInt64)stream.Length)
                {
                    pBytesTransferred = default(UInt32);
                    fileInfo = default(FileInfo);
                    return STATUS_SUCCESS;
                }
                if (offset + length > (UInt64)stream.Length)
                    length = (UInt32)((UInt64)stream.Length - offset);
            }
            Byte[] bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            if (!writeToEndOfFile)
                stream.Seek((Int64)offset, SeekOrigin.Begin);

            stream.Write(bytes, 0, bytes.Length);
            pBytesTransferred = (UInt32)bytes.Length;

            return fileDesc.GetFileInfo(out fileInfo);

        }

        public override Int32 Flush(
            Object fileNode,
            Object fileDesc0,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            if (null == fileDesc)
            {
                /* we do not flush the whole volume, so just return SUCCESS */
                fileInfo = default(FileInfo);
                return STATUS_SUCCESS;
            }

            fileDesc.Flush();

            return fileDesc.GetFileInfo(out fileInfo);
        }

        public override Int32 GetFileInfo(
            Object fileNode,
            Object fileDesc0,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            return fileDesc.GetFileInfo(out fileInfo);
        }

        public override Int32 SetBasicInfo(
            Object fileNode,
            Object fileDesc0,
            UInt32 fileAttributes,
            UInt64 creationTime,
            UInt64 lastAccessTime,
            UInt64 lastWriteTime,
            UInt64 changeTime,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            MakeWriteable(fileDesc);
            fileDesc.SetBasicInfo(fileAttributes, creationTime, lastAccessTime, lastWriteTime);

            return fileDesc.GetFileInfo(out fileInfo);
        }

        public override Int32 SetFileSize(
            Object fileNode,
            Object fileDesc0,
            UInt64 newSize,
            Boolean setAllocationSize,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            MakeWriteable(fileDesc);

            if (!setAllocationSize || (UInt64)fileDesc.WriteStream.Length > newSize)
            {
                /*
                 * "FileInfo.FileSize > NewSize" explanation:
                 * Ptfs does not support allocation size. However if the new AllocationSize
                 * is less than the current FileSize we must truncate the file.
                 */
                fileDesc.WriteStream.SetLength((Int64)newSize);
            }
            return fileDesc.GetFileInfo(out fileInfo);
        }

        public override Int32 CanDelete(
            Object fileNode,
            Object fileDesc0,
            String fileName)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;

            if(fileDesc.IsWriteable())
            {
                fileDesc.SetDisposition(false);
                return STATUS_SUCCESS;
            }
            else
            {
                return STATUS_ACCESS_DENIED;
            }
        }

        public override Int32 Rename(
            Object fileNode,
            Object fileDesc0,
            String fileName,
            String newFileName,
            Boolean replaceIfExists)
        {
            var path = GetWritePath(fileName);

            if (FileOrDirectoryExists(path))
            {
                if (!Utils.MoveFileExW(path, GetWritePath(newFileName), replaceIfExists ? 1U/*MOVEFILE_REPLACE_EXISTING*/ : 0))
                    Utils.ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                return STATUS_SUCCESS;
            }
            else
            {
                return STATUS_ACCESS_DENIED;
            }
        }

        public override Int32 GetSecurity(
            Object fileNode,
            Object fileDesc0,
            ref Byte[] securityDescriptor)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            securityDescriptor = fileDesc.GetSecurityDescriptor();
            return STATUS_SUCCESS;
        }

        public override Int32 SetSecurity(
            Object fileNode,
            Object fileDesc0,
            AccessControlSections sections,
            Byte[] securityDescriptor)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;

            MakeWriteable(fileDesc);

            fileDesc.SetSecurityDescriptor(sections, securityDescriptor);

            return STATUS_SUCCESS;
        }

        public override Boolean ReadDirectoryEntry(
            Object fileNode,
            Object fileDesc0,
            String pattern,
            String marker,
            ref Object context,
            out String fileName,
            out FileInfo fileInfo)
        {
            FileDescriptor fileDesc = (FileDescriptor)fileDesc0;
            if (null == fileDesc.FileSystemInfos)
            {
                if (null != pattern)
                    pattern = pattern.Replace('<', '*').Replace('>', '?').Replace('"', '.');
                else
                    pattern = "*";

                SortedList list = new SortedList();

                if (fileDesc.IsWriteable())
                {
                    if (null != fileDesc.WriteDirInfo && null != fileDesc.WriteDirInfo.Parent)
                    {
                        list.Add(".", fileDesc.WriteDirInfo);
                        list.Add("..", fileDesc.WriteDirInfo.Parent);
                    }
                }
                else
                {
                    if (null != fileDesc.ReadOnlyDirInfo && null != fileDesc.ReadOnlyDirInfo.Parent)
                    {
                        list.Add(".", fileDesc.ReadOnlyDirInfo);
                        list.Add("..", fileDesc.ReadOnlyDirInfo.Parent);
                    }
                }

                if(fileDesc.WriteDirInfo != null)
                {
                    IEnumerable e = fileDesc.WriteDirInfo.EnumerateFileSystemInfos(pattern);

                    foreach (FileSystemInfo info in e)
                        list.Add(info.Name, info);
                }

                if (fileDesc.ReadOnlyDirInfo != null)
                {
                    IEnumerable e = fileDesc.ReadOnlyDirInfo.EnumerateFileSystemInfos(pattern);

                    foreach (FileSystemInfo info in e)
                    {
                        if(!list.ContainsKey(info.Name))
                        {
                            list.Add(info.Name, info);
                        }
                    }
                }

                fileDesc.FileSystemInfos = new DictionaryEntry[list.Count];

                list.CopyTo(fileDesc.FileSystemInfos, 0);
            }
            int index;
            if (null == context)
            {
                index = 0;
                if (null != marker)
                {
                    index = Array.BinarySearch(fileDesc.FileSystemInfos,
                        new DictionaryEntry(marker, null),
                        Utils._DirectoryEntryComparer);
                    if (0 <= index)
                        index++;
                    else
                        index = ~index;
                }
            }
            else
                index = (int)context;
            if (fileDesc.FileSystemInfos.Length > index)
            {
                context = index + 1;
                fileName = (String)fileDesc.FileSystemInfos[index].Key;
                Utils.GetFileInfoFromFileSystemInfo(
                    (FileSystemInfo)fileDesc.FileSystemInfos[index].Value,
                    out fileInfo);
                return true;
            }
            else
            {
                fileName = default(String);
                fileInfo = default(FileInfo);
                return false;
            }
        }
    }
}
