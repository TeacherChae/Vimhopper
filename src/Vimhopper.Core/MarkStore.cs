using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// vim의 마크 (<c>m{a-z}</c>, <c>'{a-z}</c>). 문서마다 따로, 위치는 객체의 InstanceGuid.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>문서가 열려 있는 동안만 남고 파일에는 저장하지 않는다. 문서를 닫으면 <see cref="Forget"/>.</item>
/// <item>다른 문서로 바꿔도 각 문서의 마크는 남는다.</item>
/// <item>지운 객체의 마크도 지우지 않는다 (<c>u</c>로 되살리면 다시 쓰인다). 있는지는 Engine이 확인한다.</item>
/// </list>
/// </remarks>
/// <typeparam name="TDocument">문서를 가리키는 키. Engine은 <c>GH_Document</c> 객체 자체를 쓴다.</typeparam>
public sealed class MarkStore<TDocument> where TDocument : class
{
    readonly Dictionary<TDocument, SortedDictionary<char, Guid>> _marks = new();

    /// <summary>마크로 쓸 수 있는 글자인지 (<c>a</c>~<c>z</c>).</summary>
    public static bool IsValid(char mark) => mark is >= 'a' and <= 'z';

    /// <returns>저장했는지. a-z가 아니면 false.</returns>
    public bool Set(TDocument document, char mark, Guid id)
    {
        if (!IsValid(mark)) return false;
        if (!_marks.TryGetValue(document, out var marks))
            _marks[document] = marks = new SortedDictionary<char, Guid>();
        marks[mark] = id;
        return true;
    }

    /// <summary>그 문서에 설정된 마크의 객체. 없으면 null.</summary>
    public Guid? Get(TDocument document, char mark) =>
        _marks.TryGetValue(document, out var marks) && marks.TryGetValue(mark, out var id) ? id : null;

    /// <summary>그 문서에 설정된 마크들, 글자 순.</summary>
    public IReadOnlyList<KeyValuePair<char, Guid>> List(TDocument document) =>
        _marks.TryGetValue(document, out var marks) ? marks.ToList() : new List<KeyValuePair<char, Guid>>();

    /// <summary>문서를 닫을 때: 그 문서의 마크를 모두 버린다.</summary>
    public void Forget(TDocument document) => _marks.Remove(document);

    /// <summary>마크가 남아 있는 문서 수 (테스트용).</summary>
    public int DocumentCount => _marks.Count;
}
