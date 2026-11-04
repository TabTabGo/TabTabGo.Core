using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TabTabGo.Core.Data;
using TabTabGo.Core.Enums;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Models;
using TabTabGo.Core.Services;
using TabTabGo.Core.Services.ViewModels;
using TabTabGo.Core.TestKit;
using TabTabGo.Core.Utilities;
using TabTabGo.Core.ViewModels;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Tests;

public class ModelsAndUtilitiesTests
{
    // --- exceptions -------------------------------------------------------------------------------------------------

    [Fact]
    public void ApiException_Defaults_And_FullConstructor()
    {
        var empty = new ApiException();
        var withMessage = new ApiException("m");
        var withInner = new ApiException("m", new InvalidOperationException("i"));
        var full = new ApiException("m", new InvalidOperationException("outer", new ArgumentException("root")), Microsoft.Extensions.Logging.LogLevel.Warning, 12, HttpStatusCode.Conflict, "CODE");

        Assert.Equal(HttpStatusCode.InternalServerError, empty.HttpStatusCode);
        Assert.Equal(0, empty.ErrorNumber);
        Assert.Equal("m", withMessage.Message);
        Assert.Equal("i", withInner.InnerException!.Message);
        Assert.Equal("root", full.RootException!.Message); // walks down to the innermost exception
        Assert.Equal(12, full.ErrorNumber);
        Assert.Equal(HttpStatusCode.Conflict, full.HttpStatusCode);
        Assert.Equal("CODE", full.ErrorCode);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, full.Level);
        Assert.Null(new ApiException("m", null, Microsoft.Extensions.Logging.LogLevel.Error).RootException);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, withMessage.Level);
    }

#pragma warning disable CS0618 // TTGException is obsolete on purpose
    [Fact]
    public void TTGException_StillWorks()
    {
        var full = new TTGException("m", new InvalidOperationException("o", new ArgumentException("root")), Microsoft.Extensions.Logging.LogLevel.Information, 3, HttpStatusCode.NotFound, "C");

        Assert.Equal("root", full.RootException!.Message);
        Assert.Equal(3, full.ErrorNumber);
        Assert.Equal(HttpStatusCode.NotFound, full.HttpStatusCode);
        Assert.Equal("C", full.ErrorCode);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, full.Level);
        Assert.Equal("m", new TTGException("m").Message);
        Assert.Equal("i", new TTGException("m", new Exception("i")).InnerException!.Message);
        Assert.NotNull(new TTGException());
        Assert.Null(new TTGException("m", null, Microsoft.Extensions.Logging.LogLevel.Error).RootException);
    }
