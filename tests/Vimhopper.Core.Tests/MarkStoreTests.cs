using System;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class MarkStoreTests
{
    sealed class Doc { }

    static readonly Guid A = Guid.NewGuid();
    static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Set_and_get()
    {
        var store = new MarkStore<Doc>();
        var doc = new Doc();
        Assert.True(store.Set(doc, 'a', A));
        Assert.Equal(A, store.Get(doc, 'a'));
        Assert.Null(store.Get(doc, 'b'));
    }

    [Fact]
    public void Setting_again_overwrites()
    {
        var store = new MarkStore<Doc>();
        var doc = new Doc();
        store.Set(doc, 'a', A);
        store.Set(doc, 'a', B);
        Assert.Equal(B, store.Get(doc, 'a'));
    }

    [Theory]
    [InlineData('A')]
    [InlineData('1')]
    [InlineData('\'')]
    [InlineData(' ')]
    public void Only_lowercase_letters_are_marks(char mark)
    {
        var store = new MarkStore<Doc>();
        var doc = new Doc();
        Assert.False(store.Set(doc, mark, A));
        Assert.Null(store.Get(doc, mark));
        Assert.Empty(store.List(doc));
    }

    [Fact]
    public void Documents_keep_their_own_marks()
    {
        var store = new MarkStore<Doc>();
        var one = new Doc();
        var two = new Doc();
        store.Set(one, 'a', A);
        store.Set(two, 'a', B);
        Assert.Equal(A, store.Get(one, 'a'));
        Assert.Equal(B, store.Get(two, 'a'));
    }

    [Fact]
    public void Forget_drops_only_that_document()
    {
        var store = new MarkStore<Doc>();
        var one = new Doc();
        var two = new Doc();
        store.Set(one, 'a', A);
        store.Set(two, 'b', B);
        store.Forget(one);
        Assert.Null(store.Get(one, 'a'));
        Assert.Equal(B, store.Get(two, 'b'));
        Assert.Equal(1, store.DocumentCount);
        // 마크가 없는 문서를 잊어도 괜찮다
        store.Forget(new Doc());
    }

    [Fact]
    public void List_is_in_letter_order()
    {
        var store = new MarkStore<Doc>();
        var doc = new Doc();
        store.Set(doc, 'z', A);
        store.Set(doc, 'c', B);
        store.Set(doc, 'm', A);
        Assert.Equal(new[] { 'c', 'm', 'z' }, store.List(doc).Select(p => p.Key));
        Assert.Empty(store.List(new Doc()));
    }
}
