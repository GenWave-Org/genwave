using System.Collections;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>
/// Immutable list with structural equality, so snapshot records compare by content and keep doing so
/// after a JSON round trip (a plain <c>IReadOnlyList</c> in a record compares by reference).
/// </summary>
[JsonConverter(typeof(TalkListJsonConverter))]
public sealed class TalkList<T> : IReadOnlyList<T>, IEquatable<TalkList<T>>
{
    private readonly T[] items;

    public TalkList(IEnumerable<T> items) => this.items = items.ToArray();

    public static TalkList<T> Empty { get; } = new([]);

    public int Count => items.Length;

    public T this[int index] => items[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(TalkList<T>? other) => other is not null && items.AsSpan().SequenceEqual(other.items);

    public override bool Equals(object? obj) => Equals(obj as TalkList<T>);

    public override string ToString() => $"[{string.Join(", ", items)}]";

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
