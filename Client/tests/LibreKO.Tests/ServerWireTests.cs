using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class ServerWireTests
{
    private const string ServerGameDirKey = "ServerGameDir";
    private const string ServerPacketType = "LibreKO.Common.Infrastructure.Network.Packet";
    private const string ServerNamespace = "LibreKO.Game";
    private const int MaxDepth = 4;
    private const ulong ReaderClockMsec = 50_000UL;
    private static readonly string[] Variants = { "ones", "zeros", "twos" };

    private static readonly HashSet<string> RefusedForGeneratedArguments = new()
    {
        "CharacterSealPacketWriter.CharacterSheet(Sheet) ones",
        "CharacterSealPacketWriter.CharacterSheet(Sheet) zeros",
        "CharacterSealPacketWriter.CharacterSheet(Sheet) twos",
        "QuestPacketWriter.Objectives(QuestObjectives) zeros",
        "QuestPacketWriter.RewardReceipt(Int32,IReadOnlyList`1) zeros",
    };

    private enum Outcome { Read, Leftover, Unread, Unhandled, Underflow, Reader, Writer }

    private sealed record Result(Outcome Outcome, string Detail);

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private sealed class ServerLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public ServerLoadContext(string mainAssembly) : base(isCollectible: false) =>
            resolver = new AssemblyDependencyResolver(mainAssembly);

        protected override Assembly? Load(AssemblyName name) =>
            resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;
    }

    [Fact]
    public void EveryServerWriterIsReadWithoutUnderflowOrReaderError()
    {
        string serverDir = typeof(ServerWireTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == ServerGameDirKey).Value!;
        string gamePath = Path.Combine(serverDir, "LibreKO.Game.dll");
        Assert.True(File.Exists(gamePath), $"the server is not built: {gamePath}");

        var context = new ServerLoadContext(gamePath);
        var game = context.LoadFromAssemblyPath(gamePath);
        var serverPacket = context.LoadFromAssemblyName(new AssemblyName("LibreKO.Common")).GetType(ServerPacketType)!;
        var getBytes = serverPacket.GetMethod("GetBytes")!;

        var writers = Writers(game, serverPacket);
        Assert.NotEmpty(writers);

        var failures = new List<string>();
        foreach (var writer in writers)
        {
            string id = $"{writer.DeclaringType!.Name}.{writer.Name}({string.Join(",", writer.GetParameters().Select(p => p.ParameterType.Name))})";
            foreach (var variant in Variants)
            {
                var result = Run(writer, variant, serverPacket, getBytes);
                if (result.Outcome is Outcome.Underflow or Outcome.Reader && !RefusedForGeneratedArguments.Contains($"{id} {variant}"))
                    failures.Add($"{id} {variant}: {result.Outcome} {result.Detail}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static List<MethodInfo> Writers(Assembly game, Type serverPacket)
    {
        Type[] types;
        try { types = game.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        return types
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(ServerNamespace))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.ContainsGenericParameters && (m.ReturnType == serverPacket
                || typeof(IEnumerable).IsAssignableFrom(m.ReturnType) && m.ReturnType.IsGenericType
                   && m.ReturnType.GetGenericArguments()[0] == serverPacket))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();
    }

    private static Result Run(MethodInfo writer, string variant, Type serverPacket, MethodInfo getBytes)
    {
        object?[] arguments;
        try { arguments = writer.GetParameters().Select(p => Make(p.ParameterType, variant, 0)).ToArray(); }
        catch (Exception) { return new Result(Outcome.Writer, "arguments"); }

        var parameters = writer.GetParameters();
        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(byte) && !parameters[0].ParameterType.IsEnum)
            return RunWith(writer, variant, arguments, serverPacket, getBytes);

        IEnumerable<object> leading = parameters[0].ParameterType.IsEnum
            ? Enum.GetValues(parameters[0].ParameterType).Cast<object>()
            : Enumerable.Range(0, byte.MaxValue + 1).Select(v => (object)(byte)v);
        Result? best = null;
        foreach (var value in leading)
        {
            var tryArguments = (object?[])arguments.Clone();
            tryArguments[0] = value;
            var result = RunWith(writer, variant, tryArguments, serverPacket, getBytes);
            if (best == null || Rank(result.Outcome) < Rank(best.Outcome)) best = result;
            if (result.Outcome == Outcome.Read) break;
        }
        return best ?? RunWith(writer, variant, arguments, serverPacket, getBytes);
    }

    private static int Rank(Outcome outcome) => outcome switch
    {
        Outcome.Read => 0,
        Outcome.Leftover => 1,
        Outcome.Unread => 2,
        Outcome.Unhandled => 3,
        Outcome.Underflow => 4,
        Outcome.Reader => 5,
        _ => 6,
    };

    private static Result RunWith(MethodInfo writer, string variant, object?[] arguments, Type serverPacket, MethodInfo getBytes)
    {
        object? written;
        try { written = writer.Invoke(null, arguments); }
        catch (Exception e) { return new Result(Outcome.Writer, (e.InnerException ?? e).GetType().Name); }
        if (written == null) return new Result(Outcome.Writer, "null");

        var packets = new List<byte[]>();
        if (serverPacket.IsInstanceOfType(written)) packets.Add((byte[])getBytes.Invoke(written, null)!);
        else foreach (var p in (IEnumerable)written) packets.Add((byte[])getBytes.Invoke(p, null)!);

        var worst = new Result(Outcome.Read, "");
        foreach (var bytes in packets)
        {
            var result = Read(bytes, variant);
            if (Rank(result.Outcome) > Rank(worst.Outcome)) worst = result;
        }
        return worst;
    }

    private static Result Read(byte[] bytes, string variant)
    {
        var session = new GameSession();
        session.Self.ApplyCharacterId(variant switch { "ones" => 1, "twos" => 2, _ => 7 });
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => ReaderClockMsec);
        var packet = new Packet(bytes[0]);
        if (bytes.Length > 1) packet.WriteBytes(bytes[1..]);
        packet.ResetOffset();
        string where = $"0x{bytes[0]:X2}" + (bytes.Length > 1 ? $" sub {bytes[1]}" : "");
        try
        {
            if (!dispatcher.Dispatch(packet)) return new Result(Outcome.Unhandled, where);
            int remaining = packet.RemainingBytes;
            if (remaining == 0) return new Result(Outcome.Read, where);
            return new Result(remaining == bytes.Length - 2 && bytes.Length > 2 ? Outcome.Unread : Outcome.Leftover, where);
        }
        catch (InvalidDataException e) when (e.Message.Contains("underflow"))
        {
            return new Result(Outcome.Underflow, $"{where}: {e.Message}");
        }
        catch (Exception e)
        {
            return new Result(Outcome.Reader, $"{where}: {e.GetType().Name}: {e.Message}");
        }
    }

    private static object? Make(Type type, string variant, int depth)
    {
        if (depth > MaxDepth) return type.IsValueType ? Activator.CreateInstance(type) : null;
        bool zeros = variant == "zeros";
        long n = variant switch { "zeros" => 0, "twos" => 2, _ => 1 };
        if (Nullable.GetUnderlyingType(type) is { } underlying) return zeros ? null : Make(underlying, variant, depth + 1);
        if (type == typeof(string)) return zeros ? "" : "Ab";
        if (type == typeof(bool)) return !zeros && variant != "twos";
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            if (values.Length == 0) return Activator.CreateInstance(type);
            int index = zeros ? 0 : (int)n - 1 + (values.Length > 1 ? 1 : 0);
            return values.GetValue(Math.Min(index, values.Length - 1));
        }
        if (type.IsPrimitive)
            return type == typeof(float) ? (float)n : type == typeof(double) ? (double)n : Convert.ChangeType(n, type);
        if (type == typeof(decimal)) return (decimal)n;
        if (type == typeof(DateTime)) return new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        if (type == typeof(TimeSpan)) return TimeSpan.FromMinutes(n);
        if (type == typeof(Guid)) return Guid.Empty;
        if (typeof(Delegate).IsAssignableFrom(type)) return null;
        int length = zeros ? 0 : (int)n;
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            var array = Array.CreateInstance(element, length);
            for (int i = 0; i < length; i++) array.SetValue(Make(element, variant, depth + 1), i);
            return array;
        }
        if (type.IsGenericType && MakeGeneric(type, variant, depth, length) is { } generic) return generic;
        if (type.IsInterface || type.IsAbstract) return null;
        return MakeObject(type, variant, depth);
    }

    private static object? MakeGeneric(Type type, string variant, int depth, int length)
    {
        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        if (arguments.Length == 1 && (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IList<>) || definition == typeof(List<>) || definition == typeof(ICollection<>)
            || definition == typeof(IReadOnlyCollection<>)))
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments[0]))!;
            for (int i = 0; i < length; i++) list.Add(Make(arguments[0], variant, depth + 1));
            return list;
        }
        if (arguments.Length == 1 && (definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>)))
        {
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arguments[0]))!;
            var add = set.GetType().GetMethod("Add")!;
            for (int i = 0; i < length; i++) add.Invoke(set, new[] { Make(arguments[0], variant, depth + 1) });
            return set;
        }
        if (arguments.Length == 2 && (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>)
            || definition == typeof(IReadOnlyDictionary<,>)))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;
            if (length > 0) dictionary[Make(arguments[0], variant, depth + 1)!] = Make(arguments[1], variant, depth + 1);
            return dictionary;
        }
        if (definition.FullName != null && definition.FullName.StartsWith("System.ValueTuple"))
            return Activator.CreateInstance(type, arguments.Select(a => Make(a, variant, depth + 1)).ToArray());
        if (definition == typeof(KeyValuePair<,>))
            return Activator.CreateInstance(type, Make(arguments[0], variant, depth + 1), Make(arguments[1], variant, depth + 1));
        return null;
    }

    private static object MakeObject(Type type, string variant, int depth)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        object made;
        if (constructor != null && constructor.GetParameters().Length > 0)
            made = constructor.Invoke(constructor.GetParameters().Select(p => Make(p.ParameterType, variant, depth + 1)).ToArray());
        else if (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null)
            made = Activator.CreateInstance(type)!;
        else
            made = RuntimeHelpers.GetUninitializedObject(type);
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            try
            {
                var current = property.GetValue(made);
                if (current == null || property.PropertyType == typeof(string) && (string)current == "")
                    property.SetValue(made, Make(property.PropertyType, variant, depth + 1));
            }
            catch (Exception) { }
        }
        return made;
    }
}
