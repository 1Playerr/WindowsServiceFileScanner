﻿using System.Configuration;

namespace Scanner.Configuration
{
	public class RecoveryFolderElement : ConfigurationElement
	{
		[ConfigurationProperty("path", IsKey = true, IsRequired = true)]
		public string FolderPath
		{
			get { return (string)base["path"]; }
		}
	}
}