#pragma warning restore CS0618

    // --- ErrorResponse / GeneralResponse / ValidationResult ---------------------------------------------------------

    [Fact]
    public void ErrorResponse_WithException_Serializes()
    {
        // an Exception in the body made System.Text.Json throw while writing the error (D-7)
        var response = new ErrorResponse(new ApiException("bad", null, Microsoft.Extensions.Logging.LogLevel.Error, 5, HttpStatusCode.BadRequest, "BAD"));

        var json = JsonSerializer.Serialize(response, SerializerEngine.JsonSerializationSettings);
        var newtonsoft = Nj.JsonConvert.SerializeObject(response);

        var node = JsonDocument.Parse(json).RootElement;
        Assert.Equal("bad", node.GetProperty("message").GetString());
        Assert.Equal("BAD", node.GetProperty("code").GetString());
        Assert.Equal(5, node.GetProperty("errorNumber").GetInt32());
        Assert.False(node.TryGetProperty("exception", out _));
        Assert.DoesNotContain("Exception", newtonsoft.Replace("ErrorNumber", string.Empty));
        Assert.NotNull(response.Exception);
    }

    [Fact]
    public void ErrorResponse_Constructors_OptionalValuesAreOmitted()
    {
        var json = JsonSerializer.Serialize(new ErrorResponse("m", null), SerializerEngine.JsonSerializationSettings);

        Assert.Equal("{\"message\":\"m\"}", Regex.Replace(json, "\\s", string.Empty));
    }

    [Fact]
    public void GeneralResponse_Serializes()
    {
        // [JsonExtensionData] on a dynamic property made serialisation throw (D-8)
        var json = JsonSerializer.Serialize(new GeneralResponse("done", "OK", new Dictionary<string, int> { ["A"] = 1 }), SerializerEngine.JsonSerializationSettings);
        var node = JsonDocument.Parse(json).RootElement;

        Assert.Equal("done", node.GetProperty("message").GetString());
        Assert.Equal("OK", node.GetProperty("code").GetString());
        Assert.Equal(1, node.GetProperty("data").GetProperty("A").GetInt32());
        Assert.False(node.TryGetProperty("extra", out _));
    }

    [Fact]
    public void GeneralResponse_ExtraFlattened_BothEngines()
    {
        var response = new GeneralResponse("m", null) { Extra = { ["token"] = "t", ["n"] = 2 } };

        var stj = JsonDocument.Parse(JsonSerializer.Serialize(response, SerializerEngine.JsonSerializationSettings)).RootElement;
        var newtonsoft = Nj.Linq.JObject.Parse(Nj.JsonConvert.SerializeObject(response));

        Assert.Equal("t", stj.GetProperty("token").GetString());
        Assert.Equal(2, stj.GetProperty("n").GetInt32());
        Assert.Equal("t", (string?)newtonsoft["token"]);
        Assert.Null(response.Data);
        Assert.Null(response.Code);
    }

    [Fact]
    public void ValidationResult_Defaults()
    {
        var result = new ValidationResult();

        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // --- PageList --------------------------------------------------------------------------------------------------

    [Fact]
    public void PageList_ComputedMembers()
    {
        var page = new PageList<string>(new List<string> { "a", "b" }, 5, 2, 2);

        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Count);
        Assert.True(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
        Assert.False(new PageList<string>(new List<string>(), 0, 10).HasPreviousPage);
        Assert.Equal(1, new PageList<string>(new List<string>(), 5, null).TotalPages);
        Assert.Equal(1, new PageList<string>(new List<string>(), 5, 0).TotalPages);
        Assert.False(new PageList<string>(new List<string>(), 10, 10).HasNextPage);
        Assert.Empty(new PageList<string>(null!, 0, 1).Items);
    }

    [Fact]
    public void PageList_Create_BothOverloads_OneBasedPages()
    {
        var source = Enumerable.Range(1, 25).Select(i => $"n{i}").AsQueryable();

        var second = PageList<string>.Create(source, 2, 10);
        var mapped = PageList<string>.Create(source, 3, 10, s => new string(s.Reverse().ToArray()));

        Assert.Equal(25, second.TotalItems);
        Assert.Equal("n11", second.Items[0]);
        Assert.Equal(2, second.PageNumber);
        Assert.Equal(5, mapped.Count);
        Assert.Equal("12n", mapped.Items[0]);
    }

    // --- view models: one round trip per type and engine ------------------------------------------------------------

    private static void RoundTrip<T>(T value)
    {
        var stj = JsonSerializer.Serialize(value, SerializerEngine.JsonSerializationSettings);
        var stjBack = JsonSerializer.Deserialize<T>(stj, SerializerEngine.JsonSerializationSettings);
        Assert.Equal(stj, JsonSerializer.Serialize(stjBack, SerializerEngine.JsonSerializationSettings));

        var newtonsoft = Nj.JsonConvert.SerializeObject(value);
        var newtonsoftBack = Nj.JsonConvert.DeserializeObject<T>(newtonsoft);
        Assert.Equal(newtonsoft, Nj.JsonConvert.SerializeObject(newtonsoftBack));
    }

    [Fact]
    public void ViewModels_RoundTripBothEngines()
    {
        RoundTrip(new EntityViewModel { CreatedBy = "a", CreatedDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), ExtraProperties = { ["k"] = "v" } });
        RoundTrip(new ReferenceViewModel<int> { Id = 4, Display = "d", Properties = { ["x"] = 1 } });
        RoundTrip(new ExportConfiguration { FileName = "f", FileType = ExportType.Json, Columns = { new ExportColumn { Label = "L", Field = "F", Index = 1 } }, Properties = { ["p"] = true } });
        RoundTrip(new ExportColumn<string> { Label = "L", Field = "F", Hide = true });
        RoundTrip(new GenericResponseViewModel { StatusCode = 200, Code = "C", Message = "m" });
        RoundTrip(new PagingOptionRequest { Page = 2, PageSize = 5, OrderBy = "Name", OrderDirection = "desc", Extends = "a,b" });
        RoundTrip(new CustomOdataFiler { FunctionName = "f", Parameters = { "a", "b" } });
        RoundTrip(new TabTabGo.Core.Models.Country { Name = "N", Alpha2 = "NN", Alpha3 = "NNN", Number = "001", OfficialName = "Official", PhoneCode = "+1" });
        RoundTrip(new ResponseViewModel<int> { Succeeded = true, StatusCode = 200, Code = "C", Message = "m", Payload = 5 });
        RoundTrip(new ResponseViewModel { Payload = "x" });
        RoundTrip(new ValidationResult { IsValid = true, Errors = { ["a"] = new List<string> { "e" } } });
    }

    [Fact]
    public void ViewModels_Defaults_AndFunctionPropertiesAreNotSerialised()
    {
        var column = new ExportColumn<string> { FormatFunc = o => "x", PropertyValue = s => s };
        var config = new ExportConfiguration();

        Assert.DoesNotContain("formatFunc", JsonSerializer.Serialize(column, SerializerEngine.JsonSerializationSettings));
        Assert.Equal(ExportType.Csv, config.FileType);
        Assert.Equal("; ", config.CollectionDelimiter);
        Assert.Equal("MM/dd/yyyy", config.DateFormat);
        Assert.Equal("MM/dd/yyyy hh:mm:ss", config.DateTimeFormat);
        Assert.Equal(1, new PagingOptionRequest().Page);
        Assert.Equal(20, new PagingOptionRequest().PageSize);
        Assert.Equal(string.Empty, new GenericResponseViewModel().Message);
        Assert.Null(new ErrorResponseViewModel().Exception);
        Assert.Equal("m", new ErrorResponseViewModel { Message = "m" }.Message);
    }

    [Fact]
    public void Country_UsesSnakeCaseJsonNames()
    {
        var json = JsonSerializer.Serialize(new TabTabGo.Core.Models.Country { Alpha2 = "AE", OfficialName = "UAE" });

        Assert.Contains("\"alpha_2\":\"AE\"", json);
        Assert.Contains("\"official_name\":\"UAE\"", json);
    }

    // --- RandomUtil -------------------------------------------------------------------------------------------------

    [Fact]
    public void GeneratePassword_LengthAndAlphabet()
    {
        var password = RandomUtil.GeneratePassword();

        Assert.Equal(20, password.Length);
        Assert.Matches("^[A-Za-z0-9]{20}$", password);
        Assert.Matches("^[A-Za-z0-9]{20}$", RandomUtil.GenerateUserStamp());
    }

    [Fact]
    public void RandomNumeric_DigitsOnly_AndLengths()
    {
        Assert.Matches("^[0-9]{12}$", RandomUtil.RandomNumeric(12));
        Assert.Equal(string.Empty, RandomUtil.RandomNumeric(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RandomUtil.RandomNumeric(-1));
        Assert.Matches("^[0-9]{20}$", RandomUtil.GenerateActivationKey());
        Assert.Matches("^[0-9]{20}$", RandomUtil.GenerateResetKey());
    }

    [Fact]
    public void Parallel_NoDuplicatesNoExceptions()
    {
        // System.Random is not thread safe: concurrent calls could corrupt it and return zeros (D-3)
        var passwords = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 2000, _ => passwords.Add(RandomUtil.GeneratePassword()));

        Assert.Equal(2000, passwords.Distinct().Count());
        Assert.DoesNotContain(passwords, p => p.All(c => c == p[0]));
    }

    [Fact]
    public void RandomUtil_UsesEveryCharacterClass_AcrossManySamples()
    {
        var sample = string.Concat(Enumerable.Range(0, 200).Select(_ => RandomUtil.GeneratePassword()));

        Assert.Matches("[A-Z]", sample);
        Assert.Matches("[a-z]", sample);
        Assert.Matches("[0-9]", sample);
        Assert.True(sample.Distinct().Count() >= 60); // all 62 characters show up
    }

    // --- TTGStopWatch ----------------------------------------------------------------------------------------------

    [Fact]
    public void StopWatch_StartLapStop_RecordsLapsAndTotal()
    {
        using var watch = new TTGStopWatch("module");

        watch.Start("total");
        watch.Lap("step1");
        watch.Lap("step2");
        watch.Stop();

        Assert.Equal(new[] { "step1", "step2", "total" }, watch.LapTimes.Keys);
        Assert.True(watch.LapTimes["total"] >= watch.LapTimes["step1"]);
    }

    [Fact]
    public void StopWatch_DuplicateActionNames_GetSuffix()
    {
        using var watch = new TTGStopWatch("m");

        watch.Start("a");
        watch.Lap("x");
        watch.Lap("x");
        watch.Lap("x");
        watch.Stop();

        Assert.Equal(4, watch.LapTimes.Count);
        Assert.Contains("x", watch.LapTimes.Keys);
        Assert.Contains("x_0", watch.LapTimes.Keys);
        Assert.Contains("x_1", watch.LapTimes.Keys);
    }

    [Fact]
    public void StopWatch_StopWithoutStart_DoesNotThrow_AndRecordsNothing()
    {
        using var watch = new TTGStopWatch("m");

        watch.Stop();

        Assert.Empty(watch.LapTimes); // a null key used to throw
    }

    [Fact]
    public void StopWatch_LapBeforeStart_StartsTiming()
    {
        using var watch = new TTGStopWatch("m");

        watch.Lap("first");
        watch.Lap("second");

        Assert.Equal(new[] { "second" }, watch.LapTimes.Keys);
    }

    [Fact]
    public void StopWatch_RestartAndReset()
    {
        using var watch = new TTGStopWatch("m");
        watch.Start("a");

        watch.Restart("b");
        watch.Stop();

        Assert.Equal(new[] { "a", "b" }, watch.LapTimes.Keys);
        watch.Reset();
        watch.Restart("c");
        Assert.Equal(2, watch.LapTimes.Count);
        new TTGStopWatch("m").Restart("only");
    }

    [Fact]
    public void StopWatch_CommitLog_StopsAndLogsEveryLap_WithFormatAndReference()
    {
        var logger = new FakeLogger();
        using var watch = new TTGStopWatch("mod", "REF-1", "c") { Logger = logger };
        watch.Start("work");
        watch.Lap("part");

        watch.CommitLog(Microsoft.Extensions.Logging.LogLevel.Debug);

        Assert.Equal(2, logger.Collector.Count);
        Assert.All(logger.Collector.GetSnapshot(), r => Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, r.Level));
        var state = logger.Collector.GetSnapshot()[0].StructuredState!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal("mod", state["Module"]);
        Assert.Equal("part", state["Action"]);
        Assert.Equal("REF-1", state["ReferenceNo"]);
        Assert.Matches("^[0-9:.]+$", state["TimeTaken"]!); // formatted with "c"
    }

    [Fact]
    public void StopWatch_CommitLog_WithoutLogger_IsSilent_ReferenceAndFormatConstructors()
    {
        using var watch = new TTGStopWatch("m", "ref", "c");
        watch.Start("a");

        watch.CommitLog();

        Assert.Equal("ref", watch.ReferenceNo);
        Assert.Equal("c", watch.Format);
        Assert.Equal("m", watch.Module);
        Assert.Single(watch.LapTimes);
        Assert.Null(new TTGStopWatch("m").ReferenceNo);
    }

    [Fact]
    public void StopWatch_AfterDispose_ThrowsObjectDisposed_DisposeIsIdempotent()
    {
        var watch = new TTGStopWatch("m");
        watch.Start("a");

        watch.Dispose();
        watch.Dispose();

        Assert.Throws<ObjectDisposedException>(() => watch.Start("b"));
        Assert.Throws<ObjectDisposedException>(() => watch.Lap("b"));
        Assert.Throws<ObjectDisposedException>(() => watch.Stop());
        Assert.Throws<ObjectDisposedException>(() => watch.Reset());
        Assert.Throws<ObjectDisposedException>(() => watch.Restart("b"));
        Assert.Throws<ObjectDisposedException>(() => watch.CommitLog());
        Assert.Throws<ObjectDisposedException>(() => watch.LapTimes);
    }

