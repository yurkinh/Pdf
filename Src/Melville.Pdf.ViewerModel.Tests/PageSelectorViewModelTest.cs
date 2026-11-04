using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

public class PageSelectorViewModelTest
{
    private readonly PageSelectorViewModel sut = new() { MaxPage = 5 };

    [Fact]
    public void StartsOnTheFirstPage()
    {
        sut.Page.Should().Be(1);
        sut.DisplayString.Should().Be("1 of 5");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(3, 3)]
    [InlineData(99, 5)]
    public void ClampsPageToTheDocument(int requested, int expected)
    {
        sut.Page = requested;
        sut.Page.Should().Be(expected);
    }

    [Fact]
    public void CommandsAreDisabledAtTheEnds()
    {
        sut.DecrementCommand.CanExecute(null).Should().BeFalse();
        sut.ToStartCommand.CanExecute(null).Should().BeFalse();
        sut.IncrementCommand.CanExecute(null).Should().BeTrue();

        sut.ToEndCommand.Execute(null);

        sut.Page.Should().Be(5);
        sut.IncrementCommand.CanExecute(null).Should().BeFalse();
        sut.ToEndCommand.CanExecute(null).Should().BeFalse();
        sut.DecrementCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CommandsRequeryCanExecuteWhenThePageChanges()
    {
        var raised = 0;
        sut.IncrementCommand.CanExecuteChanged += (_, _) => raised++;
        sut.Page = 2;
        raised.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ChangingThePageCountReturnsToTheFirstPage()
    {
        sut.Page = 4;
        sut.MaxPage = 10;
        sut.Page.Should().Be(1);
    }

    [Fact]
    public void DisplayStringIsNotifiedWhenThePageChanges()
    {
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        sut.Page = 3;
        changed.Should().Contain(nameof(PageSelectorViewModel.DisplayString));
        sut.DisplayString.Should().Be("3 of 5");
    }
}
