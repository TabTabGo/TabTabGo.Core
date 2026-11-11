using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Extensions;
using TabTabGo.Core.Utilities;

namespace TabTabGo.Core.Tests;

public class ScopeAndBoundsTests
{
    public class Aliased : Entity
    {
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("tenant_id")]
        public int TenantId { get; set; }

        [Newtonsoft.Json.JsonProperty("legacy_code")]
        public string? Code { get; set; }
    }

    // --- RandomUtil is not System.Random (D-3 mutation survivor) ----------------------------------------------------

    [Fact]
    public void RandomUtil_DoesNotReferenceSystemRandom_AndUsesTheCryptographicGenerator()
    {
        var assembly = typeof(RandomUtil).Assembly;
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var type = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t => reader.GetString(t.Name) == nameof(RandomUtil));

        var referencedTypes = new HashSet<string>();
        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (var i = 0; i + 4 < il.Length; i++)
            {
                // call, callvirt, newobj, ldfld, ldsfld followed by a member reference token (table 0x0A)
                if (il[i] is 0x28 or 0x6F or 0x73 or 0x7B or 0x7E && il[i + 4] == 0x0A)
                {
                    var row = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16);
                    var member = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                    if (member.Parent.Kind == HandleKind.TypeReference)
                    {
                        var parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                        referencedTypes.Add(reader.GetString(parent.Namespace) + "." + reader.GetString(parent.Name));
                    }
                }
            }
        }

        Assert.DoesNotContain("System.Random", referencedTypes);
        Assert.Contains(typeof(RandomNumberGenerator).FullName!, referencedTypes);
        Assert.DoesNotContain(typeof(RandomUtil).GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => f.FieldType == typeof(Random));
    }

    [Fact]
    public void RandomUtil_CharactersAreUniformlyDistributed()
    {
        // a biased or low-entropy generator shows up as a chi-square far above the 61 degrees of freedom of the alphabet
        var counts = new Dictionary<char, int>();
        for (var i = 0; i < 5_000; i++)
        {
            foreach (var c in RandomUtil.GeneratePassword())
            {
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
        }

        var expected = counts.Values.Sum() / 62.0;
        var chiSquare = counts.Values.Sum(v => (v - expected) * (v - expected) / expected);

        Assert.Equal(62, counts.Count);
        Assert.True(chiSquare < 110, $"chi-square {chiSquare:0.0} is too high for 61 degrees of freedom");
    }

    [Fact]
    public void RandomUtil_TwoSequencesDoNotRepeat_EvenWhenStartedTogether()
    {
        // a seeded generator started twice repeats itself
        var first = Enumerable.Range(0, 50).Select(_ => RandomUtil.GenerateResetKey()).ToList();
        var second = Enumerable.Range(0, 50).Select(_ => RandomUtil.GenerateResetKey()).ToList();

        Assert.Empty(first.Intersect(second));
        Assert.Equal(50, first.Distinct().Count());
    }

    // --- MapToJsonObject index bound (R5) -----------------------------------------------------------------------------

    [Fact]
    public void MapToJsonObject_HugeArrayIndex_IsRejectedInsteadOfAllocating()
    {
        var json = new JsonObject();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SerializerEngine.MapToJsonObject(new[] { "arr", "2000000000", "x" }, json, "v"));

        Assert.Contains(SerializerEngine.MaxArrayIndex.ToString(), ex.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => SerializerEngine.ConvertDictionaryToJson(new Dictionary<string, string?> { ["arr_10001_x"] = "v" }));
        Assert.Empty(json);
    }

    [Fact]
    public void MapToJsonObject_IndexAtTheLimit_StillPads()
    {
        var json = new JsonObject();

        SerializerEngine.MapToJsonObject(new[] { "arr", SerializerEngine.MaxArrayIndex.ToString(), "x" }, json, "v");

        Assert.Equal(SerializerEngine.MaxArrayIndex + 1, json["arr"]!.AsArray().Count);
    }

    // --- Populate: ExtraProperties and the predicate (S1, S2) ----------------------------------------------------------

    [Fact]
    public void Populate_ExtraPropertiesObject_IsMergedKeyByKey_WithTheLimits()
    {
        var entity = new Aliased();
        var tooMany = new JsonObject();
        for (var i = 0; i < 101; i++)
        {
            tooMany["k" + i] = i;
        }

        Assert.Throws<ApiException>(() => new JsonObject { ["extraProperties"] = tooMany }.Populate(entity));
        Assert.Throws<ApiException>(() => new JsonObject { ["ExtraProperties"] = new JsonObject { ["big"] = new string('x', 20_000) } }.Populate(new Aliased()));
        Assert.Throws<ApiException>(() => new JsonObject { ["extraProperties"] = new JsonObject { [new string('k', 101)] = 1 } }.Populate(new Aliased()));
    }

    [Fact]
    public void Populate_ExtraPropertiesObject_SmallMergeWorks_AndNeverReplacesTheBag()
    {
        var entity = new Aliased();
        var bag = entity.ExtraProperties;
        entity.ExtraProperties["keep"] = 1;

        new JsonObject { ["extraProperties"] = new JsonObject { ["a"] = 2 } }.Populate(entity);
        new JsonObject { ["extraProperties"] = "not an object" }.Populate(entity);

        Assert.Same(bag, entity.ExtraProperties);
        Assert.Equal(1, entity.ExtraProperties["keep"]);
        Assert.Equal(2L, entity.ExtraProperties["a"]);
    }

    [Fact]
    public void Populate_CustomPredicate_ReplacesTheDefaultRule_AndSeesTheClrName()
    {
        var entity = new Aliased { Name = "n", TenantId = 1 };
        var seen = new List<string>();

        new JsonObject { ["tenant_id"] = 9, ["NAME"] = "changed", ["legacy_code"] = "c" }
            .Populate(entity, name =>
            {
                seen.Add(name);
                return name == nameof(Aliased.TenantId);
            });

        Assert.Equal(1, entity.TenantId); // the alias resolved to TenantId, which the predicate protects
        Assert.Equal("changed", entity.Name);
        Assert.Equal("c", entity.Code);
        Assert.Contains(nameof(Aliased.TenantId), seen);
        Assert.Contains(nameof(Aliased.Name), seen); // the CLR name, not the client's "NAME"
        Assert.DoesNotContain("tenant_id", seen);
    }

    [Fact]
    public void Populate_CustomPredicate_CanUnprotectTheDefaults()
    {
        var entity = new Aliased { IsEnabled = true };

        new JsonObject { ["isEnabled"] = false }.Populate(entity, _ => false);

        Assert.False(entity.IsEnabled);
    }

    // --- property resolution (S2) ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Name", "Name")]
    [InlineData("name", "Name")]
    [InlineData("NAME", "Name")]
    [InlineData("tenant_id", "TenantId")]
    [InlineData("TENANT_ID", "TenantId")]
    [InlineData("tenantid", "TenantId")]
    [InlineData("legacy_code", "Code")]
    public void ResolveProperty_BindsTheWayTheSerializersDo(string clientName, string expected)
    {
        Assert.Equal(expected, EntityPropertyGuard.ResolveProperty(typeof(Aliased), clientName)?.Name);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("tenant id")]
    public void ResolveProperty_UnknownNames_AreNull(string clientName)
    {
        Assert.Null(EntityPropertyGuard.ResolveProperty(typeof(Aliased), clientName));
        Assert.Throws<ArgumentNullException>(() => EntityPropertyGuard.ResolveProperty(null!, "x"));
    }

    // --- legacy shim ---------------------------------------------------------------------------------------------------------

