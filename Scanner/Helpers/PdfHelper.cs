using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace Scanner
{
	public static class PdfHelper
	{
		public static void AppendFileToDucument(this Section section, Document document, string file)
		{
			var img = section.AddImage(file);
			img.Height = document.DefaultPageSetup.PageHeight;
			img.Width = document.DefaultPageSetup.PageWidth;
		}

		public static void RenderAndSavePdfDocument(this Document document, string filePath)
		{
			var render = new PdfDocumentRenderer()
			{
				Document = document
			};
			render.RenderDocument();
			filePath = CheckFileExistenseAndGetFileName(filePath);
			render.Save(filePath);
		}

		public static string CheckFileExistenseAndGetFileName(string filePath)
		{
			if (File.Exists(filePath))
			{
				string extension = Path.GetExtension(filePath);
				string newFileName = $"{Path.GetFileNameWithoutExtension(filePath)}-copy{extension}";
				string destinationFolder = Path.GetDirectoryName(filePath);
				filePath = Path.Combine(destinationFolder, newFileName);

				if (File.Exists(filePath))
				{
					filePath = CheckFileExistenseAndGetFileName(filePath);
				}
			}

			return filePath;
		}
	}
}
