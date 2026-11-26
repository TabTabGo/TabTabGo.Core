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
    public void Split_BalancedGroupsFarPastTheStack_Rejects400OnSmallStack()
    {
        // within the filter length cap, but far deeper than a 256 KB stack could recurse
        var filter = new string('(', 2_000) + "A eq 1" + new string(')', 2_000);

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

    private static string BlankRun(int blanks) => "xmlExist(a,'v') and A" + new string(' ', blanks) + "B";

    // Best-of-N wall time of `repeats` runs: the minimum is the run least disturbed by other processes on a busy machine.
    private static double BestTicks(Action run, int repeats, int rounds = 7)
    {
        run(); // warm up the JIT
        var best = long.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < repeats; i++)
            {
                run();
            }

            best = Math.Min(best, watch.ElapsedTicks);
        }

        return best;
    }

    /// <summary>How much longer the parser takes for 4 times the input: about 4 when linear, about 16 when quadratic.</summary>
    private static double ScalingFactorForFourTimesTheInput(Action<string> parse)
    {
        var small = BlankRun((ODataFilterTokenizer.MaxFilterLength - 40) / 4);
        var large = BlankRun(ODataFilterTokenizer.MaxFilterLength - 40);
        // the same total work on both sides keeps the small side measurable
        var smallTicks = BestTicks(() => { for (var i = 0; i < 4; i++) parse(small); }, 5);
        var largeTicks = BestTicks(() => parse(large), 5);
        return largeTicks / smallTicks;
    }

    [Fact]
    public void Split_LongRunOfBlanks_IsLinear()
    {
        // each blank used to look ahead over the rest of the run (quadratic). Compared by scaling, not by an absolute time, so a
        // busy build machine does not fail it: linear is about 1 here (4 small parses against 1 large one), quadratic about 4.
        var factor = ScalingFactorForFourTimesTheInput(f => Split(f));

        Assert.True(factor < 2.5, $"4x the input took {factor:F1}x as long per unit of work");
    }

    [Fact]
    public void ScalingCheck_FlagsAQuadraticParser()
    {
        // proves the check above would catch the old behaviour: a parser that looks ahead over the rest of the run for every blank
        static void Quadratic(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                for (var j = i; j < text.Length; j++)
                {
                    if (text[j] == ' ')
                    {
                        count++;
                    }
                }
            }

            GC.KeepAlive(count);
        }

        var factor = ScalingFactorForFourTimesTheInput(Quadratic);

        Assert.True(factor >= 2.5, $"a quadratic parser scaled only {factor:F1}x");
    }

    [Fact]
    public void Split_BlanksBeforeAndAfterTheKeyword_StillSplits()
    {
        var result = Split("A eq 1     and     xmlExist(a,'v')");

        Assert.Equal("A eq 1", result.Filter);
        Assert.Single(result.CustomFilters);
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
