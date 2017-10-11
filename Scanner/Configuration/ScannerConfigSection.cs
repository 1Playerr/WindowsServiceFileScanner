using System.Configuration;

namespace Scanner.Configuration
{
	public class ScannerConfigSection : ConfigurationSection
	{
		[ConfigurationProperty("appName")]
		public string ApplicationName
		{
			get { return (string)base["appName"]; }
		}

		[ConfigurationCollection(typeof(FolderElement),
			AddItemName = "folder",
			ClearItemsName = "clear",
			RemoveItemName = "del")]
		[ConfigurationProperty("sourceFolders")]
		public FolderElementCollection FoldersToListen
		{
			get { return (FolderElementCollection)base["sourceFolders"]; }
		}

		[ConfigurationProperty("filePattern")]
		public FilePatternElement FilePattern
		{
			get { return (FilePatternElement)base["filePattern"]; }
		}

		[ConfigurationProperty("destinationFolder")]
		public DestinationFolderElement DestinationFolder
		{
			get { return (DestinationFolderElement)base["destinationFolder"]; }
		}

		[ConfigurationProperty("recoveryDirectory")]
		public RecoveryFolderElement RecoveryFolder
		{
			get { return (RecoveryFolderElement)base["recoveryDirectory"]; }
		}
	}
}
