using System.Reflection;
using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.NonUiRegressionTests;

internal static class EntityIdTestHelper
{
    public static TEntity WithId<TEntity>(this TEntity entity, int id)
        where TEntity : class, IEntity<int>
    {
        SetId(entity, id);
        return entity;
    }

    public static void SetId<TEntity>(TEntity entity, int id)
        where TEntity : class, IEntity<int>
    {
        var property = entity.GetType().GetProperty(
            nameof(IEntity<int>.Id),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(nonPublic: true);

        if (setter is null)
        {
            throw new InvalidOperationException($"实体 {entity.GetType().Name} 不支持测试主键赋值。");
        }

        setter.Invoke(entity, [id]);
    }
}
