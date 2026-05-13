using MediatR;

namespace IIoT.Edge.SharedKernel.Messaging;

/// <summary>
/// 写操作命令基接口。
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>
/// 写操作命令处理器基接口。
/// </summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
