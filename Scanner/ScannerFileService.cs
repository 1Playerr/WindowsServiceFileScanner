using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MigraDoc.DocumentObjectModel;
using Scanner.Configuration;
using Topshelf;
using Topshelf.Logging;
using ZXing;

namespace Scanner
{
	public class ScannerFileService : ServiceControl
	{
		#region Constructor

		public ScannerFileService()
		{
			_config = (ScannerConfigSection)ConfigurationManager.GetSection("scannerSection");
			_listennedFolders = new HashSet<string>();
			_failedToDelete = new HashSet<string>();
			_pauseSignal = new ManualResetEvent(false);
			_barcodeReader = new ThreadLocal<BarcodeReader>(() => new BarcodeReader() { AutoRotate = true });
			_log = HostLogger.Get<ScannerFileService>();
		}

		#endregion

		#region Fields

		private ThreadLocal<Queue<string>> _filesForDocument;
		private ScannerConfigSection _config;
		private HashSet<string> _listennedFolders;
		private ManualResetEvent _pauseSignal;
		private bool _isServicePaused = false;
		private CancellationTokenSource _tokenSource;
		private CancellationToken _token;
		private int _numberOfAttemptsToGetFileAccess = 5;
		private int _pauseBetweenAttempts = 1000;
		private int _timeBreakBetweenFolderEnumeration = 5000;
		private int _timeBreakBetweenSeparateDocuments = 12000;
		private ThreadLocal<BarcodeReader> _barcodeReader;
		private HashSet<string> _failedToDelete;
		private readonly LogWriter _log;
		private int _countOfThreadsForPause = 0;
		private int _countOfThreadsForStop = 0;
		[ThreadStatic]
		private bool _isFirstRun = true;
		private bool _isCustomCommandTaskHandlerStarted = false;
		private bool _isStopped = false;
		#endregion

		#region Service control methods
		public bool Start(HostControl hostControl)
		{
			this.InitializeStartData();

			if (!_isCustomCommandTaskHandlerStarted)
			{
				Task.Factory.StartNew(() => this.CustomCommandFromConsoleForEasyDebug());
			}

			foreach (FolderElement folder in _config.FoldersToListen)
			{
				if (!_listennedFolders.Contains(folder.FolderPath))
				{
					_listennedFolders.Add(folder.FolderPath);

					Task.Factory
						.StartNew(() =>
							StartFolderScan(folder.FolderPath), _token)
						.ContinueWith(t => CountCancelledTasks(t), TaskContinuationOptions.OnlyOnCanceled);
				}
			}

			_log.Info("Service started");
			return true;
		}

		public bool Stop(HostControl hostControl)
		{
			if (_isStopped)
			{
				_log.Info("The service is already stopped.");
				return false;
			}

			_tokenSource.Cancel();
			while (_countOfThreadsForStop != _listennedFolders.Count)
			{
				Task.Delay(200).Wait();
			}
			_tokenSource.Dispose();
			_listennedFolders.Clear();
			_filesForDocument.Dispose();
			_isStopped = true;
			_log.Info("Service stopped");

			return true;
		}

		public bool Pause()
		{
			if (_isServicePaused)
			{
				_log.Info("The service is already paused");
				return false;
			}

			_isServicePaused = true;
			while (_countOfThreadsForPause != _listennedFolders.Count)
			{
				Task.Delay(200).Wait();
			}

			_log.Info("Service paused");
			return true;
		}

		public bool Continue()
		{
			_isServicePaused = false;
			_countOfThreadsForPause = 0;
			_isFirstRun = true;
			_pauseSignal.Set();

			_log.Info("Service continued");
			return true;
		}

		#endregion

		private void InitializeStartData()
		{
			_filesForDocument = new ThreadLocal<Queue<string>>(() => new Queue<string>());
			_tokenSource = new CancellationTokenSource();
			_countOfThreadsForStop = 0;
			_isStopped = false;
			_token = _tokenSource.Token;
		}

		public void CustomCommand(int parameter)
		{
			Console.WriteLine($"Custom command {parameter} received.");
		}

		#region This methods should be used for debug purpose only

		public void CustomCommandFromConsoleForEasyDebug()
		{
			while (true)
			{
				var parameter = Console.ReadLine();

				if (parameter == "200")
				{
					this.Pause();
				}
				else if (parameter == "201")
				{
					this.Continue();
				}
				else if (parameter == "202")
				{
					this.Stop();
					//this.Stop((HostControl)new object());
				}
				else if (parameter == "203")
				{
					this.Start();
					//this.Start((HostControl)new object());
				}
			}
		}

