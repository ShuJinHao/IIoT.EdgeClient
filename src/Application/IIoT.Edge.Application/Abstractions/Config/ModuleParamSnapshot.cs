using System.Globalization;

namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 插件三类参数的一次性内存快照。
/// 快照只保存已经加载好的参数值，后续泛型读取不会再次访问数据库或缓存服务。
/// </summary>
public sealed class ModuleParamSnapshot<TMes, TCloud, TBusiness>(
    string moduleId,
    ModuleParamGroup<TMes> mes,
    ModuleParamGroup<TCloud> cloud,
    ModuleParamGroup<TBusiness> business)
    where TMes : struct, Enum
    where TCloud : struct, Enum
    where TBusiness : struct, Enum
{
    private readonly ModuleParamGroup<TMes> _mes = mes;
    private readonly ModuleParamGroup<TCloud> _cloud = cloud;
    private readonly ModuleParamGroup<TBusiness> _business = business;

    public string ModuleId { get; } = moduleId;

    /// <summary>
    /// 读取 MES 参数，调用方用泛型声明期望类型，例如 Mes&lt;string&gt;(MesParam.服务地址)。
    /// </summary>
    public T Mes<T>(TMes key) => _mes.Get<T>(key);

    /// <summary>
    /// 读取云端参数，调用方用泛型声明期望类型，例如 Cloud&lt;bool&gt;(CloudParam.启用)。
    /// </summary>
    public T Cloud<T>(TCloud key) => _cloud.Get<T>(key);

    /// <summary>
    /// 读取插件业务参数，调用方用泛型声明期望类型，例如 Business&lt;bool&gt;(BusinessParam.启用托盘码重码验证)。
    /// </summary>
    public T Business<T>(TBusiness key) => _business.Get<T>(key);
}

/// <summary>
/// 单个参数分类的内存读取器。
/// 只提供泛型读取入口，避免调用方在 Bool/String/Int/Decimal 多套方法之间选择。
/// </summary>
public sealed class ModuleParamGroup<TEnum>(
    string moduleId,
    ModuleParamCategory category,
    IReadOnlyDictionary<TEnum, string> configuredValues,
    IReadOnlyDictionary<TEnum, string?> defaults,
    IReadOnlyDictionary<TEnum, ParamValueKind> valueKinds,
    Action<string>? warn)
    where TEnum : struct, Enum
{
    public T Get<T>(TEnum key)
    {
        if (!valueKinds.TryGetValue(key, out var declaredKind))
        {
            throw new InvalidOperationException($"插件参数未注册：{moduleId}/{category}/{key}。");
        }

        var requestedType = typeof(T);
        if (!IsSupportedType(requestedType))
        {
            throw new InvalidOperationException($"插件参数 {moduleId}/{category}/{key} 不支持读取为 {requestedType.Name}。");
        }

        if (!MatchesDeclaredKind(declaredKind, requestedType))
        {
            throw new InvalidOperationException(
                $"插件参数类型不匹配：{moduleId}/{category}/{key} 声明为 {declaredKind}，不能读取为 {requestedType.Name}。");
        }

        var hasConfiguredValue = configuredValues.TryGetValue(key, out var configured);
        var rawValue = hasConfiguredValue
            ? configured
            : defaults.TryGetValue(key, out var declaredDefault)
                ? declaredDefault
                : null;

        if (TryConvert(rawValue, out T converted))
        {
            return converted;
        }

        if (hasConfiguredValue)
        {
            warn?.Invoke($"插件参数 {moduleId}/{category}/{key} 的值“{configured}”无法转换为 {requestedType.Name}，已回退默认值。");
        }

        var defaultValue = defaults.TryGetValue(key, out var fallbackDefault)
            ? fallbackDefault
            : null;
        if (TryConvert(defaultValue, out converted))
        {
            return converted;
        }

        throw new InvalidOperationException(
            $"插件参数默认值无效：{moduleId}/{category}/{key} 无法转换为 {requestedType.Name}。");
    }

    private static bool IsSupportedType(Type type)
        => type == typeof(string)
            || type == typeof(bool)
            || type == typeof(int)
            || type == typeof(decimal);

    private static bool MatchesDeclaredKind(ParamValueKind kind, Type type)
        => kind switch
        {
            ParamValueKind.String => type == typeof(string),
            ParamValueKind.Bool => type == typeof(bool),
            ParamValueKind.Int => type == typeof(int),
            ParamValueKind.Decimal => type == typeof(decimal),
            _ => false
        };

    private static bool TryConvert<T>(string? value, out T converted)
    {
        var targetType = typeof(T);
        if (targetType == typeof(string))
        {
            converted = (T)(object)(value ?? string.Empty);
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (TryParseBool(value, out var boolValue))
            {
                converted = (T)(object)boolValue;
                return true;
            }

            converted = default!;
            return false;
        }

        if (targetType == typeof(int)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            converted = (T)(object)intValue;
            return true;
        }

        if (targetType == typeof(decimal)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            converted = (T)(object)decimalValue;
            return true;
        }

        converted = default!;
        return false;
    }

    private static bool TryParseBool(string? value, out bool parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = false;
            return false;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out parsed))
        {
            return true;
        }

        if (normalized is "1" or "是" or "启用" or "开启")
        {
            parsed = true;
            return true;
        }

        if (normalized is "0" or "否" or "禁用" or "关闭")
        {
            parsed = false;
            return true;
        }

        parsed = false;
        return false;
    }
}