#pragma warning disable CS0618 // the legacy shim is obsolete on purpose
    [Fact]
    public void LegacyStopWatch_ThreeStringConstructor_SetsEverything()
    {
        using var watch = new TTG.Core.TTGStopWatch("module", "ref-1", "c");

        Assert.Equal("module", watch.Module);
        Assert.Equal("ref-1", watch.ReferenceNo);
        Assert.Equal("c", watch.Format);
    }
#pragma warning restore CS0618

    [Fact]
    public void EnsureWithinLimits_CountKeysAndValues()
    {
        var ok = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" };
        var many = Enumerable.Range(0, 101).ToDictionary(i => "k" + i, i => (object?)i);

        EntityPropertyGuard.EnsureWithinLimits(ok);
        EntityPropertyGuard.EnsureWithinLimits(Enumerable.Range(0, 100).ToDictionary(i => "k" + i, i => (object?)i));
        Assert.Throws<ApiException>(() => EntityPropertyGuard.EnsureWithinLimits(many));
        Assert.Throws<ApiException>(() => EntityPropertyGuard.EnsureWithinLimits(new Dictionary<string, object?> { ["big"] = new string('x', 20_000) }));
        Assert.Throws<ArgumentNullException>(() => EntityPropertyGuard.EnsureWithinLimits(null!));
    }
}
