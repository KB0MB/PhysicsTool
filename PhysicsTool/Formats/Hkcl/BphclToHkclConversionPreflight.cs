using System;
using System.Collections.Generic;
using System.Linq;

namespace HKCLTool;

// Keeps the UI and writer on the same conservative conversion contract.
// A failed check means no HKCL object graph is modified.
internal sealed record BphclToHkclConversionPreflight(
    string SourceName,
    string TemplateName,
    IReadOnlyList<string> PassedChecks,
    IReadOnlyList<string> BlockingChecks,
    IReadOnlyList<string> Notes)
{
    public bool IsEligible => BlockingChecks.Count == 0;

    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            "BPHCL -> HKCL conversion preflight",
            string.Empty,
            $"Source: {SourceName}",
            $"HKCL template to replace: {TemplateName}",
            string.Empty
        };

        lines.AddRange(PassedChecks.Select(check => "OK: " + check));
        lines.AddRange(Notes.Select(note => "Info: " + note));
        lines.AddRange(BlockingChecks.Select(check => "Blocked: " + check));
        lines.Add(string.Empty);
        lines.Add(IsEligible
            ? "Ready: the selected HKCL template will be replaced in place. Its cloth index and internal name remain unchanged."
            : "Conversion is blocked: choose an HKCL template with the required shell layout. No file data has been changed.");
        return string.Join(Environment.NewLine, lines);
    }
}
