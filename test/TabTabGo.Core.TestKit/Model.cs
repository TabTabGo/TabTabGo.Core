using TabTabGo.Core.Entities;

namespace TabTabGo.Core.TestKit;

/// <summary>Test entity with an integer key, a tenant for scope tests and a navigation to a category.</summary>
public class Product : Entity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Description { get; set; }

    public string Tenant { get; set; } = "A";

    /// <summary>Numeric tenant column with aliases, for protection tests.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tenant_id")]
    [Newtonsoft.Json.JsonProperty("tenant_id")]
    public int TenantId { get; set; }

    /// <summary>Hidden from System.Text.Json: server-owned, a patch must not write it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string HiddenStj { get; set; } = "stj";

    /// <summary>Hidden from Newtonsoft.Json: server-owned, a patch must not write it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string HiddenNewtonsoft { get; set; } = "newtonsoft";

    /// <summary>Not a column: derived state a patch must not write.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string HiddenNotMapped { get; set; } = "notmapped";

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
}

/// <summary>Test aggregate parent.</summary>
public class Category : Entity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<Product> Products { get; set; } = new();
}

/// <summary>Test aggregate child.</summary>
public class OrderLine : Entity
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }
}

/// <summary>Test entity with a composite key.</summary>
public class CompositeKeyEntity : Entity
{
    public int FirstId { get; set; }

    public int SecondId { get; set; }

    public string Label { get; set; } = string.Empty;
}

/// <summary>Test entity with a Guid key.</summary>
public class GuidEntity : Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>View model of <see cref="Product"/>.</summary>
public class ProductViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

/// <summary>Create request of <see cref="Product"/>.</summary>
public class ProductCreateRequest
{
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Tenant { get; set; } = "A";
}

/// <summary>Update request of <see cref="Product"/>.</summary>
public class ProductUpdateRequest
{
    public string Name { get; set; } = string.Empty;
}
