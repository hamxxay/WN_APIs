using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public class PdfMergeService : IPdfMergeService
    {
        public byte[] MergePdfs(IEnumerable<byte[]> pdfStreams)
        {
            if (pdfStreams == null) throw new ArgumentNullException(nameof(pdfStreams));

            using var outputDocument = new PdfDocument();

            foreach (var pdfBytes in pdfStreams)
            {
                if (pdfBytes == null || pdfBytes.Length == 0) continue;

                using var inputStream = new MemoryStream(pdfBytes);
                using var inputDocument = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);

                int count = inputDocument.PageCount;
                for (int i = 0; i < count; i++)
                {
                    PdfPage page = inputDocument.Pages[i];
                    outputDocument.AddPage(page);
                }
            }

            using var outputStream = new MemoryStream();
            outputDocument.Save(outputStream, false);
            return outputStream.ToArray();
        }
    }
}
