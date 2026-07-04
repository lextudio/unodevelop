using System;
using System.Collections.Generic;
using System.Linq;

namespace UnoDevelop.AddIns.Analysis.CodeCoverage;

public sealed class CodeCoverageSession
{
    public CodeCoverageSession(string title, IReadOnlyList<ICSharpCode.CodeCoverage.CodeCoverageResults> results, IReadOnlyList<string> logLines)
    {
        Title = title;
        Results = results;
        LogLines = logLines;
        Created = DateTime.Now;
    }

    public static CodeCoverageSession Empty { get; } = new("No coverage loaded", Array.Empty<ICSharpCode.CodeCoverage.CodeCoverageResults>(), Array.Empty<string>());

    public string Title { get; }
    public IReadOnlyList<ICSharpCode.CodeCoverage.CodeCoverageResults> Results { get; }
    public IReadOnlyList<string> LogLines { get; }
    public DateTime Created { get; }

    public int Modules => Results.Sum(result => result.Modules.Count);
    public int Methods => Results.SelectMany(result => result.Modules).Sum(module => module.Methods.Count);
    public int VisitedSequencePoints => Results.SelectMany(result => result.Modules).SelectMany(module => module.Methods).Sum(method => method.VisitedSequencePointsCount);
    public int SequencePoints => Results.SelectMany(result => result.Modules).SelectMany(module => module.Methods).Sum(method => method.SequencePointsCount);

    public decimal CoveragePercent => SequencePoints == 0
        ? 0
        : decimal.Round((decimal)VisitedSequencePoints * 100 / SequencePoints, 1);
}
