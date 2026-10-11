using System.Text.Json.Serialization;

namespace LibreKO.Domain;

// Metadata for the domain tables read with System.Text.Json. An ahead-of-time compiled build (iOS)
// has reflection-based serialization switched off, so these types are described at compile time.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FortuneDeck))]
[JsonSerializable(typeof(DisguiseForm[]))]
[JsonSerializable(typeof(AuctionScheduleLot[]))]
[JsonSerializable(typeof(CombineBook))]
internal partial class DomainJson : JsonSerializerContext;
