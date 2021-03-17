﻿using MauiDoctor.Doctoring;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MauiDoctor.Checkups
{
	public class OpenJdkInfo
	{
		public OpenJdkInfo(string javaCFile, NuGetVersion version)
		{
			JavaC = new FileInfo(javaCFile);
			Version = version;
		}

		public FileInfo JavaC { get; set; }

		public DirectoryInfo Directory
			=> new DirectoryInfo(Path.Combine(JavaC.Directory.FullName, ".."));

		public NuGetVersion Version { get; set; }
	}

	public class OpenJdkCheckup : Doctoring.Checkup
	{
		public OpenJdkCheckup(string minimumVersion, string exactVersion = null)
		{
			MinimumVersion = NuGetVersion.Parse(minimumVersion);
			ExactVersion = exactVersion != null ? NuGetVersion.Parse(exactVersion) : null;
		}

		public NuGetVersion MinimumVersion { get; private set; } = new NuGetVersion("1.8.0-1");
		public NuGetVersion ExactVersion { get; private set; }

		public override string Id => "openjdk";

		public override string Title => $"OpenJDK {MinimumVersion.ThisOrExact(ExactVersion)}";

		public override Task<Diagonosis> Examine()
		{
			var jdks = FindJdks();

			var ok = false;

			foreach (var jdk in jdks)
			{
				if ((jdk.JavaC.FullName.Contains("microsoft") || jdk.JavaC.FullName.Contains("openjdk"))
					&& jdk.Version.IsCompatible(MinimumVersion, ExactVersion))
				{
					ok = true;
					ReportStatus($"{jdk.Version} ({jdk.Directory})", Status.Ok);
					Util.SetDoctorEnvironmentVariable("JAVA_HOME", jdk.Directory.FullName);
				}
				else
					ReportStatus($"{jdk.Version} ({jdk.Directory})", null);
			}

			if (ok)
				return Task.FromResult(Diagonosis.Ok(this));

			return Task.FromResult(new Diagonosis(Status.Error, this));
		}

		IEnumerable<OpenJdkInfo> FindJdks()
		{
			var paths = new List<OpenJdkInfo>();

			if (Util.IsWindows)
			{
				SearchDirectoryForJdks(paths, "C:\\Program Files\\Android\\Jdk\\", true);
			} else if (Util.IsMac)
			{
				SearchDirectoryForJdks(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "jdk"), true);
			}

			SearchDirectoryForJdks(paths, Environment.GetEnvironmentVariable("JAVA_HOME") ?? string.Empty, true);
			SearchDirectoryForJdks(paths, Environment.GetEnvironmentVariable("JDK_HOME") ?? string.Empty, true);

			var environmentPaths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();

			foreach (var envPath in environmentPaths)
			{
				if (envPath.Contains("java", StringComparison.OrdinalIgnoreCase) || envPath.Contains("jdk", StringComparison.OrdinalIgnoreCase))
					SearchDirectoryForJdks(paths, envPath, true);
			}

			return paths
				.GroupBy(i => i.JavaC.FullName)
				.Select(g => g.First());
		}

		void SearchDirectoryForJdks(IList<OpenJdkInfo> found, string directory, bool recursive = true)
		{
			if (string.IsNullOrEmpty(directory))
				return;

			var dir = new DirectoryInfo(directory);

			if (dir.Exists)
			{
				var files = dir.EnumerateFileSystemInfos("javac.exe", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

				foreach (var file in files)
				{
					if (TryGetJavaJdkInfo(file.FullName, out var jdkInfo))
						found.Add(jdkInfo);
				}
			}
		}

		static readonly Regex rxJavaCVersion = new Regex("[0-9\\.\\-_]+", RegexOptions.Singleline);

		bool TryGetJavaJdkInfo(string javacFilename, out OpenJdkInfo javaJdkInfo)
		{
			var r = ShellProcessRunner.Run(javacFilename, "-version");
			var m = rxJavaCVersion.Match(r.GetOutput() ?? string.Empty);

			var v = m?.Value;

			if (!string.IsNullOrEmpty(v) && NuGetVersion.TryParse(v, out var version))
			{
				javaJdkInfo = new OpenJdkInfo(javacFilename, version);
				return true;
			}

			javaJdkInfo = default;
			return false;
		}
	}
}
