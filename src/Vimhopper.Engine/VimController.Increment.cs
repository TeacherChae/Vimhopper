using System.Collections.Generic;
using System.Linq;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using Vimhopper.Core;

namespace Vimhopper;

// Ctrl+a / Ctrl+x: Number Slider 값 올리기·내리기, Boolean Toggle 뒤집기 (vim의 숫자 더하기·빼기)
internal sealed partial class VimController
{
    /// <summary>
    /// 커서 객체(비주얼 모드면 선택한 객체들)의 슬라이더를 <paramref name="count"/>단계 옮기고 토글을 뒤집는다.
    /// 바뀐 객체 모두를 undo 기록 하나로 남기고 (<c>u</c> 한 번), 슬라이더를 끌 때처럼 다시 계산한다.
    /// </summary>
    /// <returns>하나라도 바뀌었는지. 모두 끝에 있었거나 대상이 없으면 false라서 <c>.</c>의 마지막 변경을 덮어쓰지 않는다.</returns>
    bool Increment(bool up, int count)
    {
        if (Document is not { } doc)
            return false;
        var key = up ? "Ctrl+a" : "Ctrl+x";
        var targets = Targets();
        if (targets.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }

        var sliders = targets.OfType<GH_NumberSlider>().ToList();
        var toggles = targets.OfType<GH_BooleanToggle>().ToList();
        if (sliders.Count + toggles.Count == 0)
        {
            _status = _anchor != null
                ? $"{key}: 선택에 Number Slider나 Boolean Toggle이 없습니다"
                : $"{key}: Number Slider나 Boolean Toggle이 아닙니다";
            return false;
        }

        // 값을 바꾸기 전에 계산해서, 바뀌는 객체만 기록한다
        var steps = up ? count : -count;
        var sliderChanges = new List<(GH_NumberSlider Slider, decimal Old, decimal New)>();
        var clamped = 0;
        foreach (var slider in sliders)
        {
            var s = slider.Slider;
            var result = SliderStep.Apply(s.Value, s.Minimum, s.Maximum, Kind(s.Type), s.DecimalPlaces, steps);
            if (result.Clamped) clamped++;
            if (result.Value != s.Value)
                sliderChanges.Add((slider, s.Value, result.Value));
        }

        if (sliderChanges.Count + toggles.Count == 0)
        {
            _status = $"{key}: {(up ? "최댓값" : "최솟값")}입니다";
            return false;
        }

        const string name = "Vimhopper increment";
        // 바꾸기 전 상태를 만들어 두고, 실제로 바뀐 객체만 기록한다.
        // 일반 객체 기록은 되돌릴 때 다시 계산한다 (ExpiresSolution)
        var events = new Dictionary<IGH_DocumentObject, GH_UndoRecord>();
        foreach (var o in sliderChanges.Select(c => (IGH_DocumentObject)c.Slider).Concat(toggles))
            events[o] = doc.UndoUtil.CreateGenericObjectEvent(name, o);

        // SetSliderValue는 범위로 자르고 ExpireSolution(false)까지 한다. Grasshopper가 값을 다시 맞출 수 있으므로
        // 실제 값으로 비교한다 (움직이지 않았으면 기록하지 않음). 다시 계산은 끝에 한 번
        var moved = new List<(GH_NumberSlider Slider, decimal Old, decimal New)>();
        foreach (var (slider, old, value) in sliderChanges)
        {
            slider.SetSliderValue(value);
            if (slider.Slider.Value != old)
                moved.Add((slider, old, slider.Slider.Value));
        }
        sliderChanges = moved;
        if (sliderChanges.Count + toggles.Count == 0)
        {
            _status = $"{key}: {(up ? "최댓값" : "최솟값")}입니다";
            return false;
        }
        foreach (var toggle in toggles)
        {
            toggle.Value = !toggle.Value;
            toggle.ExpireSolution(false);
        }
        var changed = sliderChanges.Select(c => (IGH_DocumentObject)c.Slider).Concat(toggles);
        doc.UndoUtil.RecordEvent(name, changed.SelectMany(o => events[o].Actions).ToList());
        doc.NewSolution(false);

        _status = IncrementStatus(sliderChanges, toggles, clamped, up);
        return true;
    }

    static string IncrementStatus(List<(GH_NumberSlider Slider, decimal Old, decimal New)> sliders, List<GH_BooleanToggle> toggles, int clamped, bool up)
    {
        var end = up ? "최댓값" : "최솟값";
        if (sliders.Count + toggles.Count == 1)
        {
            if (toggles.Count == 1)
                return $"{toggles[0].NickName}: {(toggles[0].Value ? "True" : "False")}";
            var (slider, old, value) = sliders[0];
            return $"{slider.NickName}: {Format(slider, old)} → {Format(slider, value)}{(clamped > 0 ? $" ({end})" : "")}";
        }
        var parts = new List<string>();
        if (sliders.Count > 0) parts.Add($"슬라이더 {sliders.Count}개");
        if (toggles.Count > 0) parts.Add($"토글 {toggles.Count}개");
        var note = clamped > 0 ? $" ({clamped}개 {end}에서 멈춤)" : "";
        return $"{string.Join(", ", parts)} 바꿈{note}";
    }

    // 슬라이더에 보이는 자릿수로
    static string Format(GH_NumberSlider slider, decimal value) =>
        slider.Slider.Type == GH_SliderAccuracy.Float
            ? value.ToString("F" + slider.Slider.DecimalPlaces, System.Globalization.CultureInfo.InvariantCulture)
            : decimal.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

    static SliderKind Kind(GH_SliderAccuracy accuracy) => accuracy switch
    {
        GH_SliderAccuracy.Integer => SliderKind.Integer,
        GH_SliderAccuracy.Even => SliderKind.Even,
        GH_SliderAccuracy.Odd => SliderKind.Odd,
        _ => SliderKind.Float,
    };
}
