using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>파라미터가 데이터를 받는 방식 (Grasshopper의 GH_ParamAccess).</summary>
public enum DataAccess { Item, List, Tree }

/// <summary>데이터 트리의 가지 하나: 경로(<c>{0;1}</c>), 항목 수, 그중 null 수.</summary>
public sealed record BranchShape(string Path, int Count, int Nulls = 0);

/// <summary>
/// 파라미터 하나의 데이터 모양. Engine이 <c>IGH_Param.VolatileData</c>에서 개수만 세어 만든다 (지오메트리는 복사하지 않음).
/// </summary>
/// <param name="Name">별명 (NickName).</param>
/// <param name="Types">항목의 타입 이름. 처음 나온 순서, 중복 없이.</param>
/// <param name="Ops">적용된 데이터 연산자 (Flatten, Graft, Simplify, Reverse, Invert).</param>
public sealed record ParamShape(string Name, DataAccess Access, IReadOnlyList<BranchShape> Branches, IReadOnlyList<string> Types, IReadOnlyList<ParamOp> Ops)
{
    public int Items => Branches.Sum(b => b.Count);
    public int Nulls => Branches.Sum(b => b.Nulls);
}

/// <summary><c>K</c> 패널의 한 줄. Label은 왼쪽 (파라미터 이름 등), Text는 오른쪽.</summary>
public enum DataShapeLineKind { Section, Param, Detail, Hint }

public sealed record DataShapeLine(DataShapeLineKind Kind, string Label, string Text);

/// <summary>
/// <c>K</c>: 커서 객체의 입력·출력 데이터 모양을 패널 줄로 만들고, 입력끼리 longest list로 짝이 맞춰지는 곳을 힌트로 알린다.
/// </summary>
/// <remarks>
/// 짝 맞춤 힌트는 오류가 아니라 힌트 등급이고, 의도한 경우가 많아서 좁게 잡는다:
/// <list type="bullet">
/// <item>항목 접근(item access) 입력만 비교한다. 리스트·트리 입력은 가지 단위로만 짝을 맞추므로 뺀다.</item>
/// <item>비어 있는 입력은 뺀다 (선택 입력). 비교할 입력이 둘 이상이어야 한다.</item>
/// <item>모든 입력이 항목 1개면 힌트가 없다 (슬라이더끼리 등).</item>
/// <item>가지: 가지가 가장 많은 입력보다 적으면 마지막 가지가 (차이)번 반복된다.</item>
/// <item>항목: 같은 순번의 가지끼리 항목 수를 비교해, 모자라서 마지막 항목이 반복된 횟수를 더한다 (자기 가지만, 반복된 가지는 위의 가지 힌트가 말함).
/// 항목이 1개뿐인 입력은 가지 힌트 없이 "1개가 n번 재사용" 하나만 낸다 (n은 전체 반복 수).</item>
/// </list>
/// </remarks>
public static class DataShape
{
    // 가지를 나열할 최대 개수. 넘으면 앞의 둘과 요약
    const int ListedBranches = 3;
    // 타입 이름을 나열할 최대 개수
    const int ListedTypes = 3;

    /// <summary>패널 줄: 입력, 출력 절(비어 있으면 생략), 그리고 짝 맞춤 힌트.</summary>
    public static IReadOnlyList<DataShapeLine> Lines(IReadOnlyList<ParamShape> inputs, IReadOnlyList<ParamShape> outputs)
    {
        var lines = new List<DataShapeLine>();
        AddSection(lines, "입력", inputs);
        AddSection(lines, "출력", outputs);
        var hints = Mismatches(inputs);
        if (hints.Count > 0)
        {
            lines.Add(new DataShapeLine(DataShapeLineKind.Section, "짝 맞춤", ""));
            foreach (var hint in hints)
                lines.Add(new DataShapeLine(DataShapeLineKind.Hint, "", hint));
        }
        return lines;
    }

    static void AddSection(List<DataShapeLine> lines, string title, IReadOnlyList<ParamShape> shapes)
    {
        if (shapes.Count == 0) return;
        lines.Add(new DataShapeLine(DataShapeLineKind.Section, title, ""));
        foreach (var shape in shapes)
        {
            lines.Add(new DataShapeLine(DataShapeLineKind.Param, shape.Name, Summary(shape)));
            if (shape.Branches.Count > 0)
                lines.Add(new DataShapeLine(DataShapeLineKind.Detail, "", Branches(shape.Branches)));
        }
    }

