namespace n8PDF;

/// <summary>
/// The data one merge field set is filled from: a record of a data source, as a name for each
/// field and what that field holds.
/// </summary>
/// <remarks>
/// A document written for a mail merge does not carry its own data — it names an external source,
/// a spreadsheet or an address book, that only the machine it was written on can reach. A
/// conversion therefore has nothing to fill the fields from unless it is given something, and a
/// document converted with none shows what Word shows in the same case: the name of each field in
/// guillemets, «like this».
/// </remarks>
public sealed class MailMergeRecord
{
    public MailMergeRecord(IEnumerable<KeyValuePair<string, string>>? fields = null)
    {
        Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (fields is null) return;

        foreach (var (name, value) in fields) Fields[name] = value;
    }

    /// <summary>The fields of the record, by the name the document knows them by.</summary>
    public Dictionary<string, string> Fields { get; }

    /// <summary>Which record of the source this is, which MERGEREC shows.</summary>
    public int Number { get; set; } = 1;

    /// <summary>
    /// Which of the merged records this is, which MERGESEQ shows. It differs from the record
    /// number where the merge passes some of the source's records over.
    /// </summary>
    public int Sequence { get; set; } = 1;

    /// <summary>
    /// What a named field holds, or null where the record does not have it — which is not the
    /// same as having it empty, since an empty field prints nothing at all and a missing one
    /// leaves its own name standing.
    /// </summary>
    public string? Value(string name) => Fields.GetValueOrDefault(name);
}
