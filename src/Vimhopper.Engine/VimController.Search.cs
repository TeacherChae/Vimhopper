using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Vimhopper.Core;

namespace Vimhopper;

// 찾기: / n N * #. 순서와 다음 대상 고르기는 Core의 Search, 여기서는 문서에서 항목을 모으고 커서를 옮긴다
internal sealed partial class VimController
{
    /// <summary>/ 입력 중인 찾기. 열 때의 커서·화면으로 Esc에 되돌리고, 순서·항목은 열 때 한 번만 모은다.</summary>
    sealed class SearchSession
    {
        public SearchSession(Guid? start, PointF view, VisualState? visual, IReadOnlyList<Guid> order, IReadOnlyList<SearchItem> items, int count)
        {
            Start = start;
            View = view;
            Visual = visual;
            Order = order;
            Items = items;
            Count = count;
        }

        public Guid? Start { get; }
        public PointF View { get; }
        // 비주얼 모드였으면 열기 전 선택 (확정 뒤 u로 되돌리기)
        public VisualState? Visual { get; }
        public IReadOnlyList<Guid> Order { get; }
        public IReadOnlyList<SearchItem> Items { get; }
        public int Count { get; }
        // 지금 글에 맞는 객체들 (incsearch 강조)
        public HashSet<Guid> Matches { get; set; } = new();
        // 입력줄 끝에 보일 [3/12] 등
        public string Info { get; set; } = "";
    }

    static List<SearchItem> SearchItems(GH_Document doc) =>
        doc.Objects
            .OfType<IGH_ActiveObject>()
            .Select(o => new SearchItem(o.InstanceGuid, o.NickName ?? "", o.Name ?? "", o.Category ?? "", o.SubCategory ?? "", o.ComponentGuid))
            .ToList();

    /// <summary><c>/</c>: 입력줄을 연다. 입력하는 동안 맞는 객체를 강조하고 커서를 다음 대상에 미리 옮긴다 (vim의 incsearch).</summary>
    void OpenSearch(int count)
    {
        if (Document is not { } doc) return;
        _search = new SearchSession(
            _current, _canvas.Viewport.MidPoint, _anchor != null ? CaptureVisual() : null,
            Search.Order(DocumentGraph.Build(doc)), SearchItems(doc), count);
        _input.Open('/', _savedIme, OnSearchChanged, OnSearchConfirmed, OnSearchCancelled);
    }

    /// <summary>입력줄을 콜백 없이 닫는다 (Vim 모드 나가기, 문서 바뀜). 커서는 그 자리에 둔다.</summary>
    void CloseSearch()
    {
        _search = null;
        _input.Close();
    }

    void OnSearchChanged(string text)
    {
        if (_search is not { } s) return;
        var query = new TextQuery(text);
        s.Matches = new HashSet<Guid>(Search.All(s.Items, query));
        var hit = Search.Next(s.Order, s.Matches.Contains, s.Start, forward: true, s.Count);
        s.Info = text.Length == 0 ? "" : hit is null ? "찾을 수 없음" : $"[{hit.Position}/{hit.Total}]";

        if (hit != null && Document?.FindObject(hit.Target, true) is { } obj)
            Select(obj);
        else
            RestoreSearchStart(s);
    }

    void OnSearchConfirmed(string text)
    {
        if (_search is not { } s) return;
        _search = null;
        // 미리 옮긴 커서는 무르고 시작 자리에서 점프한다 (점프 목록에 시작 자리가 들어가게)
        _current = s.Start;

        // 빈 Enter는 마지막 찾기 (vim과 같음. *로 찾았으면 그 종류)
        var query = text.Length > 0 ? new TextQuery(text) : _lastQuery;
        if (query == null)
        {
            RestoreSearchStart(s);
            _status = "이전 검색어가 없습니다";
            return;
        }
        _lastQuery = query;
        _lastForward = true;
        if (!JumpToMatch(query, forward: true, s.Count, s.Order, s.Items))
        {
            RestoreSearchStart(s);
            return;
        }
        if (s.Visual != null && _anchor != null && !s.Visual.SameAs(CaptureVisual()))
            _visualHistory.Push(s.Visual);
    }

    void OnSearchCancelled()
    {
        if (_search is not { } s) return;
        _search = null;
        RestoreSearchStart(s);
    }

    // 입력하기 전 커서와 화면으로
    void RestoreSearchStart(SearchSession s)
    {
        if (s.Start is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
        else
            _current = s.Start;
        _canvas.Viewport.MidPoint = s.View;
    }

    /// <summary><c>n</c> / <c>N</c>: 마지막 찾기를 같은 / 반대 방향으로.</summary>
    void SearchAgain(bool reverse, int count)
    {
        if (_lastQuery is not { } query)
        {
            _status = "이전 검색어가 없습니다";
            return;
        }
        JumpToMatch(query, _lastForward != reverse, count);
    }

    /// <summary><c>*</c> / <c>#</c>: 커서 객체와 같은 컴포넌트 종류. 마지막 찾기가 되어 <c>n</c>이 되풀이한다 (<c>#</c>면 뒤로).</summary>
    void SearchType(bool forward, int count)
    {
        if (Selected is not { } obj)
        {
            SelectNearestToView();
            return;
        }
        _lastQuery = new TypeQuery(obj.ComponentGuid, obj.Name);
        _lastForward = forward;
        JumpToMatch(_lastQuery, forward, count);
    }

    /// <summary>커서 다음 / 이전의 count번째 맞는 객체로 점프한다. 끝을 넘으면 반대쪽 끝에서 이어간다.</summary>
    /// <returns>맞는 객체가 있었는지.</returns>
    bool JumpToMatch(SearchQuery query, bool forward, int count, IReadOnlyList<Guid>? order = null, IReadOnlyList<SearchItem>? items = null)
    {
        if (Document is not { } doc) return false;
        order ??= Search.Order(DocumentGraph.Build(doc));
        items ??= SearchItems(doc);
        var matches = new HashSet<Guid>(Search.All(items, query));
        var hit = Search.Next(order, matches.Contains, _current, forward, count);
        if (hit == null || doc.FindObject(hit.Target, true) is not { } obj)
        {
            _status = $"찾을 수 없습니다: {query.Label}";
            return false;
        }
        if (hit.Target != _current)
            RecordJump();
        Select(obj);
        _status = Search.Describe(hit, query, forward);
        return true;
    }

    /// <summary>/ 입력 중: 맞는 객체를 노란 테두리로, 미리 옮긴 대상(커서)은 주황으로 (캔버스 좌표).</summary>
    void DrawSearchMatches()
    {
        if (!_vim || _search is not { Matches.Count: > 0 } s || Document is not { } doc) return;
        var g = _canvas.Graphics;
        using var match = new Pen(Color.Gold, 2f);
        using var target = new Pen(Color.DarkOrange, 3f);
        foreach (var id in s.Matches)
        {
            if (doc.FindObject(id, true) is not { } obj) continue;
            var b = obj.Attributes.Bounds;
            b.Inflate(4, 4);
            g.DrawRectangle(id == _current ? target : match, b.X, b.Y, b.Width, b.Height);
        }
    }
}
