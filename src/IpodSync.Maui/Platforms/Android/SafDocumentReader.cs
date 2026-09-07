using Android.Content;
using Android.Provider;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Reads a file by path out of a Storage Access Framework document tree (the
/// URI returned by <see cref="SafBridge.PickTreeAsync"/>). Walks one path
/// segment at a time via DocumentsContract's child-listing query rather than
/// assuming any particular URI shape, since document IDs are provider-specific
/// and not meant to be constructed by hand.
/// </summary>
public static class SafDocumentReader
{
    public static async Task<byte[]?> ReadFileAsync(ContentResolver resolver, global::Android.Net.Uri treeUri, string relativePath, CancellationToken ct = default)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string documentId = DocumentsContract.GetTreeDocumentId(treeUri)!;

        foreach (string segment in segments)
        {
            string? childId = FindChildDocumentId(resolver, treeUri, documentId, segment);
            if (childId is null) return null;
            documentId = childId;
        }

        var fileUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId);
        using var stream = resolver.OpenInputStream(fileUri!);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string? FindChildDocumentId(ContentResolver resolver, global::Android.Net.Uri treeUri, string parentDocumentId, string name)
    {
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentDocumentId);
        string[] projection = [DocumentsContract.Document.ColumnDocumentId!, DocumentsContract.Document.ColumnDisplayName!];
        using var cursor = resolver.Query(childrenUri!, projection, null, null, null);
        if (cursor is null) return null;

        while (cursor.MoveToNext())
        {
            string? docId = cursor.GetString(0);
            string? displayName = cursor.GetString(1);
            if (docId is not null && string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase)) return docId;
        }
        return null;
    }
}
