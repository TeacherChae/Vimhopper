using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Vimhopper.Core;

namespace Vimhopper;

// K: 커서 객체의 데이터 모양. 개수 세기는 여기서, 요약과 짝 맞춤 힌트는 Core의 DataShape
internal sealed partial class VimController
{
    // 패널을 객체 오른쪽(모자라면 왼쪽)에서 띄울 거리 (화면 픽셀)
    const float DataShapeGap = 12;

    /// <summary>열린 <c>K</c> 패널. K를 누른 때의 값이고, 커서가 그 객체를 떠나면 닫는다.</summary>
    sealed record DataShapePanel(Guid Target, IReadOnlyList<DataShapeLine> Lines);

    DataShapePanel? _dataShape;

    /// <summary><c>K</c>: 커서 객체의 입력·출력 데이터 모양 패널을 연다. 보일 것이 없으면 상태줄에 이유를.</summary>
    void ShowDataShape()
    {
        _dataShape = null;
        if (Selected is not { } obj)
        {
            _status = "커서 객체가 없습니다";
            return;
        }
        // 떠 있는 파라미터는 그립과 무관하게 자기 자신을 한 번만 보인다
        var (inputs, outputs) = obj is IGH_Param param
            ? (Array.Empty<IGH_Param>(), new[] { param })
            : (Params(obj, ParamSide.Input), Params(obj, ParamSide.Output));
        if (inputs.Count == 0 && outputs.Count == 0)
        {
            _status = "파라미터가 없는 객체입니다";
            return;
        }
        if (obj is IGH_ActiveObject active)
        {
            if (active.Locked)
            {
                _status = "꺼진 객체라 데이터가 없습니다";
                return;
            }
            if (active.Phase == GH_SolutionPhase.Blank)
            {
                _status = "아직 계산되지 않았습니다";
                return;
            }
        }
        _dataShape = new DataShapePanel(obj.InstanceGuid, DataShape.Lines(
            inputs.Select(Shape).ToList(), outputs.Select(Shape).ToList()));
    }

    void CloseDataShape()
    {
        if (_dataShape == null) return;
        _dataShape = null;
        _canvas.Invalidate();
    }

    /// <summary>
    /// 패널이 열린 채 키를 누르면 닫는다. <c>K</c>와 <c>Esc</c>는 닫기만 하고(true) 나머지 키는 그대로 처리한다.
    /// </summary>
    bool CloseDataShapeOnKey(Keys code, Keys modifiers)
    {
        if (_dataShape == null) return false;
        CloseDataShape();
        return (code == Keys.K && modifiers == Keys.Shift) || (code == Keys.Escape && modifiers == Keys.None);
    }

    // 파라미터의 계산된 데이터에서 모양만 센다 (항목은 복사하지 않음)
    static ParamShape Shape(IGH_Param param)
    {
        var data = param.VolatileData;
        var branches = new List<BranchShape>(data.PathCount);
        var types = new List<string>();
        for (var i = 0; i < data.PathCount; i++)
        {
            var branch = data.get_Branch(i);
            var nulls = 0;
            foreach (var item in branch)
            {
                if (item is not IGH_Goo goo)
                {
                    nulls++;
                    continue;
                }
                var type = goo.TypeName;
                if (!types.Contains(type))
                    types.Add(type);
            }
            branches.Add(new BranchShape(data.get_Path(i).ToString(), branch.Count, nulls));
        }

        var ops = new List<ParamOp>();
        if (param.DataMapping == GH_DataMapping.Flatten) ops.Add(ParamOp.Flatten);
        if (param.DataMapping == GH_DataMapping.Graft) ops.Add(ParamOp.Graft);
        if (param.Simplify) ops.Add(ParamOp.Simplify);
        if (param.Reverse) ops.Add(ParamOp.Reverse);
        if (param is Param_Boolean { Invert: true }) ops.Add(ParamOp.Invert);

        var access = param.Access switch
        {
            GH_ParamAccess.item => DataAccess.Item,
            GH_ParamAccess.list => DataAccess.List,
            _ => DataAccess.Tree,
        };
        return new ParamShape(param.NickName ?? "", access, branches, types, ops);
    }

    /// <summary>
    /// 커서 객체 옆에 패널을 그린다 (화면 좌표, <see cref="DrawHud"/>가 부름). 힌트 패널과 같은 모양:
    /// 절 제목은 회색, 파라미터 이름은 주황, 짝 맞춤 힌트는 노랑.
    /// </summary>
    void DrawDataShape(Graphics g)
    {
        if (_dataShape is not { } panel) return;
        if (Selected is not { } obj || obj.InstanceGuid != panel.Target)
        {
            _dataShape = null;
            return;
        }

        const float pad = 6, gap = 8;
        var lines = panel.Lines;
        var labelWidth = lines.Where(l => l.Kind == DataShapeLineKind.Param)
            .Select(l => g.MeasureString(l.Label, _hudFont).Width).DefaultIfEmpty(0).Max();
        var rowHeight = Math.Max(_hudFont.GetHeight(g), _hintFont.GetHeight(g)) + 2;
        var width = lines.Max(l => l.Kind switch
        {
            DataShapeLineKind.Section => g.MeasureString(l.Label, _hintFont).Width,
            DataShapeLineKind.Param or DataShapeLineKind.Detail => labelWidth + gap + g.MeasureString(l.Text, _hintFont).Width,
            _ => g.MeasureString(l.Text, _hintFont).Width,
        }) + pad * 2;
        var height = lines.Count * rowHeight + pad * 2;

        // 객체 오른쪽에, 화면을 넘으면 왼쪽에. 위아래는 화면 안으로
        var b = obj.Attributes.Bounds;
        var topRight = _canvas.Viewport.ProjectPoint(new PointF(b.Right, b.Top));
        var topLeft = _canvas.Viewport.ProjectPoint(new PointF(b.Left, b.Top));
        var client = _canvas.ClientSize;
        var x = topRight.X + DataShapeGap;
        if (x + width > client.Width && topLeft.X - DataShapeGap - width >= 0)
            x = topLeft.X - DataShapeGap - width;
        x = Math.Max(4, Math.Min(x, client.Width - width - 4));
        var y = Math.Max(4, Math.Min(topRight.Y, client.Height - height - 4));
        var rect = new RectangleF(x, y, width, height);
        using (var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            g.FillRectangle(back, rect);

        using var keyBrush = new SolidBrush(Color.Orange);
        using var sectionBrush = new SolidBrush(Color.Gray);
        using var hintBrush = new SolidBrush(Color.Gold);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lx = rect.X + pad;
            var ly = rect.Y + pad + i * rowHeight;
            switch (line.Kind)
            {
                case DataShapeLineKind.Section:
                    g.DrawString(line.Label, _hintFont, sectionBrush, lx, ly + 1);
                    break;
                case DataShapeLineKind.Param:
                    g.DrawString(line.Label, _hudFont, keyBrush, lx, ly);
                    g.DrawString(line.Text, _hintFont, Brushes.White, lx + labelWidth + gap, ly + 1);
                    break;
                case DataShapeLineKind.Detail:
                    g.DrawString(line.Text, _hintFont, sectionBrush, lx + labelWidth + gap, ly + 1);
                    break;
                default:
                    g.DrawString(line.Text, _hintFont, hintBrush, lx, ly + 1);
                    break;
            }
        }
    }
}
