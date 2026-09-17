namespace LibreKO.Quests.Text;

public enum DiagnosticSeverity
{
    Hidden,
    Information,
    Warning,
    Error,
}

public static class DiagnosticId
{
    public const string UnexpectedCharacter = "KQ0001";
    public const string UnexpectedToken = "KQ0002";
    public const string ExpectedEnd = "KQ0003";
    public const string UnknownDirective = "KQ0004";
    public const string NumberOutOfRange = "KQ0005";
    public const string DeclarationAfterHandler = "KQ0007";
    public const string InlineIdNotAllowed = "KQ0008";
    public const string UndeclaredName = "KQ0009";
    public const string WrongDeclarationKind = "KQ0010";
    public const string UnusedDeclaration = "KQ0011";
    public const string ExpectedIndentedBlock = "KQ0006";

    public const string UnknownAction = "KQ1001";
    public const string UnknownCondition = "KQ1002";
    public const string BadArgumentCount = "KQ1003";
    public const string UnknownEnumMember = "KQ1004";
    public const string DuplicateEvent = "KQ1005";
    public const string UnknownEventTarget = "KQ1006";
    public const string ButtonWithoutMessage = "KQ1007";
    public const string TooManyButtons = "KQ1008";
    public const string MissingNpcDeclaration = "KQ1009";
    public const string DuplicateCaseLabel = "KQ1010";
    public const string CaseTypeMismatch = "KQ1011";
    public const string UnreachableEvent = "KQ1012";
    public const string EmptyBlock = "KQ1013";
    public const string UnknownConstant = "KQ1014";
    public const string DuplicateConstant = "KQ1015";
    public const string MessageWithoutButtons = "KQ1016";
    public const string RedundantColon = "KQ1023";
    public const string UnusedQuestScope = "KQ1024";
    public const string NoGreeting = "KQ1029";
    public const string SpaceMismatch = "KQ1030";
    public const string EntryWithoutQuest = "KQ1031";
    public const string StubEvent = "KQ1032";
    public const string UnfinishedStatement = "KQ1033";
    public const string TooManyKillGroups = "KQ1034";
    public const string DuplicateObjectives = "KQ1035";
    public const string IncludeNotFound = "KQ1025";
    public const string IncludeCycle = "KQ1026";
    public const string NameFromAnotherFile = "KQ1027";
    public const string WrongNameKind = "KQ1028";
    public const string MissingNpc = "KQ1019";
    public const string DuplicateDirective = "KQ1020";
    public const string QuestNotAtNpc = "KQ1021";
    public const string DialogWithoutQuest = "KQ1022";
    public const string UnverifiedDialogFlag = "KQ1017";
    public const string MisplacedFallback = "KQ1018";
    public const string RepeatedCondition = "KQ1036";
    public const string SplitTransaction = "KQ1037";
    public const string RewardBeforeItIsPaid = "KQ1038";
    public const string RepeatedTransfer = "KQ1039";

    public const string UnknownItem = "KQ2001";
    public const string UnknownExchange = "KQ2002";
    public const string UnknownNpc = "KQ2004";
    public const string UnknownTalkText = "KQ2005";
    public const string UnknownMenuText = "KQ2006";
    public const string UnknownZone = "KQ2007";
    public const string ActionNotImplemented = "KQ2008";
    public const string EmptyDialogText = "KQ2009";
    public const string IterationNotImplemented = "KQ2010";
    public const string BadIndent = "KQ0012";
    public const string MixedIndent = "KQ0013";
    public const string ExpectedColon = "KQ0014";
}

public sealed record Diagnostic(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    TextSpan Span,
    string? FileName = null,
    string? Hint = null)
{
    public override string ToString() => $"{Id}: {Message}";
}

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;
    public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Report(Diagnostic diagnostic) => _items.Add(diagnostic);

    public void Error(string id, TextSpan span, string message, string? hint = null) =>
        _items.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, Hint: hint));

    public void Warning(string id, TextSpan span, string message, string? hint = null) =>
        _items.Add(new Diagnostic(id, DiagnosticSeverity.Warning, message, span, Hint: hint));

    public void Info(string id, TextSpan span, string message, string? hint = null) =>
        _items.Add(new Diagnostic(id, DiagnosticSeverity.Information, message, span, Hint: hint));

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);
}

public static class DiagnosticFormatter
{
    public static string Render(Diagnostic diagnostic, SourceText source)
    {
        var start = source.GetLinePosition(diagnostic.Span.Start);
        var lineText = source.GetLineText(start.Line);
        var caretPad = new string(' ', start.Character);
        var caretLength = Math.Max(1, Math.Min(diagnostic.Span.Length, lineText.Length - start.Character));
        var caret = new string('^', caretLength);
        var label = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Information => "info",
            _ => "hidden",
        };

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{source.FileName}({start.Line + 1},{start.Character + 1}): {label} {diagnostic.Id}: {diagnostic.Message}");
        builder.AppendLine($"  {lineText}");
        builder.AppendLine($"  {caretPad}{caret}");
        if (diagnostic.Hint is { Length: > 0 })
            builder.AppendLine($"  hint: {diagnostic.Hint}");
        return builder.ToString();
    }
}
