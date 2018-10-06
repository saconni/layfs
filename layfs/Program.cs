using Fsp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace layfs
{

    class LayeredFileSystemService : Service
    {
        private class CommandLineUsageException : Exception
        {
            public CommandLineUsageException(String Message = null) : base(Message)
            {
                HasMessage = null != Message;
            }

            public bool HasMessage;
        }

        private const String PROGNAME = "layfs";

        public LayeredFileSystemService() : base("LayeredFileSystemService")
        {
        }

        protected override void OnStart(String[] Args)
        {
            try
            {
                String debugLogFile = null;
                UInt32 debugFlags = 0;
                String volumePrefix = null;
                String writePath = null;
                String readOnlyPath = null;
                String mountPoint = null;
                IntPtr debugLogHandle = (IntPtr)(-1);
                FileSystemHost host = null;

                int i;

                for (i = 1; Args.Length > i; i++)
                {
                    String Arg = Args[i];
                    if ('-' != Arg[0])
                        break;
                    switch (Arg[1])
                    {
                        case '?':
                            throw new CommandLineUsageException();
                        case 'd':
                            argtol(Args, ref i, ref debugFlags);
                            break;
                        case 'D':
                            argtos(Args, ref i, ref debugLogFile);
                            break;
                        case 'm':
                            argtos(Args, ref i, ref mountPoint);
                            break;
                        case 'r':
                            argtos(Args, ref i, ref readOnlyPath);
                            break;
                        case 'w':
                            argtos(Args, ref i, ref writePath);
                            break;
                        case 'u':
                            argtos(Args, ref i, ref volumePrefix);
                            break;
                        default:
                            throw new CommandLineUsageException();
                    }
                }

                if (Args.Length > i)
                    throw new CommandLineUsageException();

                if (null == readOnlyPath || null == writePath || null == mountPoint)
                    throw new CommandLineUsageException();

                if (null != debugLogFile)
                    if (0 > FileSystemHost.SetDebugLogFile(debugLogFile))
                        throw new CommandLineUsageException("cannot open debug log file");

                host = new FileSystemHost(new LayeredFileSystem(writePath, readOnlyPath));
                host.Prefix = volumePrefix;

                if (0 > host.Mount(mountPoint, null, true, debugFlags))
                    throw new IOException("cannot mount file system");

                mountPoint = host.MountPoint();

                _Host = host;

                Log(EVENTLOG_INFORMATION_TYPE, String.Format("{0}{1}{2} -w {3} -r {5} -m {4}",
                    PROGNAME,
                    null != volumePrefix && 0 < volumePrefix.Length ? " -u " : "",
                        null != volumePrefix && 0 < volumePrefix.Length ? volumePrefix : "",
                    writePath,
                    mountPoint,
                    readOnlyPath));
            }
            catch (CommandLineUsageException ex)
            {
                Log(EVENTLOG_ERROR_TYPE, String.Format(
                    "{0}" +
                    "usage: {1} OPTIONS\n" +
                    "\n" +
                    "options:\n" +
                    "    -d DebugFlags       [-1: enable all debug logs]\n" +
                    "    -D DebugLogFile     [file path; use - for stderr]\n" +
                    "    -u \\Server\\Share    [UNC prefix (single backslash)]\n" +
                    "    -w Directory        [write file system]\n" +
                    "    -r Directory        [read only file system]\n" +
                    "    -m MountPoint       [X:|*|directory]\n",
                    ex.HasMessage ? ex.Message + "\n" : "",
                    PROGNAME));
                throw;
            }
            catch (Exception ex)
            {
                Log(EVENTLOG_ERROR_TYPE, String.Format("{0}", ex.Message));
                throw;
            }
        }
        protected override void OnStop()
        {
            _Host.Unmount();
            _Host = null;
        }

        private static void argtos(String[] Args, ref int I, ref String V)
        {
            if (Args.Length > ++I)
                V = Args[I];
            else
                throw new CommandLineUsageException();
        }
        private static void argtol(String[] Args, ref int I, ref UInt32 V)
        {
            Int32 R;
            if (Args.Length > ++I)
                V = Int32.TryParse(Args[I], out R) ? (UInt32)R : V;
            else
                throw new CommandLineUsageException();
        }

        private FileSystemHost _Host;
    }

    class Program
    {
        static void Main(string[] args)
        {
            Environment.ExitCode = new LayeredFileSystemService().Run();
        }
    }
}
