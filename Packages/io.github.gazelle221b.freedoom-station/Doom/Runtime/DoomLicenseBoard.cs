using UdonSharp;
using UnityEngine;
using TMPro;

// Event-only paged viewer for the original, unmodified license documents.
// No Update(), no per-frame work, no network events, no synced state.
// The Editor builder assigns all public fields; this script only renders.
public class DoomLicenseBoard : UdonSharpBehaviour
{
    public TMP_Text Body;
    public TMP_Text Heading;
    public TMP_Text Counter;
    public TMP_Text Source;

    // Each serialized page has an encoding BOM so Unity YAML preserves CRLF.
    // GetPageText removes only that preamble; the displayed notice is verbatim.
    public string[] Pages;
    public string[] DocumentTitles;
    // For each document: global index of its first page and its page count.
    public int[] DocumentStarts;
    public int[] DocumentCounts;

    // Set by the Editor builder from the single authoritative release config.
    public string SourceUrl;
    public string PayloadSha256;

    [HideInInspector] public int DocumentIndex;
    [HideInInspector] public int PageIndex;

    void Start()
    {
        ShowPage();
    }

    public void NextDocument()
    {
        if (Pages == null || Pages.Length == 0) return;
        int count = DocumentCount();
        DocumentIndex = DocumentIndex + 1;
        if (DocumentIndex >= count) DocumentIndex = 0;
        PageIndex = 0;
        ShowPage();
    }

    public void PreviousDocument()
    {
        if (Pages == null || Pages.Length == 0) return;
        int count = DocumentCount();
        DocumentIndex = DocumentIndex - 1;
        if (DocumentIndex < 0) DocumentIndex = count - 1;
        PageIndex = 0;
        ShowPage();
    }

    public void NextPage()
    {
        if (Pages == null || Pages.Length == 0) return;
        int pageCount = PageCount(DocumentIndex);
        if (pageCount <= 0) return;
        PageIndex = PageIndex + 1;
        if (PageIndex >= pageCount) PageIndex = pageCount - 1;
        ShowPage();
    }

    public void PreviousPage()
    {
        if (Pages == null || Pages.Length == 0) return;
        int pageCount = PageCount(DocumentIndex);
        if (pageCount <= 0) return;
        PageIndex = PageIndex - 1;
        if (PageIndex < 0) PageIndex = 0;
        ShowPage();
    }

    public void ShowPage()
    {
        if (Source != null) Source.text = BuildSourceText();
        if (Pages == null || Pages.Length == 0) return;
        int count = DocumentCount();
        if (DocumentIndex < 0) DocumentIndex = 0;
        if (DocumentIndex >= count) DocumentIndex = count - 1;

        int pageCount = PageCount(DocumentIndex);
        if (pageCount <= 0) return;
        if (PageIndex < 0) PageIndex = 0;
        if (PageIndex >= pageCount) PageIndex = pageCount - 1;

        int page = PageStart(DocumentIndex) + PageIndex;
        if (Body != null && page >= 0 && page < Pages.Length)
            Body.text = GetPageText(page);
        if (Heading != null && DocumentTitles != null && DocumentIndex < DocumentTitles.Length)
            Heading.text = DocumentTitles[DocumentIndex];
        if (Counter != null)
            Counter.text = "文書 " + (DocumentIndex + 1) + " / " + count
                + "　ページ " + (PageIndex + 1) + " / " + pageCount;
    }

    public string GetPageText(int index)
    {
        return Pages[index].Substring(1);
    }

    private string BuildSourceText()
    {
        string text = "ソースの入手先:";
        if (SourceUrl != "")
            text = text + "\n" + SourceUrl;
        if (PayloadSha256 != "")
            text = text + "\nSHA-256: " + PayloadSha256;
        return text;
    }

    private int DocumentCount()
    {
        if (DocumentStarts != null && DocumentStarts.Length > 0) return DocumentStarts.Length;
        if (DocumentCounts != null && DocumentCounts.Length > 0) return DocumentCounts.Length;
        if (DocumentTitles != null && DocumentTitles.Length > 0) return DocumentTitles.Length;
        return 1;
    }

    private int PageStart(int document)
    {
        if (DocumentStarts != null && document < DocumentStarts.Length) return DocumentStarts[document];
        return 0;
    }

    private int PageCount(int document)
    {
        if (DocumentCounts != null && document < DocumentCounts.Length) return DocumentCounts[document];
        return Pages != null ? Pages.Length : 0;
    }
}
