using LibreKO.Quests.Binding;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Syntax;

public sealed class Parser
{
    private readonly SourceText _source;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<Line> _lines;
    private int _index;
    private bool _hasBinding;

    private sealed record Line(IReadOnlyList<Token> Tokens, TextSpan Span, int Indent)
    {
        public Token First => Tokens[0];

        public bool StartsWith(params string[] words)
        {
            if (Tokens.Count < words.Length)
                return false;
            for (var i = 0; i < words.Length; i++)
            {
                if (!Tokens[i].IsWord(words[i]))
                    return false;
            }
            return true;
        }
    }

    public Parser(SourceText source, IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        _source = source;
        _diagnostics = diagnostics;
        _lines = SplitLines(tokens);
        CheckIndentCharacters();
    }

    public QuestFileSyntax Parse()
    {
        var declarations = new List<DeclarationSyntax>();
        var handlers = new List<EventHandlerSyntax>();
        var directives = new List<DirectiveSyntax>();
        var objectiveBlocks = new List<QuestObjectivesSyntax>();
        var includes = new List<IncludeSyntax>();
        var rewards = new List<RewardDefinitionSyntax>();
        var bindings = new List<BindingSyntax>();
        bool autoAccept = false;
        ConditionSyntax? requires = null;
        List<QuestRewardsSyntax>? questRewards = null;

        while (!AtEnd)
        {
            var line = Current;

            if (line.Indent > 0)
            {
                _diagnostics.Error(DiagnosticId.BadIndent, line.Span,
                    "This line is indented but nothing opens a block above it.");
                _index++;
                continue;
            }

            if (line.Tokens.Count > 1
                && line.First.Kind is TokenKind.Word or TokenKind.String
                && line.Tokens[1].Text == "=")
            {
                var declaration = ParseDeclaration(line);
                if (declaration is not null)
                {
                    if (handlers.Count > 0)
                        _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, declaration.Span,
                            "Names belong at the top of the file, above the first 'On'.");
                    declarations.Add(declaration);
                }
                _index++;
                continue;
            }

            if (line.StartsWith("rewardpool") || line.StartsWith("rewardtable"))
            {
                var definition = ParseRewardDefinition(line);
                if (definition is not null)
                {
                    if (handlers.Count > 0)
                        _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, definition.Span,
                            "Rewards belong at the top of the file, above the first 'On'.");
                    rewards.Add(definition);
                }
                continue;
            }

