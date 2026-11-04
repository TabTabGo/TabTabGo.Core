using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Services.OData;

namespace TabTabGo.Core.Services.Tests;

public class ODataFilterTokenizerTests
{
    private static readonly string[] Custom = { "xmlExist" };

    private static ODataFilterTokenizer.Result Split(string filter) => ODataFilterTokenizer.Split(filter, Custom);

    [Fact]
    public void Split_NoCustomFunction_ReturnsFilterUnchangedInMeaning()
    {
        var result = Split("Price gt 10 and Name eq 'x'");

        Assert.Equal("Price gt 10 and Name eq 'x'", result.Filter);
        Assert.Empty(result.CustomFilters);
    }

    [Fact]
    public void Split_CustomFunctionAlone_LeavesNothing()
    {
        var result = Split("xmlExist(a/b,'v')");

        Assert.Equal(string.Empty, result.Filter);
        var call = Assert.Single(result.CustomFilters);
        Assert.Equal("xmlExist", call.FunctionName);
        Assert.Equal(new[] { "a/b", "'v'" }, call.Parameters);
    }

    [Fact]
    public void Split_CustomFunctionFirstWithAnd_NoDanglingAnd()
    {
        var result = Split("xmlExist(a,'v') and Price gt 1");

        Assert.Equal("Price gt 1", result.Filter);
    }

    [Fact]
    public void Split_CustomFunctionLastWithAnd_NoDanglingAnd()
    {
        var result = Split("Price gt 1 and xmlExist(a,'v')");

        Assert.Equal("Price gt 1", result.Filter);
    }

    [Fact]
    public void Split_CustomFunctionBetweenConditions_JoinsTheRest()
    {
        var result = Split("A eq 1 and xmlExist(a,'v') and B eq 2");

        Assert.Equal("A eq 1 and B eq 2", result.Filter);
    }

    [Fact]
    public void Split_InsideGroup_EmptyGroupIsDropped()
    {
        var result = Split("A eq 1 and (xmlExist(a,'v'))");

        Assert.Equal("A eq 1", result.Filter);
    }

    [Fact]
    public void Split_InsideGroupWithOtherCondition_KeepsGroup()
    {
        var result = Split("(xmlExist(a,'v') and B eq 2) and A eq 1");

        Assert.Equal("(B eq 2) and A eq 1", result.Filter);
    }

    [Fact]
    public void Split_CaseInsensitiveFunctionName()
    {
        var result = Split("XMLEXIST(a,'v')");

        Assert.Single(result.CustomFilters);
        Assert.Equal("XMLEXIST", result.CustomFilters[0].FunctionName);
    }

    [Fact]
    public void Split_AndOrInsideStringLiteralAreNotOperators()
    {
        var result = Split("Name eq 'this and that or (those)' and xmlExist(a,'v')");

        Assert.Equal("Name eq 'this and that or (those)'", result.Filter);
    }

    [Fact]
    public void Split_PropertyNamesStartingWithOperatorWords_AreNotOperators()
    {
        var result = Split("order eq 1 and android eq 2 and notes eq 3 and xmlExist(a,'v')");

        Assert.Equal("order eq 1 and android eq 2 and notes eq 3", result.Filter);
    }

    [Fact]
    public void Split_OrWithoutCustomFunction_IsKept()
    {
        var result = Split("A eq 1 or B eq 2");

        Assert.Equal("A eq 1 or B eq 2", result.Filter);
    }

    [Fact]
    public void Split_StandardFunctionsKeepTheirParentheses()
    {
        var result = Split("contains(Name,'a') and startswith(Name,'b') and xmlExist(a,'v')");

        Assert.Equal("contains(Name,'a') and startswith(Name,'b')", result.Filter);
    }

    [Theory]
    [InlineData("A eq 1 or xmlExist(a,'v')")]
    [InlineData("xmlExist(a,'v') or A eq 1")]
    [InlineData("not xmlExist(a,'v')")]
    [InlineData("A eq 1 and (B eq 2 or xmlExist(a,'v'))")]
    [InlineData("not (xmlExist(a,'v') and A eq 1)")]
    public void Split_CustomFunctionUnderOrOrNot_Is400(string filter)
    {
        var ex = Assert.Throws<ApiException>(() => Split(filter));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
    }

    [Fact]
    public void Split_NotAroundStandardCondition_IsKept()
    {
        var result = Split("not (A eq 1) and xmlExist(a,'v')");

        Assert.Equal("not (A eq 1)", result.Filter);
    }

    [Theory]
    [InlineData("(A eq 1")]
    [InlineData("A eq 1)")]
    [InlineData("A eq 'unterminated")]
    [InlineData("A eq 1 and")]
    [InlineData("and A eq 1")]
    public void Split_Malformed_Is400(string filter)
    {
        Assert.Throws<ApiException>(() => Split(filter));
    }

