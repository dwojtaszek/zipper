namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the Concordance DAT format (standard generation only). Uses the fixed
/// Concordance field names and emits leading BEGATTY/ENDATTY columns (left empty — parent/child
/// attachment ranges are not tracked). Shares the single-record-per-file pipeline in
/// <see cref="StandardRowComposer"/>.
/// </summary>
internal sealed class ConcordanceComposer : StandardRowComposer
{
    public ConcordanceComposer(FileGenerationRequest request)
        : base(request)
    {
    }

    protected override bool IncludeAttachmentBoundaryColumns => true;

    protected override string HeaderName(string columnKey) => columnKey switch
    {
        "BEGATTY" => "BEGATTY",
        "ENDATTY" => "ENDATTY",
        "CONTROL" => "CONTROLNUMBER",
        "PATH" => "PATH",
        "CUSTODIAN" => "CUSTODIAN",
        "DATESENT" => "DATESENT",
        "AUTHOR" => "AUTHOR",
        "FILESIZE" => "FILESIZE",
        "TO" => "TO",
        "FROM" => "FROM",
        "SUBJECT" => "SUBJECT",
        "SENTDATE" => "SENTDATE",
        "ATTACHMENT" => "ATTACHMENT",
        "BATES" => "BATES",
        "PAGECOUNT" => "PAGECOUNT",
        "TEXT" => "TEXT_PATH",
        _ => columnKey,
    };
}
