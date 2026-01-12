using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameScreenTranslator.Core.App.Flows;

/// <summary>
/// Small pipeline abstraction to compose reusable operation steps.
/// Keeps "flow code" mostly declarative and avoids duplicated boilerplate.
/// </summary>
public interface IOperationStep<TContext>
{
    Task ExecuteAsync(TContext ctx, CancellationToken cancellationToken);
}

/// <summary>
/// Runs steps in order. Error handling is intentionally left to the caller so
/// flows can decide what to swallow vs rethrow (and how to log).
/// </summary>
public sealed class OperationPipeline<TContext>
{
    private readonly IReadOnlyList<IOperationStep<TContext>> _steps;

    public OperationPipeline(IReadOnlyList<IOperationStep<TContext>> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public OperationPipeline(params IOperationStep<TContext>[] steps)
        : this((IReadOnlyList<IOperationStep<TContext>>)steps)
    {
    }

    public async Task RunAsync(TContext ctx, CancellationToken cancellationToken)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        foreach (var step in _steps)
            await step.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
    }
}