#pragma warning disable CS0618 // the legacy shim is obsolete on purpose
    [Fact]
    public void StopWatch_LegacyShim_StillWorks()
    {
        using var legacy = new TTG.Core.TTGStopWatch("m");
        using var withReference = new TTG.Core.TTGStopWatch("m", "ref");

        legacy.Start("a");
        legacy.Stop();

        Assert.IsAssignableFrom<TTGStopWatch>(legacy);
        Assert.Single(legacy.LapTimes);
        Assert.NotNull(withReference);
    }
#pragma warning restore CS0618

    // --- port default interface methods ----------------------------------------------------------------------------

    [Fact]
    public async Task DefaultInterfaceMethods_ReadRepository_FallBackToCountAndSyncLinq()
    {
        // implemented against the 8.x member set only
        IGenericReadRepository<Product, int> repository = new MinimalReadRepository { Data = { new Product { Id = 1 }, new Product { Id = 2 } } };

        Assert.True(await repository.AnyAsync(p => p.Id == 1));
        Assert.False(await repository.AnyAsync(p => p.Id == 9));
        Assert.Equal(2, await repository.CountAsync(repository.GetQueryable()));
        Assert.Equal(2, (await repository.ToListAsync(repository.GetQueryable())).Count);
        var page = await repository.GetProjectedPageListAsync(p => p.Id.ToString(), pageSize: 1);
        Assert.Equal("1", page.Items.Single());
        Assert.Equal(2, page.TotalItems);
    }

    [Fact]
    public async Task DefaultInterfaceMethods_UnitOfWork_FallBackToSyncMembers()
    {
        var uow = new MinimalUnitOfWork();
        IUnitOfWork port = uow;

        await port.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await port.CommitAsync();
        await port.RollbackAsync();

        Assert.Equal(new[] { "Begin:Serializable", "Commit", "Rollback" }, uow.Calls);
        Assert.False(port.HasActiveTransaction);
    }

    [Fact]
    public void EntityChange_IsAValueRecord()
    {
        var a = new EntityChange("e", "Added", new Dictionary<string, object?>(), new Dictionary<string, object?>());

        Assert.Equal("Added", a.State);
        Assert.Equal(a with { }, a);
    }

    [Fact]
    public void QueryFlags_AreCombinable()
    {
        var flags = QueryFlags.DisableTracking | QueryFlags.IsExpandable;

        Assert.True(flags.HasFlag(QueryFlags.DisableTracking));
        Assert.False(flags.HasFlag(QueryFlags.UseLocal));
        Assert.Equal(1, (int)QueryFlags.DisableTracking);
    }
}
