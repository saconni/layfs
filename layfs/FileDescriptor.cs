using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using VolumeInfo = Fsp.Interop.VolumeInfo;
using FileInfo = Fsp.Interop.FileInfo;
using System.Security.AccessControl;
using System.Collections;

namespace layfs
{
    class FileDescriptor
    {
        public string FileName;

        public FileStream WriteStream;
        public FileStream ReadOnlyStream;

        public DirectoryInfo WriteDirInfo;
        public DirectoryInfo ReadOnlyDirInfo;

        public DictionaryEntry[] FileSystemInfos;

        public FileDescriptor(string fileName, FileStream stream, bool writeable)
        {
            FileName = fileName;

            if (writeable)
            {
                WriteStream = stream;
            }
            else
            {
                ReadOnlyStream = stream;
            }
        }

        public FileDescriptor(string fileName, DirectoryInfo dirInfo, bool writeable)
        {
            FileName = fileName;

            if (writeable)
            {
                WriteDirInfo = dirInfo;
            }
            else
            {
                ReadOnlyDirInfo = dirInfo;
            }
        }

        public bool IsFile()
        {
            return WriteStream != null 
                || (WriteDirInfo == null && ReadOnlyStream != null);
        }

        public bool IsDirectory()
        {
            return WriteDirInfo != null
                || (WriteStream == null && ReadOnlyDirInfo != null);
        }

        public bool IsWriteable()
        {
            return WriteStream != null || WriteDirInfo != null;
        }

        public bool IsReadOnly()
        {
            return !IsWriteable();
        }

        public void SetFileAttributes(UInt32 FileAttributes)
        {
            SetBasicInfo(FileAttributes, 0, 0, 0);
        }

        public Int32 GetFileInfo(out FileInfo fileInfo)
        {
            if(IsWriteable())
            {
                if (IsFile())
                {
                    return Utils.GetFileInfoFromFileStream(WriteStream, out fileInfo);
                }
                else
                {
                    return Utils.GetFileInfoFromDirectoryInfo(WriteDirInfo, out fileInfo);
                }
            }
            else
            {
                if(IsFile())
                {
                    return Utils.GetFileInfoFromFileStream(ReadOnlyStream, out fileInfo);
                }
                else
                {
                    return Utils.GetFileInfoFromDirectoryInfo(ReadOnlyDirInfo, out fileInfo);
                }
            }
        }

        public void Dispose()
        {
            if (WriteStream != null)
                WriteStream.Dispose();

            if (ReadOnlyStream != null)
                ReadOnlyStream.Dispose();
        }

        public void Flush()
        {
            if (WriteStream != null)
                WriteStream.Flush(true);

            if (ReadOnlyStream != null)
                ReadOnlyStream.Flush(true);
        }

        public void SetBasicInfo(
            UInt32 fileAttributes,
            UInt64 creationTime,
            UInt64 lastAccessTime,
            UInt64 lastWriteTime)
        {
            if (0 == fileAttributes)
                fileAttributes = (UInt32)System.IO.FileAttributes.Normal;

            if(IsWriteable())
            {
                if(IsFile())
                {
                    Utils.SetBasicInfoToFileStream(WriteStream, fileAttributes, creationTime, lastAccessTime, lastWriteTime);
                }
                else
                {
                    Utils.SetBasicInfoToDirectoryInfo(WriteDirInfo, fileAttributes, creationTime, lastAccessTime, lastWriteTime);
                }
            }
            else
            {
                // ??
            }
        }

        public UInt32 GetFileAttributes()
        {
            FileInfo fileInfo;
            GetFileInfo(out fileInfo);
            return fileInfo.FileAttributes;
        }

        public void SetDisposition(bool safe)
        {
            if(IsWriteable())
            {
                if(IsFile())
                {
                    Utils.SetFileDisposition(WriteStream, safe);
                }
                else
                {
                    try
                    {
                        WriteDirInfo.Delete();
                    }
                    catch (Exception ex)
                    {
                        if (!safe)
                            Utils.ThrowIoExceptionWithHResult(ex.HResult);
                    }
                }
            }
        }

        public void Close()
        {
            this.Dispose();
        }

        public FileStream GetReadStream()
        {
            if(IsWriteable())
            {
                return WriteStream;
            }
            else
            {
                return ReadOnlyStream;
            }
        }

        public Byte[] GetSecurityDescriptor()
        {
            if (IsWriteable())
            {
                if(IsFile())
                {
                    return WriteStream.GetAccessControl().GetSecurityDescriptorBinaryForm();
                }
                else
                {
                    return WriteDirInfo.GetAccessControl().GetSecurityDescriptorBinaryForm();
                }
            }
            else
            {
                if (IsFile())
                {
                    return ReadOnlyStream.GetAccessControl().GetSecurityDescriptorBinaryForm();
                }
                else
                {
                    return ReadOnlyDirInfo.GetAccessControl().GetSecurityDescriptorBinaryForm();
                }
            }   
        }

        public void SetSecurityDescriptor(AccessControlSections sections, Byte[] securityDescriptor)
        {
            if(IsWriteable())
            {
                Int32 securityInformation = 0;
                if (0 != (sections & AccessControlSections.Owner))
                    securityInformation |= 1/*OWNER_SECURITY_INFORMATION*/;
                if (0 != (sections & AccessControlSections.Group))
                    securityInformation |= 2/*GROUP_SECURITY_INFORMATION*/;
                if (0 != (sections & AccessControlSections.Access))
                    securityInformation |= 4/*DACL_SECURITY_INFORMATION*/;
                if (0 != (sections & AccessControlSections.Audit))
                    securityInformation |= 8/*SACL_SECURITY_INFORMATION*/;
                if (IsFile())
                {
                    if (!Utils.SetKernelObjectSecurity(WriteStream.SafeFileHandle.DangerousGetHandle(),
                        securityInformation, securityDescriptor))
                        Utils.ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
                else
                {
                    if (!Utils.SetFileSecurityW(WriteDirInfo.FullName,
                        securityInformation, securityDescriptor))
                        Utils.ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
            }

        }
    }
}
