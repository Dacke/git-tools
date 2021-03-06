﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Configuration;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GitScc
{
    public abstract class GitBash
    {
        public static bool UseUTF8FileNames { get; set; }

        private static string gitExePath;
        
        public static string GitExePath
        {
            get { return gitExePath; }
            set
            {
                try
                {
                    gitExePath = value == null ? null : Path.Combine(Path.GetDirectoryName(value), "git.exe");
                }
                catch{}
            }
        }

        public static bool Exists { get { return !string.IsNullOrWhiteSpace(gitExePath) &&
            File.Exists(gitExePath); }
        }

        public static async Task<GitBashResult> RunAsync(string args, string workingDirectory)
        {
            GitBashResult ret = null;
            await Task.Run(new Action(() => {
                ret = Run(args, workingDirectory);
            }));
            return ret;
        }

        public static GitBashResult Run(string args, string workingDirectory = null)
        {
            Debug.WriteLine(string.Format("{2}>{0} {1}", "git", args, Thread.CurrentThread.ManagedThreadId));
            
            if (string.IsNullOrWhiteSpace(gitExePath) || !File.Exists(gitExePath))
                throw new GitException("Git Executable not found");

            if (workingDirectory!=null && !Directory.Exists(workingDirectory))
            {
                return new GitBashResult
                    {
                        HasError = true,
                        Error = workingDirectory + " is not a valid folder to run git command " + args
                    };
            }

            GitBashResult result = new GitBashResult();

            var pinfo = new ProcessStartInfo(gitExePath)
            {
                Arguments = args,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            };

            if (UseUTF8FileNames)
            {
                pinfo.StandardOutputEncoding = Encoding.UTF8;
                pinfo.StandardErrorEncoding = Encoding.UTF8;
            }

            using (var process = Process.Start(pinfo))
            {
                var output = "";
                Thread thread = new Thread(_ => output = process.StandardOutput.ReadToEnd());
                thread.Start();
                var error = process.StandardError.ReadToEnd();
                thread.Join();

                process.WaitForExit();

                result.HasError = process.ExitCode != 0;
                result.Output = output;
                result.Error = error;

                return result;
            }

        }

        public static void RunCmd(string args, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(gitExePath) || !File.Exists(gitExePath))
                throw new GitException("Git Executable not found");

            Debug.WriteLine(string.Format("{2}>{0} {1}", gitExePath, args, workingDirectory));

            var pinfo = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/C \"\"" + gitExePath + "\" " + args + "\"",
                CreateNoWindow = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            };
            if (UseUTF8FileNames)
            {
                pinfo.StandardErrorEncoding = Encoding.UTF8;
            }
            using (var process = Process.Start(pinfo))
            {
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error))
                    throw new GitException(error);
            }
        }

        public static void OpenGitBash(string workingDirectory)
        {
            if (!Exists) return;

            var gitBashPath = gitExePath.Replace("git.exe", "sh.exe");
            RunDetatched("cmd.exe", string.Format("/c \"{0}\" --login -i", gitBashPath), workingDirectory);
        }

        internal static void RunDetatched(string cmd, string arguments, string workingDirectory)
        {
            using (Process process = new Process())
            {
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.ErrorDialog = false;
                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.RedirectStandardInput = false;

                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.FileName = cmd;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = workingDirectory;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                process.StartInfo.LoadUserProfile = true;

                process.Start();
            }
        }

    }

    [Serializable]
    public class GitException : Exception
    {
        public GitException(string message)
            : base(message)
        {

        }
    }

}
