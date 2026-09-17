using LibreKO.Quests.Text;

namespace LibreKO.Quests.Binding;

public sealed record BorrowedEvent(string Name, string File, TextSpan Span);
