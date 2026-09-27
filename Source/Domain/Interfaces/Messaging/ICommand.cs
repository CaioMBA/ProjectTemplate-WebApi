using Domain.Results;

namespace Domain.Interfaces.Messaging;

#pragma warning disable CA1040
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "TResponse is a phantom type parameter. It is never a member, but it is " +
                    "what lets ISender.Send infer the response type from the request type " +
                    "alone, so the caller writes `await sender.Send(query)` and gets a " +
                    "correctly typed result with no cast and no second generic argument.")]
public interface IRequest<out TResponse>;

public interface ICommand : IRequest<Result>;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
#pragma warning restore CA1040