            if (line.StartsWith("include"))
            {
                var include = ParseInclude(line);
                if (include is not null)
                {
                    if (handlers.Count > 0 || _hasBinding || declarations.Count > 0)
                        _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, include.Span,
                            "Includes belong at the top of the file, above Bind and On.");
                    includes.Add(include);
                }
                _index++;
                continue;
            }

            if (line.StartsWith("bind"))
            {
                if (handlers.Count > 0)
                    _diagnostics.Error(DiagnosticId.DuplicateDirective, line.Span,
                        "Write every Bind line above the handlers.");
                var first = !_hasBinding;
                _hasBinding = true;
                var npcTokens = new List<Token>();
                Token? zoneToken = null;
                Token? nationToken = null;
                var position = 1;
                while (position + 1 < line.Tokens.Count)
                {
                    var kind = line.Tokens[position].IsWord("npc") ? SlotKind.NpcId
                        : line.Tokens[position].IsWord("zone") ? SlotKind.ZoneId : SlotKind.Int;
                    var value = line.Tokens[position + 1];
                    if (kind == SlotKind.Int || value.Kind != TokenKind.Number || value.Value is <= 0 or > int.MaxValue)
                        break;
                    if (kind == SlotKind.ZoneId) zoneToken = value; else npcTokens.Add(value);
                    if (first)
                        directives.Add(new DirectiveSyntax(line.Span, kind, value.Value, value.Span));
                    position += 2;
                }
                Token? bindClassToken = null;
                if (position + 1 < line.Tokens.Count && line.Tokens[position].IsWord("for"))
                {
                    position++;
                    while (position < line.Tokens.Count && line.Tokens[position].Kind == TokenKind.Word)
                    {
                        if (Binding.QuestVocabulary.ClassGroups.ContainsKey(line.Tokens[position].Text))
                            bindClassToken = line.Tokens[position];
                        else
                            nationToken = line.Tokens[position];
                        position++;
                    }
                }
                if (position == 1 || position != line.Tokens.Count)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                        "Bind takes Npc <id>, Zone <id>, or Npc <id> Zone <id>, "
                        + "then an optional \"for <nation>\", \"for <class>\" or \"for <nation> <class>\".");
                bindings.Add(new BindingSyntax(line.Span, npcTokens, zoneToken, nationToken, bindClassToken));
                _index++;
                continue;
            }

            if (line.StartsWith("npc") || line.StartsWith("zone"))
            {
                _diagnostics.Error(DiagnosticId.UnknownDirective, line.Span,
                    "A file binds with 'Bind', as in 'Bind Npc 16079 Zone 21'.");
                _index++;
                continue;
            }

            if (line.StartsWith("auto", "accept"))
            {
                if (autoAccept || line.Tokens.Count != 2 || handlers.Count > 0)
                    _diagnostics.Error(DiagnosticId.UnknownDirective, line.Span, "Write Auto accept once above the handlers.");
                autoAccept = true;
                _index++;
                continue;
            }

            if (line.StartsWith("rewards"))
            {
                if (handlers.Count > 0)
                    _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, line.Span,
                        "Rewards belongs above the first On handler.");
                Token? rewardClass = null;
                if (line.Tokens.Count >= 3 && line.Tokens[1].IsWord("for")
                    && Binding.QuestVocabulary.ClassGroups.ContainsKey(line.Tokens[2].Text))
                    rewardClass = line.Tokens[2];
                if (questRewards is not null
                    && (rewardClass is not { } wanted || questRewards.Any(r => r.ClassGroup is not { } had
                        || had.Text.Equals(wanted.Text, StringComparison.OrdinalIgnoreCase))))
                    _diagnostics.Error(DiagnosticId.DuplicateDirective, line.Span,
                        "This quest already has Rewards for that class.");
                questRewards ??= [];
                if (line.StartsWith("rewards", "none"))
                {
                    ExpectEndOfLine(line, 2);
                    questRewards.Add(new QuestRewardsSyntax(line.Span, []));
                    _index++;
                }
                else
                {
                    var position = rewardClass is null ? 1 : 3;
                    questRewards.Add(new QuestRewardsSyntax(line.Span,
                        ParseIndentedBlock(line, ref position), rewardClass));
                }
                continue;
            }

            if (line.StartsWith("requires"))
            {
                if (handlers.Count > 0)
                    _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, line.Span,
                        "Requires belongs above the first On handler.");
                if (requires is not null)
                    _diagnostics.Error(DiagnosticId.DuplicateDirective, line.Span,
                        "This quest already has Requires; combine conditions with 'and' or 'or'.");
                var position = 1;
                requires = ParseCondition(line, ref position);
                if (position != line.Tokens.Count)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                        "Requires takes one boolean condition.");
                _index++;
                continue;
            }

            if (line.StartsWith("quest"))
            {
                if (handlers.Count > 0)
                    _diagnostics.Error(DiagnosticId.DeclarationAfterHandler, line.Span,
                        "Quest belongs above the first On handler.");
                var objectives = ParseObjectives();
                if (objectives is not null)
                    objectiveBlocks.Add(objectives);
                continue;
            }

            if (line.StartsWith("on"))
            {
                handlers.Add(ParseHandler());
                continue;
            }

            _diagnostics.Error(DiagnosticId.UnknownDirective, line.Span,
                $"Expected a name or 'On', found {line.First}.",
                "A quest file names the events other files enter, then answers each one with 'On \"name\":'.");
            _index++;
        }

        var span = _lines.Count == 0
            ? new TextSpan(0, 0)
            : _lines[0].Span.Union(_lines[^1].Span);
        return new QuestFileSyntax(
            span, _source, declarations, handlers, directives, includes, objectiveBlocks, rewards, _hasBinding,
            requires, questRewards, autoAccept, bindings);
    }

    private void CheckIndentCharacters()
    {
        char? unit = null;
        foreach (var parsed in _lines)
        {
            var line = _source.GetLineIndex(parsed.First.Span.Start);
            var text = _source.GetLineText(line);
            var leading = text.Length - text.TrimStart(' ', '\t').Length;
            if (leading == 0)
                continue;

            var prefix = text[..leading];
            var hasTab = prefix.Contains('\t');
            var hasSpace = prefix.Contains(' ');

            if (hasTab && hasSpace)
            {
                _diagnostics.Error(DiagnosticId.MixedIndent, LineSpan(line, leading),
                    "This line indents with both tabs and spaces.");
                continue;
            }

            var seen = hasTab ? '\t' : ' ';
            if (unit is null)
            {
                unit = seen;
                continue;
            }

            if (unit != seen)
                _diagnostics.Error(DiagnosticId.MixedIndent, LineSpan(line, leading),
                    unit == ' '
                        ? "This line indents with tabs; the rest of the file uses spaces."
                        : "This line indents with spaces; the rest of the file uses tabs.");
        }
    }

    private TextSpan LineSpan(int line, int length) =>
        new(_source.GetPosition(new LinePosition(line, 0)), Math.Max(1, length));

    private IncludeSyntax? ParseInclude(Line line)
    {
        var keyword = line.First;
        var start = keyword.Span.End;
        var end = line.Span.End;
        if (start >= end)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "'include' takes a path, as in 'include locations/moradon'.");
            return null;
        }

        var raw = _source.Content[start..end];
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "'include' takes a path, as in 'include locations/moradon'.");
            return null;
        }

        var offset = start + raw.IndexOf(trimmed[0]);
        var path = trimmed.Trim('"');
        return new IncludeSyntax(line.Span, path, new TextSpan(offset, trimmed.Length));
    }



    private RewardDefinitionSyntax? ParseRewardDefinition(Line opener)
    {
        var isTable = opener.First.IsWord("rewardtable");
        if (opener.Tokens.Count != 2 || opener.Tokens[1].Kind != TokenKind.Word)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, opener.Span,
                $"'{opener.First.Text}' takes a name, as in '{opener.First.Text} forgotten_shrine'.");
            _index++;
            return null;
        }

        var name = opener.Tokens[1];
        _index++;
        var rows = new List<RewardRowSyntax>();
        IReadOnlyList<Token>? shared = null;

        while (!AtEnd && Current.Indent > opener.Indent)
        {
            var line = Current;
            if (line.First.IsWord("weights"))
            {
                var values = Numbers(line, 1);
                if (rows.Count > 0)
                    rows[^1] = rows[^1] with { Weights = values };
                else if (shared is not null)
                    _diagnostics.Error(DiagnosticId.DuplicateDirective, line.Span,
                        "This reward already has a 'Weights' line.");
                else
                    shared = values;
                _index++;
                continue;
            }

            if (isTable && line.First.IsWord("row"))
            {
                var split = line.Tokens.Count;
                for (var at = 1; at < line.Tokens.Count; at++)
                {
                    if (line.Tokens[at].IsWord("weights"))
                    {
                        split = at;
                        break;
                    }
                }
                var items = Names(line with { Tokens = [.. line.Tokens.Take(split)] }, 1);
                var inline = split == line.Tokens.Count ? null : Numbers(line, split + 1);
                rows.Add(new RewardRowSyntax(line.Span, items, inline));
                _index++;
                continue;
            }

            if (!isTable && ParsePoolEntry(line) is { } entry)
            {
                rows.Add(entry);
                _index++;
                continue;
            }

            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span, isTable
                ? "A RewardTable holds 'Weights' and 'Row' lines."
                : "A RewardPool holds '<weight> <item>' lines.");
            _index++;
        }

        if (rows.Count == 0)
        {
            _diagnostics.Error(DiagnosticId.EmptyBlock, opener.Span,
                $"'{opener.First.Text} {name.Text}' lists nothing to give.");
            return null;
        }

        if (!isTable)
        {
            var flattened = new RewardRowSyntax(opener.Span,
                [.. rows.Select(row => row.Items[0])],
                [.. rows.Select(row => row.Weights![0])],
                [.. rows.Select(row => row.Counts![0])]);
            return new RewardDefinitionSyntax(opener.Span, name, null, [flattened]);
        }

        return new RewardDefinitionSyntax(opener.Span, name, shared, rows);
    }

    private RewardRowSyntax? ParsePoolEntry(Line line)
    {
        var at = 0;
        long count = 1;
        if (line.Tokens.Count > 2 && line.First.Kind == TokenKind.Number && line.Tokens[1].IsWord("of"))
        {
            count = line.First.Value;
            at = 2;
        }
        if (at >= line.Tokens.Count || line.Tokens[at].Kind is not (TokenKind.Number or TokenKind.Word))
            return null;

        var item = line.Tokens[at];
        at++;
        var weight = new Token(TokenKind.Number, "1", item.Span, 1);
        if (at < line.Tokens.Count && line.Tokens[at].IsWord("weight"))
        {
            if (at + 1 >= line.Tokens.Count || line.Tokens[at + 1].Kind != TokenKind.Number)
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                    "A weight is a whole number, as in '810231000 weight 2'.");
                return null;
            }
            weight = line.Tokens[at + 1];
            at += 2;
        }
        return at == line.Tokens.Count
            ? new RewardRowSyntax(line.Span, [item], [weight], [count])
            : null;
    }

    private IReadOnlyList<Token> Numbers(Line line, int position)
    {
        var values = new List<Token>();
        for (var index = position; index < line.Tokens.Count; index++)
        {
            if (line.Tokens[index].Kind == TokenKind.Number)
                values.Add(line.Tokens[index]);
            else if (line.Tokens[index].Kind != TokenKind.Comma)
                _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Tokens[index].Span,
                    "Weights are whole numbers.");
        }
        return values;
    }

    private IReadOnlyList<Token> Names(Line line, int position)
    {
        var values = new List<Token>();
        for (var index = position; index < line.Tokens.Count; index++)
        {
            if (line.Tokens[index].Kind is TokenKind.Number or TokenKind.Word)
                values.Add(line.Tokens[index]);
            else if (line.Tokens[index].Kind != TokenKind.Comma)
                _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Tokens[index].Span,
                    "A row lists item ids or named items.");
        }
        return values;
    }

    private DeclarationSyntax? ParseDeclaration(Line line)
    {
        var nameToken = line.First;
        if (nameToken.Kind == TokenKind.String)
            _diagnostics.Error(DiagnosticId.UnexpectedToken, nameToken.Span,
                "A name is written without quotes, as in chaos_3_offer = event 1205.",
                "Quotes always mean literal text, so a quoted name would be ambiguous.");
        if (nameToken.Text.Trim().Length == 0)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, nameToken.Span, "A name cannot be empty.");
            return null;
        }

        if (line.Tokens.Count < 4 || line.Tokens[1].Text != "=")
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                $"A name line reads {nameToken} = event 1205, or {nameToken} = quest 61.");
            return null;
        }

        var kindToken = line.Tokens[2];
        if (kindToken.IsWord("event") && line.Tokens[3].IsWord("from"))
        {
            var start = line.Tokens[3].Span.End;
            var raw = _source.Content[start..line.Span.End];
            var trimmed = raw.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                    $"'{nameToken.Text} = event from' names the file that answers it, "
                    + "as in 'event_240 = event from 11610_0_0'.");
                return null;
            }
            var at = start + raw.IndexOf(trimmed[0]);
            return new DeclarationSyntax(
                line.Span, SlotKind.EventRef, nameToken.Text, nameToken.Span,
                null, new TextSpan(at, trimmed.Length), null, trimmed);
        }

        SlotKind kind;
        if (kindToken.IsWord("event"))
            kind = SlotKind.EventRef;
        else if (kindToken.IsWord("quest"))
            kind = SlotKind.QuestId;
        else if (kindToken.IsWord("item"))
            kind = SlotKind.ItemId;
        else if (kindToken.IsWord("npc"))
            kind = SlotKind.NpcId;
        else if (kindToken.IsWord("location"))
            kind = SlotKind.MapId;
        else if (kindToken.IsWord("text"))
            kind = SlotKind.TalkTextId;
        else
        {
            _diagnostics.Error(DiagnosticId.UnknownDirective, kindToken.Span,
                "A name line declares an event, a quest, an item, an npc or a location.",
                "An event only this file reaches needs no name line — its 'On' block is the declaration.");
            return null;
        }

        if (kind == SlotKind.TalkTextId)
        {
            if (line.Tokens[3].Kind != TokenKind.String)
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Tokens[3].Span,
                    "Text is declared with the words in quotes.");
                return null;
            }

            ExpectEndOfLine(line, 4);
            return new DeclarationSyntax(
                line.Span, kind, nameToken.Text, nameToken.Span,
                null, line.Tokens[3].Span,
                new LocationPayloadSyntax(
                    line.Tokens[3].Text, line.Tokens[3].Span, null, default, null, null, null));
        }

        if (kind == SlotKind.MapId)
        {
            var at = 3;
            var payload = ParseLocationPayload(line, ref at);
            if (payload is null)
                return null;
            ExpectEndOfLine(line, at);
            return new DeclarationSyntax(
                line.Span, kind, nameToken.Text, nameToken.Span,
                null, payload.TitleSpan, payload);
        }

        if (line.Tokens[3].Kind != TokenKind.Number)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Tokens[3].Span,
                $"Expected an id, found {line.Tokens[3]}.");
            return null;
        }

        ExpectEndOfLine(line, 4);
        return new DeclarationSyntax(
            line.Span, kind, nameToken.Text, nameToken.Span,
            line.Tokens[3].Value, line.Tokens[3].Span);
    }

    private LocationPayloadSyntax? ParseLocationPayload(Line line, ref int position)
    {
        if (position >= line.Tokens.Count || line.Tokens[position].Kind != TokenKind.String)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "A location needs the name the player sees, in quotes.",
                "For example: \"worms\" = location \"Worm\" found \"Outside Moradon\" at 637 429 in zone 21");
            return null;
        }

        var title = line.Tokens[position];
        position++;

        Token? where = null;
        Token? about = null;
        long? x = null;
        long? y = null;
        long? elmoradX = null;
        long? elmoradY = null;
        long? zone = null;

        while (position < line.Tokens.Count)
        {
            var word = line.Tokens[position];
            if (word.IsWord("found") && position + 1 < line.Tokens.Count
                && line.Tokens[position + 1].Kind == TokenKind.String)
            {
                where = line.Tokens[position + 1];
                position += 2;
                continue;
            }

            if (word.IsWord("about") && position + 1 < line.Tokens.Count
                && line.Tokens[position + 1].Kind == TokenKind.String)
            {
                about = line.Tokens[position + 1];
                position += 2;
                continue;
            }

            Token? nation = word.IsWord("karus") || word.IsWord("elmorad") ? word : null;
            var at = nation is null ? position : position + 1;
            if (at < line.Tokens.Count && line.Tokens[at].IsWord("at") && at + 2 < line.Tokens.Count
                && line.Tokens[at + 1].Kind == TokenKind.Number
                && line.Tokens[at + 2].Kind == TokenKind.Number)
            {
                if (nation is null || nation.Value.IsWord("karus"))
                {
                    x = line.Tokens[at + 1].Value;
                    y = line.Tokens[at + 2].Value;
                }
                if (nation is null || nation.Value.IsWord("elmorad"))
                {
                    elmoradX = line.Tokens[at + 1].Value;
                    elmoradY = line.Tokens[at + 2].Value;
                }
                position = at + 3;
                continue;
            }

            if (word.IsWord("in") && position + 2 < line.Tokens.Count
                && line.Tokens[position + 1].IsWord("zone")
                && line.Tokens[position + 2].Kind == TokenKind.Number)
            {
                zone = line.Tokens[position + 2].Value;
                position += 3;
                continue;
            }

            break;
        }

        return new LocationPayloadSyntax(
            title.Text, title.Span,
            where?.Text, where?.Span ?? default,
            x, y, zone,
            about?.Text, about?.Span ?? default,
            elmoradX, elmoradY);
    }

    private QuestObjectivesSyntax? ParseObjectives()
    {
        var opener = Current;
        if (opener.Tokens.Count < 2 || opener.Tokens[1].Kind != TokenKind.Number)
        {
            _index++;
            _diagnostics.Error(DiagnosticId.UnexpectedToken, opener.Span,
                "'Quest' takes the id of the quest whose objectives follow.");
            return null;
        }

        var questId = opener.Tokens[1].Value;
        var questSpan = opener.Tokens[1].Span;
        var indent = opener.Indent;
        var anyWillDo = false;
        var position = 2;
        Token? title = null;

        if (position < opener.Tokens.Count && opener.Tokens[position].Kind == TokenKind.String)
            title = opener.Tokens[position++];

        if (position < opener.Tokens.Count)
        {
            if (opener.Tokens.Count == position + 2 && opener.Tokens[position].IsWord("needs")
                && (opener.Tokens[position + 1].IsWord("any") || opener.Tokens[position + 1].IsWord("all")))
            {
                anyWillDo = opener.Tokens[position + 1].IsWord("any");
            }
            else
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, opener.Tokens[position].Span,
                    "A 'Quest' line takes the id, then the quest's title in quotes, "
                    + "then 'needs any' or 'needs all'.");
            }
        }

        _index++;

        var groups = new List<KillGroupSyntax>();
        var collects = new List<CollectSyntax>();
        var grants = new List<GrantSyntax>();
        Token? journal = null;
        var journals = new List<NationTextSyntax>();
        var daily = false;
        var repeat = false;
        while (!AtEnd && Current.Indent > indent)
        {
            if (Current.StartsWith("daily") && Current.Tokens.Count == 1)
            {
                if (daily)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, Current.Span,
                        "This quest already has a Daily declaration.");
                daily = true;
                _index++;
                continue;
            }
            if (Current.StartsWith("repeat"))
            {
                if (Current.Tokens.Count != 2 || !Current.Tokens[1].IsWord("always"))
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, Current.Span,
                        "'Repeat' takes 'always', as in 'Repeat always'.");
                else if (repeat || daily)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, Current.Span,
                        "This quest already has a repetition declaration.");
                repeat = true;
                _index++;
                continue;
            }
            if (Current.StartsWith("journal"))
            {
                if (Current.Tokens.Count == 4 && Current.Tokens[1].IsWord("for")
                    && Current.Tokens[3].Kind == TokenKind.String)
                {
                    if (journals.Any(entry => entry.Nation.Text.Equals(Current.Tokens[2].Text,
                            StringComparison.OrdinalIgnoreCase)))
                        _diagnostics.Error(DiagnosticId.DuplicateObjectives, Current.Span,
                            "This quest already has a Journal line for that nation.");
                    journals.Add(new NationTextSyntax(Current.Tokens[2], Current.Tokens[3]));
                }
                else
                {
                    journal = ParseJournal(Current, journal);
                }
                _index++;
                continue;
            }

            if (Current.StartsWith("give"))
            {
                var granted = ParseGrant(Current);
                if (granted is not null)
                    grants.Add(granted);
                _index++;
                continue;
            }
            if (Current.StartsWith("collect"))
            {
                var collected = ParseCollect(Current);
                if (collected is not null)
                    collects.Add(collected);
                _index++;
                continue;
            }

            var group = ParseKillGroup(Current);
            if (group is not null)
                groups.Add(group);
            _index++;
        }

        if (journal is not null && journals.Count > 0)
            _diagnostics.Error(DiagnosticId.DuplicateObjectives, opener.Span,
                "Write Journal once, or once per nation, not both.");
        if (!_hasBinding && groups.Count == 0 && collects.Count == 0 && title is null
            && journal is null && journals.Count == 0)
            _diagnostics.Error(DiagnosticId.EmptyBlock, opener.Span,
                "A 'Quest' block needs a title, a 'Journal' line, a 'Kill' line or a 'Collect' line.");

        return new QuestObjectivesSyntax(
            opener.Span, questId, questSpan, groups, anyWillDo, title, journal, daily, journals,
            collects, grants, repeat);
    }

    private Token? ParseJournal(Line line, Token? already)
    {
        if (line.Tokens.Count != 2 || line.Tokens[1].Kind != TokenKind.String)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "A 'Journal' line takes the text the quest panel shows, in quotes.");
            return already;
        }

        if (already is not null)
        {
            _diagnostics.Error(DiagnosticId.DuplicateObjectives, line.Span,
                "This quest already has a 'Journal' line.");
            return already;
        }

        return line.Tokens[1];
    }

    private GrantSyntax? ParseGrant(Line line)
    {
        if (line.Tokens.Count != 6 || line.Tokens[1].Kind != TokenKind.Number
            || !line.Tokens[2].IsWord("of")
            || line.Tokens[3].Kind is not (TokenKind.Number or TokenKind.Word)
            || !line.Tokens[4].IsWord("on") || !line.Tokens[5].IsWord("accept"))
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "A 'Give' line in a 'Quest' block hands the player an item as the quest starts, "
                + "like: Give 1 of 900670000 on accept");
            return null;
        }

        return new GrantSyntax(line.Span, line.Tokens[1].Value, line.Tokens[1].Span, line.Tokens[3]);
    }

    private CollectSyntax? ParseCollect(Line line)
    {
        IReadOnlyList<Token> tokens = line.Tokens;
        Token? group = null;
        if (tokens.Count >= 3 && tokens[tokens.Count - 2].IsWord("for")
            && Binding.QuestVocabulary.ClassGroups.ContainsKey(tokens[tokens.Count - 1].Text))
        {
            group = tokens[tokens.Count - 1];
            tokens = tokens.Take(tokens.Count - 2).ToList();
        }
        var coins = tokens.Count == 3 && tokens[2].IsWord("coins");
        if (tokens.Count < 2 || tokens[1].Kind != TokenKind.Number
            || (!coins && (tokens.Count != 4 || !tokens[2].IsWord("of")
                || tokens[3].Kind is not (TokenKind.Number or TokenKind.Word))))
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "A 'Collect' line names what the quest hands in, like: Collect 3 of 810418000, "
                + "Collect 3000 coins, or Collect 1 of 810090000 for warrior");
            return null;
        }

        return new CollectSyntax(line.Span, tokens[1].Value, tokens[1].Span,
            coins ? null : tokens[3], group);
    }

    private KillGroupSyntax? ParseKillGroup(Line line)
    {
        if (!line.StartsWith("kill") || line.Tokens.Count < 4
            || line.Tokens[1].Kind != TokenKind.Number
            || !line.Tokens[2].IsWord("of"))
        {
            _diagnostics.Error(DiagnosticId.UnknownAction, line.Span,
                "A 'Quest' block takes 'Daily', 'Repeat always', 'Journal', 'Kill', 'Collect' or 'Give .. on accept' lines.");
            return null;
        }

        var monsters = new List<Token>();
        Token? target = null;
        for (var position = 3; position < line.Tokens.Count; position++)
        {
            var token = line.Tokens[position];
            if (token.Kind == TokenKind.Comma)
                continue;
            if (token.IsWord("at") && position + 1 < line.Tokens.Count
                && line.Tokens[position + 1].Kind == TokenKind.Word)
            {
                target = line.Tokens[position + 1];
                position++;
                continue;
            }
            if (token.Kind is not (TokenKind.Number or TokenKind.Word))
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, token.Span,
                    $"{token} is not a monster.");
                continue;
            }
            monsters.Add(token);
        }

        if (monsters.Count == 0)
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "This 'Kill' line names no monster.");

        return new KillGroupSyntax(line.Span, line.Tokens[1].Value, line.Tokens[1].Span, monsters, target);
    }

    private EventHandlerSyntax ParseHandler()
    {
        var header = Current;
        var events = new List<EventReferenceSyntax>();
        var position = 1;

        while (position < header.Tokens.Count
               && header.Tokens[position].Kind is TokenKind.String or TokenKind.Word
               && !header.Tokens[position].IsWord("for"))
        {
            events.Add(new EventReferenceSyntax(header.Tokens[position].Span, header.Tokens[position].Text));
            position++;
            if (position < header.Tokens.Count && header.Tokens[position].Kind == TokenKind.Comma)
            {
                position++;
                continue;
            }
            break;
        }

        if (events.Count == 0)
            _diagnostics.Error(DiagnosticId.UnexpectedToken, header.Span,
                "'On' takes the name of an event, without quotes.");

        long? questId = null;
        var questSpan = default(TextSpan);
        if (position + 2 < header.Tokens.Count
            && header.Tokens[position].IsWord("for")
            && header.Tokens[position + 1].IsWord("quest")
            && header.Tokens[position + 2].Kind == TokenKind.Number)
        {
            questId = header.Tokens[position + 2].Value;
            questSpan = header.Tokens[position + 2].Span;
            position += 3;
        }

        var body = ParseIndentedBlock(header, ref position, mayBeStub: true);
        return new EventHandlerSyntax(
            header.Span, header.Span, events, body, questId, questSpan);
    }

    private IReadOnlyList<StatementSyntax> ParseIndentedBlock(
        Line opener,
        ref int position,
        bool mayBeStub = false)
    {
        var inlineBody = TakeInlineBody(opener, ref position);
        _index++;

        if (inlineBody is not null)
            return [inlineBody];

        return ParseBlock(opener.Indent, mayBeStub, opener.Span);
    }

    private StatementSyntax? TakeInlineBody(Line line, ref int position)
    {
        if (position >= line.Tokens.Count || line.Tokens[position].Kind != TokenKind.Colon)
        {
            ExpectEndOfLine(line, position);
            return null;
        }

        var colon = line.Tokens[position];
        position++;
        if (position >= line.Tokens.Count)
        {
            _diagnostics.Error(DiagnosticId.RedundantColon, colon.Span,
                "This ':' does nothing — the indented lines below already open the body.",
                "A ':' belongs only before a body written on the same line.");
            return null;
        }

        var trailing = line.Tokens.Skip(position).ToArray();
        var inline = TextSpan.FromBounds(trailing[0].Span.Start, trailing[^1].Span.End);
        return ParseAction(new Line(trailing, inline, line.Indent));
    }

    private IReadOnlyList<StatementSyntax> ParseBlock(
        int parentIndent,
        bool mayBeStub = false,
        TextSpan? openedBy = null)
    {
        var statements = new List<StatementSyntax>();
        var blockIndent = -1;

        while (!AtEnd)
        {
            var line = Current;
            if (line.Indent <= parentIndent)
                break;

            if (blockIndent < 0)
            {
                blockIndent = line.Indent;
            }
            else if (line.Indent != blockIndent)
            {
                if (line.Indent < blockIndent)
                    break;
                _diagnostics.Error(DiagnosticId.BadIndent, line.Span,
                    $"This line is indented {line.Indent} but the block around it is indented {blockIndent}.");
                _index++;
                continue;
            }

            var statement = ParseStatement();
            if (statement is not null)
                statements.Add(statement);
        }

        if (blockIndent < 0)
        {
            var where = openedBy ?? (AtEnd ? _lines[^1].Span : Current.Span);
            if (mayBeStub)
                _diagnostics.Warning(DiagnosticId.StubEvent, where,
                    "This event is reached but does nothing yet.",
                    "The routing is in place; the body still has to be written.");
            else
                _diagnostics.Error(DiagnosticId.EmptyBlock, where,
                    "This block has no indented lines under it.");
        }

        return statements;
    }

    private StatementSyntax? ParseStatement()
    {
        var line = Current;

        if (line.StartsWith("trade"))
            return ParseTrade(line);

        if (line.StartsWith("transaction"))
        {
            var position = 1;
            var body = ParseIndentedBlock(line, ref position);
            IReadOnlyList<StatementSyntax>? elseBody = null;
            while (!AtEnd && Current.Indent == line.Indent && Current.StartsWith("else"))
            {
                var elseLine = Current;
                var elsePosition = 1;
                var branch = ParseIndentedBlock(elseLine, ref elsePosition);
                if (elseBody is not null)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, elseLine.Span,
                        "This block already has an 'Else'.");
                elseBody = branch;
            }
            return new StatementSyntax.Reward(line.Span, body, elseBody);
        }
        if (line.StartsWith("choose", "one"))
        {
            var position = 2;
            var body = ParseIndentedBlock(line, ref position);
            return new StatementSyntax.Choice(line.Span, body);
        }
        if (line.StartsWith("if"))
            return ParseIf();
        if (line.StartsWith("by"))
            return ParseSwitch();
        if (line.StartsWith("topic") || line.StartsWith("button"))
        {
            var guard = TrailingCondition(line);
            if (guard > 0)
            {
                var position = guard + 1;
                var condition = ParseCondition(line, ref position);
                _lines[_index] = line with { Tokens = [.. line.Tokens.Take(guard)] };
                var offered = ParseStatement();
                return offered is null
                    ? null
                    : new StatementSyntax.If(line.Span, [(condition, (IReadOnlyList<StatementSyntax>)[offered])], null);
            }
        }

        if ((line.StartsWith("button") || line.StartsWith("topic")) && line.Tokens[^1].IsWord("do"))
            return ParseButtonBlock();

        _index++;
        return ParseAction(line);
    }


    private static int TrailingCondition(Line line)
    {
        for (var index = line.Tokens.Count - 1; index > 0; index--)
        {
            if (line.Tokens[index].IsWord("if"))
                return index;
        }
        return 0;
    }

    private StatementSyntax? ParseButtonBlock()
    {
        var opener = Current;
        var resolved = ResolveAction(opener.Tokens, out _);
        if (resolved is null)
        {
            _index++;
            _diagnostics.Error(DiagnosticId.UnknownAction, opener.Span,
                $"'{_source.GetText(opener.Span)}' is not something an NPC can do.");
            return null;
        }

        var position = opener.Tokens.Count;
        var body = ParseIndentedBlock(opener, ref position);
        return new StatementSyntax.ButtonBlock(
            opener.Span, opener.Span, resolved.Signature, resolved.Match, body);
    }

    private StatementSyntax ParseIf()
    {
        var opener = Current;
        var position = 1;
        var condition = ParseCondition(opener, ref position);
        var arms = new List<(ConditionSyntax, IReadOnlyList<StatementSyntax>)>
        {
            (condition, ParseIndentedBlock(opener, ref position)),
        };

        IReadOnlyList<StatementSyntax>? elseBody = null;
        while (!AtEnd && Current.Indent == opener.Indent && Current.StartsWith("else"))
        {
            var elseLine = Current;
            var elsePosition = 1;

            if (elseLine.StartsWith("else", "if"))
            {
                elsePosition = 2;
                var elseCondition = ParseCondition(elseLine, ref elsePosition);
                arms.Add((elseCondition, ParseIndentedBlock(elseLine, ref elsePosition)));
                continue;
            }

            var body = ParseIndentedBlock(elseLine, ref elsePosition);
            if (elseBody is not null)
                _diagnostics.Error(DiagnosticId.UnexpectedToken, elseLine.Span, "This 'If' already has an 'Else'.");
            elseBody = body;
        }

        return new StatementSyntax.If(opener.Span, arms, elseBody);
    }

    private StatementSyntax ParseSwitch()
    {
        var opener = Current;
        SwitchSelectorKind selector;
        var position = 1;
        long max = 0;

        if (opener.StartsWith("by", "player", "class"))
        {
            selector = SwitchSelectorKind.PlayerClass;
            position = 3;
        }
        else if (opener.StartsWith("by", "player", "nation"))
        {
            selector = SwitchSelectorKind.PlayerNation;
            position = 3;
        }
        else if (opener.StartsWith("by", "selection"))
        {
            selector = SwitchSelectorKind.Reward;
            position = 2;
        }
        else if (opener.StartsWith("by", "event"))
        {
            selector = SwitchSelectorKind.Event;
            position = 2;
        }
        else if (opener.StartsWith("by", "roll", "of"))
        {
            selector = SwitchSelectorKind.Roll;
            position = 3;
            if (position < opener.Tokens.Count && opener.Tokens[position].Kind == TokenKind.Number)
            {
                max = opener.Tokens[position].Value;
                position++;
                if (max <= 0)
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, opener.Tokens[position - 1].Span,
                        "A roll needs a highest number of 1 or more.",
                        "'By roll of 5' rolls one of 0, 1, 2, 3, 4.");
            }
            else
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, opener.Span,
                    "A roll says how high it goes, as in 'By roll of 20'.");
            }
        }
        else
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, opener.Span,
                "This can branch by player class, by player nation, by event, or by roll of a number.");
            selector = SwitchSelectorKind.PlayerClass;
            position = 1;
        }

        if (position < opener.Tokens.Count && opener.Tokens[position].Kind == TokenKind.Colon)
        {
            _diagnostics.Error(DiagnosticId.RedundantColon, opener.Tokens[position].Span,
                "This ':' does nothing — the cases below already open the body.");
            position++;
        }

        ExpectEndOfLine(opener, position);
        _index++;

        var cases = new List<CaseClauseSyntax>();
        IReadOnlyList<StatementSyntax>? defaultBody = null;
        var caseIndent = -1;

        while (!AtEnd && Current.Indent > opener.Indent)
        {
            var clause = Current;
            if (caseIndent < 0)
                caseIndent = clause.Indent;
            if (clause.Indent != caseIndent)
            {
                if (clause.Indent < caseIndent)
                    break;
                _diagnostics.Error(DiagnosticId.BadIndent, clause.Span,
                    $"This case is indented {clause.Indent} but its siblings are indented {caseIndent}.");
                _index++;
                continue;
            }

            var clausePosition = 0;
            if (clause.StartsWith("else"))
            {
                clausePosition = 1;
                var body = ParseIndentedBlock(clause, ref clausePosition);
                if (defaultBody is not null)
                    _diagnostics.Error(DiagnosticId.UnexpectedToken, clause.Span, "This already has an 'Else'.");
                defaultBody = body;
                continue;
            }

            var labels = new List<CaseLabelSyntax>();
            if (clause.StartsWith("for"))
                clausePosition++;
            while (clausePosition < clause.Tokens.Count)
            {
                var token = clause.Tokens[clausePosition];
                if (token.Kind is TokenKind.Word or TokenKind.String)
                    labels.Add(new CaseLabelSyntax(token.Span, token.Text, null));
                else if (token.Kind == TokenKind.Number)
                    labels.Add(new CaseLabelSyntax(token.Span, null, token.Value));
                else
                    break;
                clausePosition++;
                if (labels.Count > 0 && labels[^1].Number is not null
                    && clausePosition + 1 < clause.Tokens.Count && clause.Tokens[clausePosition].IsWord("to")
                    && clause.Tokens[clausePosition + 1].Kind == TokenKind.Number)
                {
                    labels[^1] = labels[^1] with { Last = clause.Tokens[clausePosition + 1].Value };
                    clausePosition += 2;
                }
                if (clausePosition < clause.Tokens.Count && clause.Tokens[clausePosition].Kind == TokenKind.Comma)
                {
                    clausePosition++;
                    continue;
                }
                break;
            }

            if (labels.Count == 0)
            {
                _diagnostics.Error(DiagnosticId.UnexpectedToken, clause.Span,
                    "A case line lists what to match.");
                _index++;
                continue;
            }

            cases.Add(new CaseClauseSyntax(clause.Span, labels, ParseIndentedBlock(clause, ref clausePosition)));
        }

        return new StatementSyntax.Switch(opener.Span, selector, opener.Span, cases, defaultBody, max);
    }


    private StatementSyntax? ParseTrade(Line line)
    {
        _index++;
        var at = 1;
        while (at < line.Tokens.Count && !line.Tokens[at].IsWord("for"))
            at++;
        if (at == 1 || at >= line.Tokens.Count - 1)
        {
            _diagnostics.Error(DiagnosticId.UnexpectedToken, line.Span,
                "Trade reads 'Trade 1 of 810207000 for 1 of 521511004'.",
                "The right-hand side may also be 'random from <reward>'.");
            return null;
        }

        var take = Word("Take", line.First.Span);
        var give = Word("Give", line.Tokens[at].Span);
        var taking = ParseAction(new Line([take, .. line.Tokens.Skip(1).Take(at - 1)],
            line.Span, line.Indent));
        var giving = ParseAction(new Line([give, .. line.Tokens.Skip(at + 1)],
            line.Span, line.Indent));
        if (taking is null || giving is null)
            return null;
        return new StatementSyntax.Reward(line.Span, [taking, giving]);
    }

    private static Token Word(string text, TextSpan span) => new(TokenKind.Word, text, span);

    private StatementSyntax? ParseAction(Line line)
    {
        var best = ResolveAction(line.Tokens, out var partial);
        if (best is not null)
            return new StatementSyntax.Action(line.Span, best.Signature, best.Match);

        if (partial is not null)
        {
            var consumed = partial.Match.TokensConsumed;
            var badToken = consumed < line.Tokens.Count ? line.Tokens[consumed] : line.Tokens[^1];
            _diagnostics.Error(DiagnosticId.UnexpectedToken, badToken.Span,
                $"This reads as '{partial.Signature.Text}', which does not take {badToken} here.");
            return null;
        }

        _diagnostics.Error(DiagnosticId.UnknownAction, line.Span,
            $"'{_source.GetText(line.Span)}' is not something an NPC can do.",
            Suggest(line.Tokens));
        return null;
    }

    private sealed record ActionCandidate(ActionSignature Signature, PhraseMatch Match);

    private static ActionCandidate? ResolveAction(IReadOnlyList<Token> tokens, out ActionCandidate? partial)
    {
        ActionCandidate? complete = null;
        ActionCandidate? best = null;
        foreach (var signature in QuestVocabulary.Actions)
        {
            var match = PhraseMatcher.TryMatch(signature.Pattern, tokens, 0);
            if (match is null)
                continue;
            var candidate = new ActionCandidate(signature, match);
            if (match.TokensConsumed == tokens.Count)
            {
                if (complete is null || match.LiteralsMatched > complete.Match.LiteralsMatched)
                    complete = candidate;
                continue;
            }
            if (best is null || match.LiteralsMatched > best.Match.LiteralsMatched)
                best = candidate;
        }

        partial = complete is null ? best : null;
        return complete;
    }

    private static bool EnumSlotsResolve(PhraseMatch match)
    {
        foreach (var (name, token) in match.Arguments)
        {
            var slot = match.Pattern.Slots.FirstOrDefault(s => s.Name == name);
            if (slot is null)
                continue;
            if (slot.Kind == SlotKind.ClassGroup
                && !QuestVocabulary.ClassGroups.ContainsKey(token.Text))
                return false;
            if (slot.Kind == SlotKind.Nation
                && !QuestVocabulary.Nations.ContainsKey(token.Text))
                return false;
            if (slot.Kind == SlotKind.ClanRank
                && !QuestVocabulary.ClanRanks.ContainsKey(token.Text))
                return false;
        }
        return true;
    }

    private ConditionSyntax ParseCondition(Line line, ref int position)
    {
        var condition = ParseOr(line, ref position);
        return condition;
    }

    private ConditionSyntax ParseOr(Line line, ref int position)
    {
        var left = ParseAnd(line, ref position);
        while (position < line.Tokens.Count && line.Tokens[position].IsWord("or"))
        {
            position++;
            var right = ParseAnd(line, ref position);
            left = new ConditionSyntax.Or(left.Span.Union(right.Span), left, right);
        }
        return left;
    }

    private ConditionSyntax ParseAnd(Line line, ref int position)
    {
        var left = ParseUnary(line, ref position);
        while (position < line.Tokens.Count && line.Tokens[position].IsWord("and"))
        {
            position++;
            var right = ParseUnary(line, ref position);
            left = new ConditionSyntax.And(left.Span.Union(right.Span), left, right);
        }
        return left;
    }

    private ConditionSyntax ParseUnary(Line line, ref int position)
    {
        if (position < line.Tokens.Count && line.Tokens[position].IsWord("not"))
        {
            var negation = line.Tokens[position++];
            var operand = ParseUnary(line, ref position);
            return new ConditionSyntax.Not(negation.Span.Union(operand.Span), operand);
        }

        if (position < line.Tokens.Count && line.Tokens[position].Kind == TokenKind.OpenParen)
        {
            var open = line.Tokens[position];
            position++;
            var inner = ParseOr(line, ref position);
            if (position < line.Tokens.Count && line.Tokens[position].Kind == TokenKind.CloseParen)
            {
                position++;
                return inner;
            }
            _diagnostics.Error(DiagnosticId.UnexpectedToken, open.Span, "This '(' has no matching ')'.");
            return inner;
        }

        return ParsePredicate(line, ref position);
    }

    private ConditionSyntax ParsePredicate(Line line, ref int position)
    {
        if (position >= line.Tokens.Count)
        {
            var span = line.Tokens.Count > 0 ? line.Tokens[^1].Span : line.Span;
            _diagnostics.Error(DiagnosticId.UnknownCondition, span, "Expected something to test here.");
            return new ConditionSyntax.Invalid(span);
        }

        ConditionSignature? bestSignature = null;
        PhraseMatch? bestMatch = null;
        var bestScore = (-1, -1);
        foreach (var signature in QuestVocabulary.Conditions)
        {
            var match = PhraseMatcher.TryMatch(signature.Pattern, line.Tokens, position);
            if (match is null)
                continue;

            // 'player is Karus' and 'player is Warrior' match identical literals
            var score = (match.LiteralsMatched, EnumSlotsResolve(match) ? 1 : 0);
            if (score.CompareTo(bestScore) <= 0)
                continue;

            bestScore = score;
            bestSignature = signature;
            bestMatch = match;
        }

        if (bestSignature is null || bestMatch is null)
        {
            var token = line.Tokens[position];
            var end = FindColon(line, position);
            var span = TextSpan.FromBounds(token.Span.Start, end);
            _diagnostics.Error(DiagnosticId.UnknownCondition, span,
                $"'{_source.GetText(span)}' is not something this language can test.",
                "Conditions start with 'player', 'quest', 'daily' or 'chance'.");
            while (position < line.Tokens.Count && line.Tokens[position].Kind != TokenKind.Colon)
                position++;
            return new ConditionSyntax.Invalid(span);
        }

        var matchedSpan = TextSpan.FromBounds(
            line.Tokens[position].Span.Start,
            line.Tokens[position + bestMatch.TokensConsumed - 1].Span.End);
        position += bestMatch.TokensConsumed;
        return new ConditionSyntax.Predicate(matchedSpan, bestSignature, bestMatch);
    }

    private static int FindColon(Line line, int from)
    {
        for (var i = from; i < line.Tokens.Count; i++)
        {
            if (line.Tokens[i].Kind == TokenKind.Colon)
                return line.Tokens[i].Span.Start;
        }
        return line.Tokens[^1].Span.End;
    }

    private void ExpectEndOfLine(Line line, int position)
    {
        if (position >= line.Tokens.Count)
            return;
        var token = line.Tokens[position];
        _diagnostics.Error(DiagnosticId.UnexpectedToken, token.Span, $"Unexpected {token} at the end of the line.");
    }

    private static string Suggest(IReadOnlyList<Token> tokens)
    {
        var typed = Normalize(tokens.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text));
        var candidates = QuestVocabulary.Actions
            .Select(a => a.Pattern)
            .DistinctBy(p => p.Text)
            .OrderBy(p => Distance(typed, Normalize(LiteralWords(p))))
            .Take(2)
            .Select(p => p.Text)
            .ToArray();
        return $"Did you mean: {string.Join("   or   ", candidates)}";
    }

    private static string Normalize(IEnumerable<string> words) =>
        string.Join(' ', words.Select(w => w.ToLowerInvariant()));

    private static IEnumerable<string> LiteralWords(PhrasePattern pattern) =>
        pattern.Parts.SelectMany(Expand).OfType<PhrasePart.Literal>().Select(l => l.Word);

    private static IEnumerable<PhrasePart> Expand(PhrasePart part) =>
        part is PhrasePart.Optional optional ? optional.Parts.SelectMany(Expand) : [part];

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private bool AtEnd => _index >= _lines.Count;

    private Line Current => _lines[_index];

    private List<Line> SplitLines(IReadOnlyList<Token> tokens)
    {
        var lines = new List<Line>();
        var buffer = new List<Token>();

        void Flush()
        {
            if (buffer.Count == 0)
                return;
            var span = TextSpan.FromBounds(buffer[0].Span.Start, buffer[^1].Span.End);
            lines.Add(new Line([.. buffer], span, IndentOf(buffer[0].Span.Start)));
            buffer.Clear();
        }

        foreach (var token in tokens)
        {
            if (token.Kind is TokenKind.LineBreak or TokenKind.EndOfFile)
            {
                Flush();
                continue;
            }
            if (token.Kind == TokenKind.Bad)
                continue;
            buffer.Add(token);
        }

        Flush();
        return lines;
    }

    private int IndentOf(int offset)
    {
        var position = _source.GetLinePosition(offset);
        var text = _source.GetLineText(position.Line);
        var indent = 0;
        foreach (var c in text)
        {
            if (c == ' ')
                indent++;
            else if (c == '\t')
                indent += 4;
            else
                break;
        }
        return indent;
    }
}
