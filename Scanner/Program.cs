using System.Diagnostics;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using Topshelf;

namespace Scanner
{
	class Program
	{
		static void Main(string[] args)
		{
			var logFactory = ConfigureNLog();

			HostFactory.Run(
				hostConf =>
				{
					hostConf.Service<ScannerFileService>(
						s =>
						{
							s.ConstructUsing(() => new ScannerFileService());
							s.WhenStarted((serv, hostControl) => serv.Start(hostControl));
							s.WhenStopped((serv, hostControl) => serv.Stop(hostControl));
							s.WhenPaused(serv => serv.Pause());
							s.WhenContinued(serv => serv.Continue());
							s.WhenCustomCommandReceived((execute, hostControl, commandNumber) =>
								execute.CustomCommand(commandNumber));
						}).UseNLog(logFactory);

					
					hostConf.EnablePauseAndContinue();
					hostConf.SetServiceName("ScannerFileService");
					hostConf.SetDisplayName("Scanner File Service");
					hostConf.SetDescription("Scans folders for .jpg or .png files of scanned documents and coverts them to the whole .pdf file.");
					hostConf.StartAutomatically();
					hostConf.EnableServiceRecovery(recoveryOption => recoveryOption.RestartService(1));
				});
		}

		private static LogFactory ConfigureNLog()
		{
			string currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
			var logConfig = new LoggingConfiguration();
			var fileTarget = new FileTarget()
			{
				Name = "Default",
				FileName = Path.Combine(currentDir, "log.txt"),
				Layout = "${message}"
			};
			var consoleTarget = new ConsoleTarget()
			{
				Name = "Console",
				Layout = "${message}"
			};
			logConfig.AddTarget(fileTarget);
			logConfig.AddTarget(consoleTarget);
			logConfig.AddRuleForAllLevels(fileTarget);
			logConfig.AddRuleForAllLevels(consoleTarget);

			return new LogFactory(logConfig);
		}
	}
}