    [Fact]
    public void Split_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ODataFilterTokenizer.Split(null!, Custom));
        Assert.Throws<ArgumentNullException>(() => ODataFilterTokenizer.Split("a", null!));
    }

    [Fact]
    public void Split_ArgumentsWithCommasInsideQuotes_AreOneArgument()
    {
        var result = Split("xmlExist(a,'x, y')");

        Assert.Equal(new[] { "a", "'x, y'" }, result.CustomFilters[0].Parameters);
    }

    [Theory]
    [InlineData("(A add 1) gt 2")]
    [InlineData("(Price mul Qty) gt 100")]
    [InlineData("((A add 1) mul (B sub 2)) gt 2")]
    [InlineData("A gt 1 and (Price mul Qty) gt 100")]
    [InlineData("((A add 1) gt 2)")]
    public void Split_GroupedArithmeticOperands_ParseAsConditions(string filter)
    {
        // a parenthesised operand used to throw 400 "Unexpected 'g'"
        var result = Split(filter + " and xmlExist(a,'v')");

        Assert.Equal(filter, result.Filter);
        Assert.Single(result.CustomFilters);
        Assert.Equal(filter, Split(filter).Filter);
    }

    [Fact]
    public void Split_ArithmeticGroupNextToBooleanGroup()
    {
        var result = Split("((A add 1) gt 2 or B eq 1) and xmlExist(a,'v')");

        Assert.Equal("((A add 1) gt 2 or B eq 1)", result.Filter);
    }

    // C1: a hostile $filter must never reach unbounded recursion

    private static void RunOnSmallStack(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }, 256 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    [Theory]
    [InlineData(33)]
    [InlineData(1_000)]
    public void Split_GroupsNestedTooDeeply_Rejects400OnSmallStack(int depth)
    {
        var filter = new string('(', depth) + "A eq 1" + new string(')', depth);

        RunOnSmallStack(() =>
        {
            var ex = Assert.Throws<ApiException>(() => Split(filter));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
        });
    }

    [Fact]
    public void Split_ManyNotsAndParensUnclosed_Rejects400OnSmallStack()
    {
        var filter = string.Concat(Enumerable.Repeat("not (", 10_000));

        RunOnSmallStack(() =>
        {
            var ex = Assert.Throws<ApiException>(() => Split(filter));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
        });
    }

    [Fact]
    public void Split_NestingAtTheLimit_StillParses()
    {
        var filter = new string('(', 10) + "A eq 1" + new string(')', 10) + " and xmlExist(a,'v')";

        var result = Split(filter);

        Assert.Single(result.CustomFilters);
    }

    [Fact]
    public void Split_FilterLongerThanCap_Rejects400()
    {
        var filter = "A eq '" + new string('x', 1_000_000) + "'";

        var ex = Assert.Throws<ApiException>(() => Split(filter));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.HttpStatusCode);
    }

    [Fact]
    public void Split_FilterJustUnderCap_Parses()
    {
        var filter = "A eq '" + new string('x', ODataFilterTokenizer.MaxFilterLength - 10) + "'";

        var result = Split(filter);

        Assert.Equal(filter, result.Filter);
    }

    [Theory]
    [InlineData("xmlExist(a,'v')", true)]
    [InlineData("A eq 1 and XMLEXIST (a,'v')", true)]
    [InlineData("Name eq 'xmlExist(' and A eq 1", false)]
    [InlineData("Name eq \"xmlExist(a)\"", false)]
    [InlineData("Name eq 'it''s xmlExist(x)'", false)]
    [InlineData("Name eq 'xmlExist'", false)]
    [InlineData("myxmlExist(a)", false)]
    [InlineData("x.xmlExist(a)", false)]
    [InlineData("xmlExist", false)]
    [InlineData("", false)]
    public void ContainsCustomFunction_ChecksCallsOutsideLiterals(string filter, bool expected)
    {
        Assert.Equal(expected, ODataFilterTokenizer.ContainsCustomFunction(filter, Custom));
    }

    [Fact]
    public void ContainsCustomFunction_HugeInput_NoRecursion()
    {
        RunOnSmallStack(() =>
            Assert.False(ODataFilterTokenizer.ContainsCustomFunction(new string('(', 1_000_000), Custom)));
    }

    [Fact]
    public void ContainsCustomFunction_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ODataFilterTokenizer.ContainsCustomFunction(null!, Custom));
        Assert.Throws<ArgumentNullException>(() => ODataFilterTokenizer.ContainsCustomFunction("a", null!));
    }
}
