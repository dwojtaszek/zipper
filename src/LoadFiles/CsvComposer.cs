using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the CSV format (standard generation only). Columns default to UPPERCASE
/// naming for e-discovery platform compatibility unless a column profile overrides the
/// convention. Shares the single-record-per-file pipeline in <see cref="StandardRowComposer"/>.
/// </summary>
internal sealed class CsvComposer : StandardRowComposer
{
    public CsvComposer(FileGenerationRequest request)
        : base(request)
    {
    }

    protected override bool IncludeAttachmentBoundaryColumns => false;

    protected override string HeaderName(string columnKey)
    {
        var convention = this.request.Metadata.ColumnProfile?.FieldNamingConvention ?? "UPPERCASE";
        return NamingConventionHelper.ApplyConvention(Friendly(columnKey), convention);
    }

    private static string Friendly(string columnKey) => columnKey switch
    {
        "CONTROL" => "Control Number",
        "PATH" => "File Path",
        "CUSTODIAN" => "Custodian",
        "DATESENT" => "Date Sent",
        "AUTHOR" => "Author",
        "FILESIZE" => "File Size",
        "TO" => "To",
        "FROM" => "From",
        "SUBJECT" => "Subject",
        "SENTDATE" => "Sent Date",
        "ATTACHMENT" => "Attachment",
        "BATES" => "Bates Number",
        "PAGECOUNT" => "Page Count",
        "TEXT" => "Extracted Text",
        _ => columnKey,
    };
}