		//This method is for debugging only
		public bool Start()
		{
			this.InitializeStartData();

			if (_isCustomCommandTaskHandlerStarted)
			{
				Task.Factory.StartNew(() => this.CustomCommandFromConsoleForEasyDebug());
			}
			foreach (FolderElement folder in _config.FoldersToListen)
			{
				if (!_listennedFolders.Contains(folder.FolderPath))
				{
					_listennedFolders.Add(folder.FolderPath);

					Task.Factory
						.StartNew(() =>
							StartFolderScan(folder.FolderPath), _token)
						.ContinueWith(t => CountCancelledTasks(t), TaskContinuationOptions.OnlyOnCanceled);
				}
			}

			_log.Info("Service started");
			return true;
		}

		//This method is for debugging only
		public bool Stop()
		{
			if (_isStopped)
			{
				_log.Info("The service is already stopped.");
				return false;
			}

			_tokenSource.Cancel();
			while (_countOfThreadsForStop != _listennedFolders.Count)
			{
				Task.Delay(200).Wait();
			}
			_tokenSource.Dispose();
			_listennedFolders.Clear();
			_filesForDocument.Dispose();
			_isStopped = true;
			_log.Info("Service stopped");

			return true;
		}
		#endregion

		private void StartFolderScan(string inputDirectory)
		{
			_log.Info(this.GetLogText($"New task started listenning to folder {inputDirectory}"));

			while (true)
			{
				string[] files = this.GetFilesFromFolder(inputDirectory);
				ProcessFiles(files);
			}
		}

		private string[] GetFilesFromFolder(string inputDirectory)
		{
			string[] files = null;
			double numberOfAttemptsBetweenSeparateDocument = 0;
			int currentAtempt = 0;

			while ((files == null || !files.Any()) || (files != null && files.All(x => _filesForDocument.Value.Contains(x))))
			{
				if (_token.IsCancellationRequested)
				{
					_token.ThrowIfCancellationRequested();
				}

				if (_isServicePaused)
				{
					this.CountThreadForPauseAndWait();
				}

				if (currentAtempt >= numberOfAttemptsBetweenSeparateDocument 
					&& numberOfAttemptsBetweenSeparateDocument != 0)
				{
					this.BuildPdfDocumentFromChunks();
					currentAtempt = 0;
					numberOfAttemptsBetweenSeparateDocument = 0;
				}

				try
				{
					files = Directory.GetFiles(inputDirectory);
				}
				catch (Exception ex)
				{
					_log.Error(this.GetLogText($"Failed to get files from directory {inputDirectory}", ex));
				}

				if (files != null && files.Any() && !files.All(x => _filesForDocument.Value.Contains(x)))
				{
					if (this.CheckForFirstRunAfterPauseOrStop(files))
					{
						files = new string[] { };
						continue;
					}
					if (_filesForDocument.Value.Any())
					{
						files = files.Where(x => !_filesForDocument.Value.Contains(x) || _failedToDelete.Contains(x)).ToArray();
					}
					break;
				}
				else if (numberOfAttemptsBetweenSeparateDocument == 0)
				{
					_isFirstRun = false;
					currentAtempt = 0;
					numberOfAttemptsBetweenSeparateDocument = this.GetNumberOfAttemptsBetweenSeparateDocument();
				}
				currentAtempt++;

				Task.Delay(_timeBreakBetweenFolderEnumeration).Wait();
			}

			return files;
		}

		private bool CheckForFirstRunAfterPauseOrStop(string[] files)
		{
			if (_isFirstRun)
			{
				this.ProcessFiles(files);
				this.BuildPdfDocumentFromChunks();
				_isFirstRun = false;

				return true;
			}

			return false;
		}

		private double GetNumberOfAttemptsBetweenSeparateDocument()
		{
			double res = (double)_timeBreakBetweenSeparateDocuments / (double)_timeBreakBetweenFolderEnumeration;
			return Math.Round(res, MidpointRounding.AwayFromZero);
		}

		private void ProcessFiles(string[] files)
		{
			foreach (var sourceFileName in files)
			{
				if (!_failedToDelete.Contains(sourceFileName))
				{
					ProcessFile(sourceFileName);
				}
			}
		}

		private void ProcessFile(string sourceFileName)
		{
			string fileName = Path.GetFileName(sourceFileName);
			bool isMatch = Regex.IsMatch(fileName, _config.FilePattern.FilePattern, RegexOptions.Multiline);

			if (isMatch)
			{
				bool isEndOfDocument = false;
				try
				{
					isEndOfDocument = this.IsEndOfDocument(sourceFileName);
				}
				catch (FileNotFoundException ex)
				{
					_log.Error(this.GetLogText($"Failed to find file {sourceFileName}", ex));
					this.MoveFilesToRecoveryDirectory(_filesForDocument.Value);
					return;
				}
				catch (Exception ex)
				{
					_log.Error(this.GetLogText($"Failed to convert file {sourceFileName} to Bitmap", ex));
					_filesForDocument.Value.Enqueue(sourceFileName);
					this.MoveFilesToRecoveryDirectory(_filesForDocument.Value);
					return;
				}

				if (isEndOfDocument)
				{
					this.BuildPdfDocumentFromChunks();
					this.DeleteFile(sourceFileName);
				}
				else
				{
					_filesForDocument.Value.Enqueue(sourceFileName);
				}
			}
			else
			{
				this.MoveFile(sourceFileName, _config.RecoveryFolder.FolderPath);
			}
		}

