using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class JumpListTests
{
    static readonly Guid A = Id(1), B = Id(2), C = Id(3), D = Id(4), E = Id(5);

    static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    static bool All(Guid _) => true;

    // 커서 위치를 따라가며 jumplist를 쓰는 작은 시뮬레이션
    sealed class Cursor
    {
        public readonly JumpList List = new();
        public Guid At;
        public readonly HashSet<Guid> Deleted = new();

        public Cursor(Guid start) => At = start;

        bool Exists(Guid id) => !Deleted.Contains(id);

        public void JumpTo(Guid to)
        {
            List.Record(At);
            At = to;
        }

        public bool Back(int count = 1) => Go(List.Back(At, count, Exists));

        public bool Forward(int count = 1) => Go(List.Forward(count, Exists));

        bool Go(Guid? to)
        {
            if (to is not { } id) return false;
            At = id;
            return true;
        }
    }

    [Fact]
    public void Empty_list_goes_nowhere()
    {
        var list = new JumpList();
        Assert.Null(list.Back(A, 1, All));
        Assert.Null(list.Forward(1, All));
        Assert.Empty(list.Entries);
    }

    [Fact]
    public void Record_appends_the_position_jumped_from()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        Assert.Equal(new[] { A, B }, c.List.Entries);
        Assert.Equal(2, c.List.Index);
    }

    [Fact]
    public void First_back_stores_the_current_position_so_forward_returns()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        Assert.True(c.Back());
        Assert.Equal(A, c.At);
        Assert.Equal(new[] { A, B }, c.List.Entries);
        Assert.True(c.Forward());
        Assert.Equal(B, c.At);
        Assert.False(c.Forward());
        Assert.Equal(B, c.At);
    }

    [Fact]
    public void Back_and_forward_walk_the_list()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(D);
        Assert.True(c.Back()); Assert.Equal(C, c.At);
        Assert.True(c.Back()); Assert.Equal(B, c.At);
        Assert.True(c.Back()); Assert.Equal(A, c.At);
        Assert.False(c.Back()); Assert.Equal(A, c.At);
        Assert.True(c.Forward()); Assert.Equal(B, c.At);
        Assert.True(c.Forward()); Assert.Equal(C, c.At);
        Assert.True(c.Forward()); Assert.Equal(D, c.At);
        Assert.False(c.Forward());
        // 앞뒤로 다녀도 목록은 그대로 (지금 위치는 처음 한 번만 저장)
        Assert.Equal(new[] { A, B, C, D }, c.List.Entries);
    }

    [Fact]
    public void Counts_move_several_entries()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(D);
        Assert.True(c.Back(3)); Assert.Equal(A, c.At);
        Assert.True(c.Forward(2)); Assert.Equal(C, c.At);
    }

    [Fact]
    public void Count_past_the_end_does_nothing()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        Assert.False(c.Back(5));
        Assert.Equal(C, c.At);
        // 실패한 Ctrl+o는 지금 위치도 저장하지 않는다
        Assert.Equal(new[] { A, B }, c.List.Entries);
        Assert.Equal(2, c.List.Index);

        Assert.True(c.Back(2)); Assert.Equal(A, c.At);
        Assert.False(c.Forward(5));
        Assert.Equal(A, c.At);
        Assert.Equal(0, c.List.Index);
    }

    [Fact]
    public void Zero_or_negative_count_does_nothing()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        Assert.False(c.Back(0));
        Assert.False(c.Forward(-1));
        Assert.Equal(B, c.At);
    }

    [Fact]
    public void Recording_drops_an_earlier_duplicate()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(A);
        c.JumpTo(C);
        Assert.Equal(new[] { B, A }, c.List.Entries);
    }

    [Fact]
    public void Storing_the_current_position_drops_its_earlier_duplicate()
    {
        // A → B → C → A (C에서 A로 점프). 끝에서 Ctrl+o는 A를 저장하며 앞의 A를 지운다
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(A);
        Assert.True(c.Back());
        Assert.Equal(C, c.At);
        Assert.Equal(new[] { B, C, A }, c.List.Entries);
        Assert.True(c.Back()); Assert.Equal(B, c.At);
        Assert.False(c.Back());
        Assert.True(c.Forward(2)); Assert.Equal(A, c.At);
    }

    [Fact]
    public void Back_with_only_the_current_position_goes_nowhere()
    {
        // A에서 B로 점프한 뒤 hjkl로 A에 돌아온 경우
        var list = new JumpList();
        list.Record(A);
        Assert.Null(list.Back(A, 1, All));
        Assert.Equal(new[] { A }, list.Entries);
        Assert.Equal(1, list.Index);
    }

    [Fact]
    public void Jumping_after_going_back_appends_without_dropping_newer_entries()
    {
        // vim 기본 (jumpoptions=stack이 아님): 뒤쪽 항목을 지우지 않고 끝에 붙인다
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(D);
        c.Back(2); // B
        c.JumpTo(E);
        Assert.Equal(new[] { A, C, D, B }, c.List.Entries);
        Assert.Equal(4, c.List.Index);
        Assert.True(c.Back()); Assert.Equal(B, c.At);
        Assert.Equal(new[] { A, C, D, B, E }, c.List.Entries);
        Assert.True(c.Back()); Assert.Equal(D, c.At);
    }

    [Fact]
    public void Deleted_positions_are_skipped_and_not_counted()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(D);
        c.Deleted.Add(C);
        Assert.True(c.Back()); Assert.Equal(B, c.At);
        Assert.True(c.Forward()); Assert.Equal(D, c.At);
        c.Deleted.Add(B);
        Assert.True(c.Back()); Assert.Equal(A, c.At);
        Assert.False(c.Back());
        // 지운 객체가 되살아나면(u) 다시 쓰인다
        c.Deleted.Clear();
        Assert.True(c.Forward()); Assert.Equal(B, c.At);
        Assert.Equal(new[] { A, B, C, D }, c.List.Entries);
    }

    [Fact]
    public void Count_counts_only_existing_positions()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.JumpTo(C);
        c.JumpTo(D);
        c.Deleted.Add(B);
        Assert.False(c.Back(3));
        Assert.Equal(D, c.At);
        Assert.True(c.Back(2)); Assert.Equal(A, c.At);
    }

    [Fact]
    public void Back_without_a_current_position_does_not_store_one()
    {
        var list = new JumpList();
        list.Record(A);
        list.Record(B);
        Assert.Equal(B, list.Back(null, 1, All));
        Assert.Equal(new[] { A, B }, list.Entries);
        Assert.Equal(1, list.Index);
    }

    [Fact]
    public void Length_is_capped_by_dropping_the_oldest()
    {
        var list = new JumpList();
        for (var i = 0; i < JumpList.MaxLength + 5; i++)
            list.Record(Id(100 + i));
        Assert.Equal(JumpList.MaxLength, list.Entries.Count);
        Assert.Equal(Id(105), list.Entries[0]);
        Assert.Equal(JumpList.MaxLength, list.Index);

        // 끝에서 Ctrl+o가 지금 위치를 넣어도 최대 길이를 넘지 않는다
        Assert.Equal(Id(100 + JumpList.MaxLength + 4), list.Back(E, 1, All));
        Assert.Equal(JumpList.MaxLength, list.Entries.Count);
        Assert.Equal(E, list.Entries.Last());
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        var c = new Cursor(A);
        c.JumpTo(B);
        c.List.Clear();
        Assert.Empty(c.List.Entries);
        Assert.Equal(0, c.List.Index);
        Assert.False(c.Back());
    }
}
