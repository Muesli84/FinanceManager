using FinanceManager.Application.Statements;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public abstract class PDFStatementFilereader : TemplateStatementFileReader, IStatementFileReader
    {
        protected override IEnumerable<string> ReadContent(byte[] fileBytes)
        {
            using var ms = new MemoryStream(fileBytes, false);
            PdfReader iTextReader = new PdfReader(ms);
            try
            {
                PdfDocument pdfDoc = new PdfDocument(iTextReader);
                int numberofpages = pdfDoc.GetNumberOfPages();
                ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                var totalContent = "";
                var lastContent = "";
                for (int pageNo = 1; pageNo <= numberofpages; pageNo++)
                {
                    var page = pdfDoc.GetPage(pageNo);
                    var pageContent = PdfTextExtractor.GetTextFromPage(page, strategy).Replace("\r\n", "\n").Replace("\r", "\n");
                    var currentContent = pageContent;
                    if (!string.IsNullOrWhiteSpace(lastContent) && pageContent.StartsWith(lastContent))
                        pageContent = pageContent.Remove(0, lastContent.Length).TrimStart('\n');
                    lastContent = currentContent;
                    totalContent += pageContent;
                }

                var pageLines = totalContent.TrimEnd('\n').Split('\n');
                foreach (var line in pageLines)
                    yield return line;
            }
            finally
            {
                iTextReader.Close();
            }
        }
    }
}
