using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace PCHealthPro.Core.Reporting;

internal static class ReportPdf
{
    static ReportPdf() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    internal static void Write(Stream output, ReportLayout layout)
    {
        var doc = new Document();
        doc.Info.Title = layout.Title;
        doc.Info.Author = layout.Brand;
        var normal = doc.Styles["Normal"]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        normal.Font.Color = Color.FromRgb(24, 48, 59);
        var page = doc.AddSection();
        page.PageSetup.PageFormat = PageFormat.A4;
        page.PageSetup.LeftMargin = page.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        page.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        page.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        page.PageSetup.FooterDistance = Unit.FromCentimeter(0.9);
        var teal = Color.FromRgb(9, 92, 82);
        var brand = page.AddTable();
        brand.AddColumn(Unit.FromCentimeter(0.95));
        brand.AddColumn(Unit.FromCentimeter(16.45));
        var strip = brand.AddRow();
        strip.VerticalAlignment = VerticalAlignment.Center;
        strip.Height = Unit.FromCentimeter(0.8);
        strip.Cells[0].Shading.Color = teal;
        var mark = strip.Cells[0].AddParagraph("PC"); mark.Format.Alignment = ParagraphAlignment.Center;
        mark.Format.Font.Bold = true; mark.Format.Font.Color = Colors.White;
        strip.Cells[1].AddParagraph(layout.Brand).Format.Font.Bold = true;
        var title = page.AddParagraph(layout.Title);
        title.Format.SpaceBefore = 10; title.Format.Font.Size = 21; title.Format.Font.Bold = true;
        page.AddParagraph($"{layout.Machine}   ·   {layout.Date}");
        foreach (var group in layout.Sections)
        {
            var heading = page.AddParagraph(group.Title);
            heading.Format.SpaceBefore = 10; heading.Format.SpaceAfter = 4;
            heading.Format.Font.Bold = true; heading.Format.Font.Size = 12;
            heading.Format.Font.Color = teal; heading.Format.KeepWithNext = true;
            var table = page.AddTable();
            table.AddColumn(Unit.FromCentimeter(6.0));
            table.AddColumn(Unit.FromCentimeter(11.4));
            table.Rows.VerticalAlignment = VerticalAlignment.Center;
            table.TopPadding = 3; table.BottomPadding = 3;
            int index = 0;
            foreach (var line in group.Lines)
            {
                bool shaded = index++ % 2 == 0;
                var parts = line.Value.Replace("\r\n", "\n").Split('\n');
                bool first = true;
                foreach (var part in parts)
                foreach (var chunk in part.Chunk(400).Select(chars => new string(chars)).DefaultIfEmpty(""))
                {
                    var row = table.AddRow();
                    if (shaded) row.Shading.Color = Color.FromRgb(239, 246, 244);
                    if (first) row.Cells[0].AddParagraph(line.Label);
                    row.Cells[1].AddParagraph(WrapTokens(chunk));
                    first = false;
                }
            }
        }
        var footer = page.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8; footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText(layout.Brand + "    ·    "); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.Save(output, false);
    }

    private static string WrapTokens(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\S{36}(?=\S)", "$0\u00ad");
}
