using LibreKO.Quests.Binding;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Syntax;
using LibreKO.Quests.Text;

namespace LibreKO.Quests;

public sealed class QuestCompilation
{
    public SourceText Source { get; }
    public QuestFileSyntax Syntax { get; }
    public QuestProgram Program { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public IReadOnlyList<ResolvedId> ResolvedIds { get; }
    public IReadOnlyList<NamedReference> NamedReferences { get; }
    public IReadOnlyList<TranslatableText> Translatable { get; }
    public IReadOnlyList<BorrowedEvent> Borrowed { get; }
    public IReadOnlyDictionary<int, string> QuestAliases { get; }
    public IReadOnlyDictionary<int, string> QuestTitles { get; }
    public IQuestCatalog Catalog { get; }

    public bool Succeeded => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    private QuestCompilation(
        SourceText source,
        QuestFileSyntax syntax,
        QuestProgram program,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<ResolvedId> resolvedIds,
        IReadOnlyList<NamedReference> namedReferences,
        IReadOnlyList<TranslatableText> translatable,
        IReadOnlyList<BorrowedEvent> borrowed,
        IReadOnlyDictionary<int, string> questAliases,
        IQuestCatalog catalog)
    {
        Source = source;
        Syntax = syntax;
        Program = program;
        Diagnostics = diagnostics;
        ResolvedIds = resolvedIds;
        NamedReferences = namedReferences;
        Translatable = translatable;
        Borrowed = borrowed;
        QuestAliases = questAliases;
        QuestTitles = program.Texts
            .Where(text => text.Title is { Length: > 0 })
            .GroupBy(text => text.QuestId)
            .ToDictionary(group => group.Key, group => group.First().Title!);
        Catalog = catalog;
    }

    public static QuestCompilation Create(
        string text,
        string fileName,
        IQuestCatalog? catalog = null,
        IQuestIncludes? includes = null)
    {
        catalog ??= NullQuestCatalog.Instance;
        includes ??= NoQuestIncludes.Instance;
        var source = new SourceText(text, fileName);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).Tokenize();
        var syntax = new Parser(source, tokens, diagnostics).Parse();
        var binder = new Binder(syntax, catalog, diagnostics);
        ImportIncludes(syntax, includes, binder, diagnostics, [fileName], null);
        var program = binder.Bind();
        var items = diagnostics.Items
            .Select(d => d with { FileName = fileName })
            .OrderBy(d => d.Span.Start)
            .ToArray();
        return new QuestCompilation(
            source, syntax, program, items, binder.ResolvedIds, binder.NamedReferences,
            binder.Translatable, binder.Borrowed, binder.QuestAliases, catalog);
    }

    private static void ImportIncludes(
        QuestFileSyntax file,
        IQuestIncludes includes,
        Binder binder,
        DiagnosticBag diagnostics,
        List<string> openFiles,
        TextSpan? blame)
    {
        foreach (var include in file.Includes)
        {
            var at = blame ?? include.PathSpan;
            if (!includes.TryRead(include.Path, out var text, out var name))
            {
                diagnostics.Error(DiagnosticId.IncludeNotFound, at,
                    $"There is no quest file at \"{include.Path}\".");
                continue;
            }

            if (openFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Error(DiagnosticId.IncludeCycle, at,
                    $"\"{include.Path}\" includes itself: {string.Join(" -> ", openFiles)} -> {name}.");
                continue;
            }

            var nested = new SourceText(text, name);
            var nestedDiagnostics = new DiagnosticBag();
            var nestedTokens = new Lexer(nested, nestedDiagnostics).Tokenize();
            var nestedSyntax = new Parser(nested, nestedTokens, nestedDiagnostics).Parse();

            foreach (var problem in nestedDiagnostics.Items)
            {
                if (problem.Severity != DiagnosticSeverity.Error)
                    continue;
                diagnostics.Error(DiagnosticId.IncludeNotFound, at,
                    $"{name} does not compile: {problem.Message}",
                    $"Open {name} and fix it there.");
                break;
            }

            if (nestedSyntax.Handlers.Count > 0)
                diagnostics.Warning(DiagnosticId.IncludeNotFound, at,
                    $"{name} answers events of its own; only its names are imported.");

            openFiles.Add(name);
            ImportIncludes(nestedSyntax, includes.ForFile(name), binder, diagnostics, openFiles, at);
            binder.ImportRewards(nestedSyntax.Rewards, name);
            binder.Import(nestedSyntax.Declarations, name, at);
            openFiles.RemoveAt(openFiles.Count - 1);
        }
    }

    public string? NameFor(ResolvedId id) => id.Kind switch
    {
        SlotKind.ItemId => Catalog.ItemName(id.Value),
        SlotKind.NpcId => Catalog.NpcName(id.Value),
        SlotKind.ZoneId => Catalog.ZoneName(id.Value),
        SlotKind.MapId => Catalog.MapName(id.Value),
        SlotKind.ExchangeId => Catalog.ExchangeSummary(id.Value),
        SlotKind.QuestId => QuestAliases.TryGetValue((int)id.Value, out var alias) ? alias
            : QuestTitles.TryGetValue((int)id.Value, out var title) ? title
            : Catalog.QuestTitle(id.Value) ?? Catalog.QuestSummary(id.Value),
        SlotKind.TalkTextId => Catalog.TalkText(id.Value),
        SlotKind.MenuTextId => Catalog.MenuText(id.Value),
        _ => null,
    };

    public string Annotate(int maxLength = 48)
    {
        var edits = new List<(int Start, int End, string Text, bool Replace)>();

        foreach (var id in ResolvedIds)
        {
            var name = NameFor(id);
            if (name is not null and { Length: > 0 })
                edits.Add((id.Span.Start, id.Span.End, Shorten(name, maxLength), true));
        }

        foreach (var reference in NamedReferences)
        {
            if (reference.Detail is not null and { Length: > 0 })
                edits.Add((reference.Span.Start, reference.Span.End,
                    Shorten(reference.Detail, maxLength), false));
        }

        edits.Sort((left, right) => left.Start.CompareTo(right.Start));

        var builder = new System.Text.StringBuilder();
        var position = 0;
        foreach (var (spanStart, spanEnd, text, replace) in edits)
        {
            if (spanStart < position || spanEnd > Source.Content.Length)
                continue;

            builder.Append(Source.Content, position, spanStart - position);
            if (replace)
            {
                builder.Append('\u2039').Append(text).Append('\u203a');
            }
            else
            {
                builder.Append(Source.Content, spanStart, spanEnd - spanStart);
                builder.Append('\u27e8').Append(text).Append('\u27e9');
            }
            position = spanEnd;
        }

        builder.Append(Source.Content, position, Source.Content.Length - position);
        return builder.ToString();
    }

    private static string Shorten(string value, int maxLength)
    {
        var shown = value.Replace("\n", " ").Trim();
        return shown.Length > maxLength ? shown[..(maxLength - 1)] + "\u2026" : shown;
    }

    public static QuestCompilation CreateFromFile(
        string path, IQuestCatalog? catalog = null, IQuestIncludes? includes = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        includes ??= directory is { Length: > 0 }
            ? new DirectoryQuestIncludes(directory)
            : NoQuestIncludes.Instance;
        return Create(File.ReadAllText(path), Path.GetFileName(path), catalog, includes);
    }

    public string RenderDiagnostics() =>
        string.Concat(Diagnostics.Select(d => DiagnosticFormatter.Render(d, Source)));
}