    /// <summary>한 줄 요약: <c>item·flatten  가지 3 · 항목 30 · null 2 · Point</c>. 비어 있으면 <c>비어 있음</c>.</summary>
    public static string Summary(ParamShape shape)
    {
        var mode = string.Join("·", new[] { Access(shape.Access) }.Concat(shape.Ops.Select(Op)));
        if (shape.Items == 0 && shape.Branches.Count <= 1)
            return $"{mode}  비어 있음";
        var parts = new List<string> { $"가지 {shape.Branches.Count}", $"항목 {shape.Items}" };
        if (shape.Nulls > 0) parts.Add($"null {shape.Nulls}");
        if (shape.Types.Count > 0)
            parts.Add(shape.Types.Count > ListedTypes
                ? string.Join(", ", shape.Types.Take(ListedTypes)) + $" 외 {shape.Types.Count - ListedTypes}"
                : string.Join(", ", shape.Types));
        return $"{mode}  {string.Join(" · ", parts)}";
    }

    /// <summary>
    /// 가지별 항목 수: 셋 이하면 모두 (<c>{0;0} 10, {0;1} 7</c>), 넘으면 앞의 둘과
    /// 모두 같을 때 <c>… 가지마다 10개</c>, 다르면 <c>… 가지당 3~10개</c>.
    /// </summary>
    public static string Branches(IReadOnlyList<BranchShape> branches)
    {
        if (branches.Count == 0) return "";
        if (branches.Count <= ListedBranches)
            return string.Join(", ", branches.Select(Branch));
        var head = string.Join(", ", branches.Take(ListedBranches - 1).Select(Branch));
        var min = branches.Min(b => b.Count);
        var max = branches.Max(b => b.Count);
        return min == max ? $"{head} … 가지마다 {min}개" : $"{head} … 가지당 {min}~{max}개";
    }

    static string Branch(BranchShape b) => $"{b.Path} {b.Count}";

    static string Access(DataAccess access) => access switch
    {
        DataAccess.Item => "item",
        DataAccess.List => "list",
        _ => "tree",
    };

    static string Op(ParamOp op) => op switch
    {
        ParamOp.Flatten => "flatten",
        ParamOp.Graft => "graft",
        ParamOp.Simplify => "simplify",
        ParamOp.Reverse => "reverse",
        _ => "invert",
    };

    /// <summary>
    /// 컴포넌트 입력끼리 longest list로 데이터가 재사용되는 곳 (힌트 등급). 규칙은 <see cref="DataShape"/>의 설명.
    /// </summary>
    public static IReadOnlyList<string> Mismatches(IReadOnlyList<ParamShape> inputs)
    {
        var hints = new List<string>();
        var used = inputs.Where(p => p.Access == DataAccess.Item && p.Items > 0).ToList();
        if (used.Count < 2 || used.All(p => p.Items == 1))
            return hints;

        var maxBranches = used.Max(p => p.Branches.Count);
        var widest = used.First(p => p.Branches.Count == maxBranches);
        // 가지 순번마다 가장 긴 항목 수 (짧은 입력은 마지막 가지를 다시 씀)
        var longest = Enumerable.Range(0, maxBranches)
            .Select(b => used.Max(p => BranchAt(p, b).Count))
            .ToArray();
        var iterations = longest.Sum();

        foreach (var p in used)
        {
            if (p.Items == 1)
            {
                if (iterations > 1)
                    hints.Add($"{p.Name}: 항목 1개가 {iterations}번 재사용");
                continue;
            }

            if (p.Branches.Count < maxBranches)
                hints.Add($"{p.Name}: 가지 {p.Branches.Count}개 vs {widest.Name} {maxBranches}개 → {p.Name}의 마지막 가지가 {maxBranches - p.Branches.Count}번 반복");

            // 자기 가지 안에서 항목이 모자라 마지막 항목을 다시 쓴 횟수. 빈 가지는 반복할 항목이 없으므로 뺀다
            var repeats = 0;
            for (var b = 0; b < p.Branches.Count; b++)
            {
                var count = p.Branches[b].Count;
                if (count > 0 && count < longest[b])
                    repeats += longest[b] - count;
            }
            if (repeats == 0) continue;
            if (maxBranches == 1)
            {
                var longer = used.First(q => q.Items == longest[0]);
                hints.Add($"{p.Name}: 항목 {p.Items}개 vs {longer.Name} {longest[0]}개 → {p.Name}의 마지막 항목이 {repeats}번 반복");
            }
            else
            {
                hints.Add($"{p.Name}: 가지 안 항목이 모자라 마지막 항목이 모두 {repeats}번 반복");
            }
        }
        return hints;
    }

    // b번째 가지. 가지가 모자라면 마지막 가지 (longest list)
    static BranchShape BranchAt(ParamShape p, int b) => p.Branches[Math.Min(b, p.Branches.Count - 1)];
}
