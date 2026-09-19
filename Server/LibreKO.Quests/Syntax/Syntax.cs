using LibreKO.Quests.Binding;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Syntax;

public abstract record SyntaxNode(TextSpan Span);

public sealed record EventReferenceSyntax(TextSpan Span, string Name) : SyntaxNode(Span)
{
    public override string ToString() => Name;
}

public sealed record IncludeSyntax(TextSpan Span, string Path, TextSpan PathSpan) : SyntaxNode(Span);

public sealed record LocationPayloadSyntax(
    string Title,
    TextSpan TitleSpan,
    string? Where,
    TextSpan WhereSpan,
    long? X,
    long? Y,
    long? Zone,
    string? About = null,
    TextSpan AboutSpan = default,
    long? ElMoradX = null,
    long? ElMoradY = null) : SyntaxNode(TitleSpan);

public sealed record DirectiveSyntax(
    TextSpan Span,
    SlotKind Kind,
    long Value,
    TextSpan ValueSpan,
    IReadOnlyList<(long Value, TextSpan Span)>? Extra = null) : SyntaxNode(Span);

public sealed record DeclarationSyntax(
    TextSpan Span,
    SlotKind Kind,
    string Name,
    TextSpan NameSpan,
    long? Value,
    TextSpan ValueSpan,
    LocationPayloadSyntax? Location = null,
    string? SharedFrom = null) : SyntaxNode(Span);

public abstract record ConditionSyntax(TextSpan Span) : SyntaxNode(Span)
{
    public sealed record Or(TextSpan Span, ConditionSyntax Left, ConditionSyntax Right) : ConditionSyntax(Span);

    public sealed record And(TextSpan Span, ConditionSyntax Left, ConditionSyntax Right) : ConditionSyntax(Span);

    public sealed record Not(TextSpan Span, ConditionSyntax Operand) : ConditionSyntax(Span);

    public sealed record Predicate(TextSpan Span, ConditionSignature Signature, PhraseMatch Match)
        : ConditionSyntax(Span);

    public sealed record Invalid(TextSpan Span) : ConditionSyntax(Span);
}

public abstract record StatementSyntax(TextSpan Span) : SyntaxNode(Span)
{
    public sealed record Action(TextSpan Span, ActionSignature Signature, PhraseMatch Match)
        : StatementSyntax(Span);

    public sealed record Reward(
        TextSpan Span,
        IReadOnlyList<StatementSyntax> Body,
        IReadOnlyList<StatementSyntax>? ElseBody = null) : StatementSyntax(Span);

    public sealed record ButtonBlock(
        TextSpan Span,
        TextSpan HeaderSpan,
        ActionSignature Signature,
        PhraseMatch Match,
        IReadOnlyList<StatementSyntax> Body) : StatementSyntax(Span);

    public sealed record If(
        TextSpan Span,
        IReadOnlyList<(ConditionSyntax Condition, IReadOnlyList<StatementSyntax> Body)> Arms,
        IReadOnlyList<StatementSyntax>? ElseBody) : StatementSyntax(Span);

    public sealed record Switch(
        TextSpan Span,
        SwitchSelectorKind Selector,
        TextSpan SelectorSpan,
        IReadOnlyList<CaseClauseSyntax> Cases,
        IReadOnlyList<StatementSyntax>? DefaultBody,
        long Max = 0) : StatementSyntax(Span);

    public sealed record Choice(TextSpan Span, IReadOnlyList<StatementSyntax> Body) : StatementSyntax(Span);
}

public sealed record CaseLabelSyntax(TextSpan Span, string? Name, long? Number, long? Last = null) : SyntaxNode(Span);

public sealed record CaseClauseSyntax(
    TextSpan Span,
    IReadOnlyList<CaseLabelSyntax> Labels,
    IReadOnlyList<StatementSyntax> Body) : SyntaxNode(Span);

public sealed record EventHandlerSyntax(
    TextSpan Span,
    TextSpan HeaderSpan,
    IReadOnlyList<EventReferenceSyntax> Events,
    IReadOnlyList<StatementSyntax> Body,
    long? QuestId = null,
    TextSpan QuestSpan = default) : SyntaxNode(Span);

public sealed record KillGroupSyntax(
    TextSpan Span,
    long Count,
    TextSpan CountSpan,
    IReadOnlyList<Token> Monsters,
    Token? Target = null) : SyntaxNode(Span);

public sealed record CollectSyntax(
    TextSpan Span,
    long Count,
    TextSpan CountSpan,
    Token? Item,
    Token? ClassGroup = null) : SyntaxNode(Span);

public sealed record GrantSyntax(
    TextSpan Span,
    long Count,
    TextSpan CountSpan,
    Token Item) : SyntaxNode(Span);

public sealed record NationTextSyntax(Token Nation, Token Text);

public sealed record QuestObjectivesSyntax(
    TextSpan Span,
    long QuestId,
    TextSpan QuestSpan,
    IReadOnlyList<KillGroupSyntax> Groups,
    bool AnyWillDo = false,
    Token? Title = null,
    Token? Journal = null,
    bool Daily = false,
    IReadOnlyList<NationTextSyntax>? Journals = null,
    IReadOnlyList<CollectSyntax>? Collects = null,
    IReadOnlyList<GrantSyntax>? Grants = null,
    bool Repeat = false) : SyntaxNode(Span);

public sealed record RewardRowSyntax(
    TextSpan Span,
    IReadOnlyList<Token> Items,
    IReadOnlyList<Token>? Weights,
    IReadOnlyList<long>? Counts = null) : SyntaxNode(Span);

public sealed record RewardDefinitionSyntax(
    TextSpan Span,
    Token Name,
    IReadOnlyList<Token>? Weights,
    IReadOnlyList<RewardRowSyntax> Rows) : SyntaxNode(Span);

public sealed record BindingSyntax(TextSpan Span, IReadOnlyList<Token> Npcs, Token? Zone,
    Token? Nation, Token? ClassGroup = null);

public sealed record QuestRewardsSyntax(
    TextSpan Span,
    IReadOnlyList<StatementSyntax> Body,
    Token? ClassGroup = null);

public sealed record QuestFileSyntax(
    TextSpan Span,
    SourceText Source,
    IReadOnlyList<DeclarationSyntax> Declarations,
    IReadOnlyList<EventHandlerSyntax> Handlers,
    IReadOnlyList<DirectiveSyntax> Directives,
    IReadOnlyList<IncludeSyntax> Includes,
    IReadOnlyList<QuestObjectivesSyntax> Objectives,
    IReadOnlyList<RewardDefinitionSyntax> Rewards,
    bool HasBinding = false,
    ConditionSyntax? Requires = null,
    IReadOnlyList<QuestRewardsSyntax>? QuestRewards = null, bool AutoAccept = false, bool AutoComplete = false,
    IReadOnlyList<BindingSyntax>? Bindings = null) : SyntaxNode(Span);
