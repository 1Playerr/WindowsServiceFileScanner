using System.Configuration;

namespace Scanner.Configuration
{
	public class FilePatternElement : ConfigurationElement
	{
		[ConfigurationProperty("pattern", IsKey = true, IsRequired = true)]
		public string FilePattern
		{
			get { return (string)base["pattern"]; }
		}
	}
}