		private void BuildPdfDocumentFromChunks()
		{
			if (_filesForDocument.Value.Any())
			{
				var processedFiles = new List<string>();
				var document = new Document();
				var section = document.AddSection();

				while (_filesForDocument.Value.Count != 0)
				{
					string file = _filesForDocument.Value.Dequeue();
					processedFiles.Add(file);

					if (File.Exists(file))
					{
						try
						{
							section.AppendFileToDucument(document, file);
						}
						catch (Exception ex)
						{
							_log.Error(this.GetLogText($"Failed to append file '{file}' to pdf document.", ex));
							this.MoveFilesToRecoveryDirectory(processedFiles);
						}
					}
					else
					{
						this.MoveFilesToRecoveryDirectory(processedFiles);
						return;
					}
				}

				document.RenderAndSavePdfDocument(this.GetDocumentFileName(processedFiles));
				this.DeleteRenderedFiles(processedFiles);
			}
		}

		private void DeleteFile(string sourceFileName)
		{
			bool isDeleted = false;
			for (int i = 0; i < _numberOfAttemptsToGetFileAccess; i++)
			{
				if (File.Exists(sourceFileName))
				{
					try
					{
						File.Delete(sourceFileName);
						isDeleted = true;
						break;
					}
					catch (Exception ex)
					{
						_log.Error(this.GetLogText($"Wasn't able to delete file '{sourceFileName}'", ex));
					}
					Task.Delay(_pauseBetweenAttempts).Wait();
				}
				else
				{
					isDeleted = true;
				}
			}
			if (!isDeleted)
			{
				_failedToDelete.Add(sourceFileName);
				_log.Warn(this.GetLogText($"File {sourceFileName} wasn't deleted."));
			}
		}

		private void DeleteRenderedFiles(IEnumerable<string> files)
		{
			foreach (var file in files)
			{
				this.DeleteFile(file);
			}
		}

		private string GetDocumentFileName(IList<string> files)
		{
			string concatedName = null;
			string extension = ".pdf";
			string firstName = Path.GetFileNameWithoutExtension(files.FirstOrDefault());

			if (files.Count > 1)
			{
				string lastName = Path.GetFileNameWithoutExtension(files.LastOrDefault());
				concatedName = $"{firstName}-{lastName}{extension}";
			}
			else
			{
				concatedName = $"{firstName}{extension}";
			}

			return Path.Combine(_config.DestinationFolder.FolderPath, concatedName);
		}

		private void MoveFilesToRecoveryDirectory(IEnumerable<string> files)
		{
			foreach (var file in files)
			{
				this.MoveFile(file, _config.RecoveryFolder.FolderPath);
			}
		}

		private bool IsEndOfDocument(string sourceFileName)
		{
			if (File.Exists(sourceFileName))
			{
				using (Bitmap bmp = (Bitmap)Image.FromFile(sourceFileName))
				{
					var result = _barcodeReader.Value.Decode(bmp);

					return result != null && result.Text == "End of document";
				}
			}

			throw new FileNotFoundException($"File '{sourceFileName}' wasn't found.");
		}

		private void MoveFile(string sourceFileName, string destFolder)
		{
			string fileName = Path.GetFileName(sourceFileName);
			if (File.Exists(Path.Combine(destFolder, fileName)))
			{
				string newFileName = PdfHelper.CheckFileExistenseAndGetFileName(Path.Combine(destFolder, fileName));
				try
				{
					File.Copy(sourceFileName, newFileName);
					this.DeleteFile(sourceFileName);
				}
				catch (Exception ex)
				{
					_log.Error(ex);
				}
			}
			else
			{
				for (int i = 0; i < _numberOfAttemptsToGetFileAccess; i++)
				{
					try
					{
						File.Move(sourceFileName, Path.Combine(destFolder, fileName));
						break;
					}
					catch (Exception ex)
					{
						_log.Error(this.GetLogText($"Failed to move file '{sourceFileName}' to {destFolder}", ex));
					}

					Task.Delay(_pauseBetweenAttempts).Wait();
				}
			}
		}

		private void CountCancelledTasks(Task t)
		{
			Interlocked.Add(ref _countOfThreadsForStop, 1);
		}

		private void CountThreadForPauseAndWait()
		{
			Interlocked.Add(ref _countOfThreadsForPause, 1);
			_pauseSignal.WaitOne();
		}

		private string GetLogText(string message, Exception ex = null)
		{
			return ($"{DateTime.Now} | Task id: {Task.CurrentId} | {message} | {ex}");
		}
	}
}
