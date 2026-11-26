using System.Reflection;

namespace TabTabGo.Core.Test.Infrastructure;

/// <summary>
/// Compile- and shape-compat for the restored TabTabGo.Core.Infrastructure.Data contracts
/// (identical to the published 8.0.0-alpha-2 package).
/// </summary>
public class InfrastructureCompatTests
{
    public class Order
    {
        public int Id { get; set; }
    }

    /// <summary>Consumer code written the way downstream apps use the interfaces.</summary>
    private static async Task<int> Consume(IUnitOfWork unitOfWork)
    {
        IGenericRepository<Order, int> repository = unitOfWork.Repository<Order, int>();
        IGenericReadRepository<Order, int> reader = repository;
        _ = reader.GetByKey(1);
        await repository.InsertAsync(new Order());
        unitOfWork.BeginTransaction();
        var saved = await unitOfWork.SaveChangesAsync();
        unitOfWork.Commit();
        return saved;
    }

    [Fact]
    public async Task Consumer_UsingInfrastructureData_CompilesAndRunsAgainstAFake()
    {
        var uow = Fake.Create<IUnitOfWork>(m => m.Name switch
        {
            "Repository" => Fake.Create<IGenericRepository<Order, int>>(r => r.Name switch
            {
                "InsertAsync" => Task.FromResult<Order>(new Order()),
                _ => null
            }),
            "SaveChangesAsync" => Task.FromResult(7),
            _ => null
        });

        Assert.Equal(7, await Consume(uow));
    }

    [Theory]
    [InlineData(typeof(IGenericReadRepository<,>), 21)]
    [InlineData(typeof(IGenericRepository<,>), 15)]
    [InlineData(typeof(IUnitOfWork), 11)]
    public void Contracts_KeepNamespaceAndDeclaredMemberCount(Type type, int declaredMethods)
    {
        Assert.Equal("TabTabGo.Core.Infrastructure.Data", type.Namespace);
        Assert.Equal("TabTabGo.Core.Infrastructure", type.Assembly.GetName().Name);
        Assert.Equal(declaredMethods, type.GetMethods().Count(m => m.DeclaringType == type));
    }

    [Fact]
    public void GenericReadRepository_KeepsAlpha2KeyShape()
    {
        // alpha-2: TKey is invariant and GetByKey takes an object? key.
        var readRepo = typeof(IGenericReadRepository<,>);
        Assert.False(readRepo.GetGenericArguments()[1].GenericParameterAttributes.HasFlag(
            GenericParameterAttributes.Contravariant));
        Assert.Equal(typeof(object), readRepo.GetMethod("GetByKey")!.GetParameters()[0].ParameterType);
    }

    public class Fake : DispatchProxy
    {
        private Func<MethodInfo, object?> _answer = _ => null;

        public static T Create<T>(Func<MethodInfo, object?> answer) where T : class
        {
            var proxy = DispatchProxy.Create<T, Fake>();
            ((Fake)(object)proxy)._answer = answer;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = _answer(targetMethod!);
            if (result != null || targetMethod!.ReturnType == typeof(void))
            {
                return result;
            }

            return targetMethod.ReturnType == typeof(Task)
                ? Task.CompletedTask
                : targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }
}
