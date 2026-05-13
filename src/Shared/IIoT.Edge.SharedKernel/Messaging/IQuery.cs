using MediatR;

namespace IIoT.Edge.SharedKernel.Messaging;

/// <summary>
/// 读操作查询基接口。
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>
/// 读操作查询处理器基接口。
/// </summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
