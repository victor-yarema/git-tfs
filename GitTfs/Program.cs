using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Sep.Git.Tfs.Core;
using Sep.Git.Tfs.Core.Changes.Git;
using Sep.Git.Tfs.Core.TfsInterop;
using Sep.Git.Tfs.Util;
using StructureMap;
using StructureMap.Graph;

namespace Sep.Git.Tfs
{
    public class Program
    {
        [STAThreadAttribute]
        public static void Main(string[] args)
        {
            try
            {
                Environment.ExitCode = MainCore(args);
            }
            catch (Exception e)
            {
                ReportException(e);
                Environment.ExitCode = GitTfsExitCodes.ExceptionThrown;
            }
        }

        public static int MainCore(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var curArg = args[i];
                if (curArg.StartsWith("$//"))
                {
                    args[i] = curArg.Replace("$//", "$/");
                }
            }

            var container = Initialize();
            return container.GetInstance<GitTfs>().Run(new List<string>(args));
        }

        private static void ReportException(Exception e)
        {
            var gitTfsException = e as GitTfsException;
            if(gitTfsException != null)
            {
                Trace.WriteLine(gitTfsException);
                Console.WriteLine(gitTfsException.Message);
                if (gitTfsException.InnerException != null)
                    ReportException(gitTfsException.InnerException);
                if (!gitTfsException.RecommendedSolutions.IsEmpty())
                {
                    Console.WriteLine("You may be able to resolve this problem.");
                    foreach (var solution in gitTfsException.RecommendedSolutions)
                    {
                        Console.WriteLine("- " + solution);
                    }
                }
            }
            else
            {
                ReportInternalException(e);
            }
        }

        private static void ReportInternalException(Exception e)
        {
            Trace.WriteLine(e);
            while(e is TargetInvocationException && e.InnerException != null)
                e = e.InnerException;
            while (e != null)
            {
                var gitCommandException = e as GitCommandException;
                if (gitCommandException != null)
                    Console.WriteLine("error running command: " + gitCommandException.Process.StartInfo.FileName + " " + gitCommandException.Process.StartInfo.Arguments);

                Console.WriteLine(e.Message);
                e = e.InnerException;
            }
        }

        private static IContainer Initialize()
        {
            return new Container(Initialize);
        }

        private static void Initialize(ConfigurationExpression initializer)
        {
            var tfsPlugin = TfsPlugin.Find();
            initializer.Scan(x => { Initialize(x); tfsPlugin.Initialize(x); });
            initializer.For<TextWriter>().Use(() => Console.Out);
            initializer.For<IGitRepository>().Add<GitRepository>();
            AddGitChangeTypes(initializer);
            DoCustomConfiguration(initializer);
            tfsPlugin.Initialize(initializer);
        }

        public static void AddGitChangeTypes(ConfigurationExpression initializer)
        {
            // See git-diff-tree(1).
            initializer.For<IGitChangedFile>().Use<Add>().Named(GitChangeInfo.ChangeType.ADD);
            initializer.For<IGitChangedFile>().Use<Copy>().Named(GitChangeInfo.ChangeType.COPY);
            initializer.For<IGitChangedFile>().Use<Modify>().Named(GitChangeInfo.ChangeType.MODIFY);
            //initializer.For<IGitChangedFile>().Use<TypeChange>().Named(GitChangeInfo.GitChange.TYPECHANGE);
            initializer.For<IGitChangedFile>().Use<Delete>().Named(GitChangeInfo.ChangeType.DELETE);
            initializer.For<IGitChangedFile>().Use<RenameEdit>().Named(GitChangeInfo.ChangeType.RENAMEEDIT);
            //initializer.For<IGitChangedFile>().Use<Unmerged>().Named(GitChangeInfo.GitChange.UNMERGED);
            //initializer.For<IGitChangedFile>().Use<Unknown>().Named(GitChangeInfo.GitChange.UNKNOWN);
        }

        private static void Initialize(IAssemblyScanner scan)
        {
            scan.WithDefaultConventions();
            scan.TheCallingAssembly();
        }

        private static void DoCustomConfiguration(ConfigurationExpression initializer)
        {
            foreach(var type in typeof(Program).Assembly.GetTypes())
            {
                foreach(ConfiguresStructureMap attribute in type.GetCustomAttributes(typeof(ConfiguresStructureMap), false))
                {
                    attribute.Initialize(initializer, type);
                }
            }
        }
    }
}
