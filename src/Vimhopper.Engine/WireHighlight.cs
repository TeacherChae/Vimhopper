using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>
/// 연결 강조: 커서 객체의 입력 와이어(상류)와 출력 와이어(하류)를 일반 와이어 위에 굵게 다시 그리고,
/// 지금 h / l이 갈 객체에 표시를 단다.
/// </summary>
/// <remarks>
/// 와이어는 매 프레임 커서의 파라미터만 훑는다 (그래프를 만들지 않음). h/l 대상은 층 계산이 필요해서
/// 커서가 바뀌거나 문서가 바뀌었을 때(undo 기록, 객체 추가·삭제)만 다시 계산한다.
/// </remarks>
internal sealed class WireHighlight : IDisposable
{
    // Grasshopper 기본 캔버스(밝은 회색)에서 잘 보이고, 선택 와이어(초록)·인덱스 번호(주황)와 겹치지 않는 색
    static readonly Color Upstream = Color.FromArgb(0, 105, 230);
    static readonly Color Downstream = Color.FromArgb(205, 0, 125);

    // 캔버스 좌표 두께. 축소해도 화면에서 이 픽셀보다 가늘어지지 않는다
    const float WireWidth = 3.5f;
    const float MinWireScreenWidth = 2.5f;
    const float OutlineWidth = 2f;
    const float MinOutlineScreenWidth = 1.5f;

    readonly Font _tagFont = new("Consolas", 7f, FontStyle.Bold);

    GH_Document? _doc;
    bool _dirty = true;
    Guid? _cachedFor;
    (Guid? H, Guid? L) _hops;
    // 계산할 때 커서 자식들의 위치. l의 순서가 화면 위치에 달려 있어서, undo 기록 없이 옮겨도(드래그 중) 따라가게 한다
    int _childrenSignature;

    public void Dispose()
    {
        Release();
        _tagFont.Dispose();
    }

    /// <summary>문서 이벤트를 떼고 캐시를 비운다. 캔버스 문서가 바뀌거나 Vim 모드를 나갈 때. 다음 그리기에서 다시 붙는다.</summary>
    public void Release()
    {
        Watch(null);
        _cachedFor = null;
    }

    /// <summary>CanvasPostPaintWires에서: 일반 와이어 위, 객체 아래에 그린다.</summary>
    public void DrawWires(GH_Canvas canvas, IGH_DocumentObject cursor)
    {
        var g = canvas.Graphics;
        var width = Math.Max(WireWidth, MinWireScreenWidth / canvas.Viewport.Zoom);
        using var up = WirePen(Upstream, width);
        using var down = WirePen(Downstream, width);

        foreach (var input in VimController.Params(cursor, ParamSide.Input))
            foreach (var source in input.Sources)
                DrawWire(g, up, input.Attributes.InputGrip, source.Attributes.OutputGrip);

        foreach (var output in VimController.Params(cursor, ParamSide.Output))
            foreach (var recipient in output.Recipients)
                DrawWire(g, down, recipient.Attributes.InputGrip, output.Attributes.OutputGrip);
    }

    /// <summary>CanvasPostPaintObjects에서: h / l이 갈 객체에 외곽선과 글자.</summary>
    public void DrawHops(GH_Canvas canvas, GH_Document doc, IGH_DocumentObject cursor)
    {
        Watch(doc);
        var signature = ChildrenSignature(cursor);
        if (_dirty || _cachedFor != cursor.InstanceGuid || _childrenSignature != signature)
        {
            // 이동(VimController.Move)과 같은 그래프·같은 규칙
            _hops = GraphNavigator.Hops(DocumentGraph.Build(doc), cursor.InstanceGuid);
            _cachedFor = cursor.InstanceGuid;
            _childrenSignature = signature;
            _dirty = false;
        }

        var zoom = canvas.Viewport.Zoom;
        DrawTag(canvas.Graphics, doc, _hops.H, "h", Upstream, zoom, left: true);
        DrawTag(canvas.Graphics, doc, _hops.L, "l", Downstream, zoom, left: false);
    }

    // l이 자식을 고르는 기준 중 화면 위치(CenterY, X)에 쓰이는 것. 층과 h는 위치와 무관하다. 커서 차수만큼만 훑는다
    static int ChildrenSignature(IGH_DocumentObject cursor)
    {
        unchecked
        {
            var hash = 17;
            foreach (var output in VimController.Params(cursor, ParamSide.Output))
                foreach (var recipient in output.Recipients)
                {
                    var b = recipient.Attributes.GetTopLevel.Bounds;
                    hash = hash * 31 + b.X.GetHashCode();
                    hash = hash * 31 + b.Y.GetHashCode();
                    hash = hash * 31 + b.Height.GetHashCode();
                }
            return hash;
        }
    }

    static Pen WirePen(Color color, float width) =>
        new(Color.FromArgb(210, color), width) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    // Grasshopper가 와이어를 그리는 것과 같은 곡선: 입력 그립에서 왼쪽으로, 출력 그립에서 오른쪽으로 나가는 베지어
    static void DrawWire(Graphics g, Pen pen, PointF inputGrip, PointF outputGrip)
    {
        using var path = GH_Painter.ConnectionPath(inputGrip, outputGrip, GH_WireDirection.left, GH_WireDirection.right);
        if (path != null)
            g.DrawPath(pen, path);
    }

    void DrawTag(Graphics g, GH_Document doc, Guid? id, string text, Color color, float zoom, bool left)
    {
        if (id is not { } target || doc.FindObject(target, true) is not { } obj)
            return;

        var bounds = obj.Attributes.Bounds;
        bounds.Inflate(3, 3);
        using (var pen = new Pen(color, Math.Max(OutlineWidth, MinOutlineScreenWidth / zoom)))
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

        // 외곽선 위쪽 모서리에 붙는 작은 글자 상자. h는 왼쪽, l은 오른쪽 (같은 객체여도 겹치지 않게)
        var size = g.MeasureString(text, _tagFont);
        var x = left ? bounds.Left : bounds.Right - size.Width;
        var rect = new RectangleF(x, bounds.Top - size.Height, size.Width, size.Height);
        using (var back = new SolidBrush(color))
            g.FillRectangle(back, rect);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, _tagFont, Brushes.White, rect, format);
    }

    // 문서가 바뀌면 h/l 대상을 다시 계산한다. 와이어·위치 변경은 undo 기록으로 잡히고,
    // 타이머 등으로 자주 도는 SolutionStart는 쓰지 않는다
    void Watch(GH_Document? doc)
    {
        if (doc == _doc) return;
        if (_doc != null)
        {
            _doc.UndoStateChanged -= OnChanged;
            _doc.ObjectsAdded -= OnChanged;
            _doc.ObjectsDeleted -= OnChanged;
        }
        _doc = doc;
        _dirty = true;
        if (doc != null)
        {
            doc.UndoStateChanged += OnChanged;
            doc.ObjectsAdded += OnChanged;
            doc.ObjectsDeleted += OnChanged;
        }
    }

    void OnChanged(object sender, EventArgs e) => _dirty = true;
}
