using System.Collections.Generic;

namespace WorkNest.Application.Interfaces
{
    public interface IPdfMergeService
    {
        byte[] MergePdfs(IEnumerable<byte[]> pdfStreams);
    }
}
