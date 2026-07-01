#if NET8_0_OR_GREATER
namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion.UnitTests;

/// <summary>
/// Helper to locate test PDF documents for integration tests.
/// Looks in a "Documents" folder relative to the test output directory.
/// </summary>
internal static class IntegrationHelpers
{
    private static readonly string DocumentsFolder = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Documents");

    public static string GetDocumentPath(string name)
    {
        // Try with .pdf extension first
        var path = Path.Combine(DocumentsFolder, name + ".pdf");
        if (File.Exists(path))
        {
            return path;
        }

        // Try without extension
        path = Path.Combine(DocumentsFolder, name);
        if (File.Exists(path))
        {
            return path;
        }

        // Return the .pdf path (caller's test will fail with a clear "file not found" message)
        return Path.Combine(DocumentsFolder, name + ".pdf");
    }
}
#endif
